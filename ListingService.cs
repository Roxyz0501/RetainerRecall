using System.Diagnostics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace RetainerRecall;

internal sealed unsafe class ListingService : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly IAddonLifecycle lifecycle;
    private readonly IKeyState keys;
    private readonly IPlayerState player;
    private readonly IClientState client;
    private readonly IGameGui gui;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly Func<bool> recallBusy;
    private readonly ListingGamePort port;
    private delegate void OpenContextDelegate(AgentInventoryContext* agent, InventoryType inventory, int slot, int argument, uint owner);
    private Hook<OpenContextDelegate>? contextHook;
    public readonly ListingRun Run;
    public readonly SessionPrices Prices = new();
    private Stock? requested;
    private ListingFrame? requestedFrame;
    private bool locked;
    private readonly List<PriceCapture> captures = [];
    private sealed record PriceCapture(ListingFrame Before, uint ItemId, int Quantity, int Price, double ConfirmedAt, double Deadline);
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public string QuantitySource { get; private set; } = "未取得";
    public bool Busy => Run.Running || requested != null;

    public ListingService(IDalamudPluginInterface pi, IAddonLifecycle lifecycle, IKeyState keys, IPlayerState player, IClientState client, IGameGui gui, IChatGui chat, IPluginLog log, ICondition condition, IDataManager data, IGameInteropProvider interop, Configuration config, Func<bool> recallBusy)
    {
        this.pi = pi; this.lifecycle = lifecycle; this.keys = keys; this.player = player; this.client = client;
        this.gui = gui; this.chat = chat; this.log = log; this.config = config; this.recallBusy = recallBusy;
        port = new(gui, player, condition, data); Run = new(port);
        Prices.SetCharacter(player.IsLoaded ? player.ContentId : 0);
        try
        {
            var address = (nint)AgentInventoryContext.MemberFunctionPointers.OpenForItemSlot;
            if (address == 0) throw new InvalidOperationException("所持品メニューの関数を確認できません");
            contextHook = interop.HookFromAddress<OpenContextDelegate>(address, ContextOpened);
            contextHook.Enable();
        }
        catch (Exception e)
        {
            contextHook?.Dispose(); contextHook = null;
            Error("連続出品ショートカットを初期化できません。ログを確認してください", e);
        }
        lifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "RetainerSell", BeforeSellEvent);
        lifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerSell", SaleClosing);
        lifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerSellList", ListClosed);
        client.Logout += Logout;
    }
    private void Logout(int type, int code) { Stop("ログアウトしました"); Prices.Clear(); captures.Clear(); }
    private void ListClosed(AddonEvent type, AddonArgs args) { Stop("販売リストが閉じたため連続出品を停止しました"); captures.Clear(); }
    private void ContextOpened(AgentInventoryContext* agent, InventoryType inventory, int slot, int argument, uint owner)
    {
        // PostSetup runs inside this call, before the inventory target may be finalized.
        // Observe input before the original call, then inspect its completed ordinary menu.
        var trigger = false;
        try { trigger = config.EnableListing && !Busy && !recallBusy() && ListingGamePort.AllowedInventory(inventory) && ModifierHeld(); }
        catch (Exception e) { log.Warning(e, "Could not read listing shortcut"); }
        contextHook!.Original(agent, inventory, slot, argument, owner);
        if (!trigger) return;
        try
        {
            if (!port.Visible("RetainerSellList")) return;
            var source = port.ContextSource(inventory, slot, owner);
            if (source == null) { Error("出品対象を確認できません。販売リストと所持品を開き、アーマリーチェストも一度開いてから再実行してください"); return; }
            Prices.SetCharacter(player.IsLoaded ? player.ContentId : 0);
            if (!Prices.TryGet(source.ItemId, out _)) { Error("このアイテムはログイン後の出品履歴がありません。先に通常の操作で価格を確定して出品してください"); return; }
            requested = source;
            requestedFrame = port.Read();
            log.Information("Listing shortcut accepted for inventory {Inventory}, slot {Slot}", inventory, slot);
        }
        catch (Exception e) { Error("ショートカットの対象を確認できません", e); }
    }
    private bool ModifierHeld() => keys[config.ListingKey switch
    {
        ListingKey.LeftAlt => VirtualKey.LMENU, ListingKey.RightControl => VirtualKey.RCONTROL,
        ListingKey.LeftControl => VirtualKey.LCONTROL, ListingKey.RightShift => VirtualKey.RSHIFT,
        ListingKey.LeftShift => VirtualKey.LSHIFT, _ => VirtualKey.RMENU,
    }];

    private void BeforeSellEvent(AddonEvent type, AddonArgs args)
    {
        try
        {
            var evt = (AddonReceiveEventArgs)args;
            var addon = (AddonRetainerSell*)args.Addon.Address;
            if (addon == null || addon->Confirm == null || addon->Confirm->OwnerNode == null || addon->Quantity == null || addon->AskingPrice == null || (byte)evt.AtkEventType != (byte)AtkEventType.ButtonClick) return;
            var registered = addon->Confirm->OwnerNode->AtkEventManager.Event;
            var matched = false;
            for (var i = 0; registered != null && i < 32; i++, registered = registered->NextEvent)
                if (registered->State.EventType == AtkEventType.ButtonClick && registered->Param == evt.EventParam && registered->Listener == (AtkEventListener*)addon) { matched = true; break; }
            if (!matched || !addon->Confirm->IsEnabled) return;
            CaptureSale(addon);
        }
        catch (Exception e) { log.Warning(e, "Could not observe listing confirmation"); }
    }
    private void SaleClosing(AddonEvent type, AddonArgs args)
    {
        // Also cover UI callback-based confirmation. Closing/cancelling alone never teaches a price:
        // a matching successful market update must still arrive within the same session.
        try { CaptureSale((AddonRetainerSell*)args.Addon.Address); }
        catch (Exception e) { log.Warning(e, "Could not observe closing sale dialog"); }
    }
    private void CaptureSale(AddonRetainerSell* addon)
    {
            if (addon == null || addon->Quantity == null || addon->AskingPrice == null) return;
            var before = port.Read();
            if (before == null || addon->Quantity->Value <= 0 || addon->AskingPrice->Value <= 0) return;
            var agent = AgentRetainer.Instance();
            if (agent == null || agent->RetainerSellAddonId != addon->Id || agent->SellItemInventorySlot < 0) return;
            var manager = InventoryManager.Instance();
            var bag = manager == null ? null : manager->GetInventoryContainer(agent->SellItemInventoryType);
            if (bag == null || !bag->IsLoaded || agent->SellItemInventorySlot >= bag->Size) return;
            var item = bag->GetInventorySlot(agent->SellItemInventorySlot);
            if (item == null || item->IsSymbolic || item->ItemId == 0) return;
            Prices.SetCharacter(before.Character);
            var now = Now;
            captures.Add(new(before, item->ItemId, addon->Quantity->Value, addon->AskingPrice->Value, now, now + 15));
            if (captures.Count > 20) captures.RemoveAt(0);
    }

    private bool MarketbuddyLoaded => pi.InstalledPlugins.Any(x => x.InternalName == "Marketbuddy" && x.IsLoaded);
    private int ReadLimit()
    {
        if (!config.UseMarketbuddyLimit || !MarketbuddyLoaded) { QuantitySource = "本プラグインの数量設定"; return Math.Clamp(config.ListingStackLimit, 1, 9999); }
        // The overlay saves both fields on every change. Read only these saved settings; never modify them.
        var path = Path.Combine(pi.ConfigFile.DirectoryName!, "Marketbuddy.json");
        var value = StackLimitPolicy.ReadMarketbuddy(File.ReadAllText(path));
        QuantitySource = $"Marketbuddy: {value}個（制限オフ時は99個）";
        return value;
    }
    private void LockMarketbuddy()
    {
        if (!MarketbuddyLoaded) return;
        pi.GetIpcSubscriber<string, bool>("Marketbuddy.Lock").InvokeFunc("RetainerRecall");
        locked = true;
        if (!pi.GetIpcSubscriber<string, bool>("Marketbuddy.IsLocked").InvokeFunc("RetainerRecall")) throw new InvalidOperationException("Marketbuddyの価格操作を一時停止できません");
    }
    private void UnlockMarketbuddy()
    {
        if (!locked) return;
        try { pi.GetIpcSubscriber<string, bool>("Marketbuddy.Unlock").InvokeFunc("RetainerRecall"); }
        catch (Exception e) { log.Warning(e, "Could not release Marketbuddy lock"); }
        finally { locked = false; }
    }
    public void Tick()
    {
        try
        {
            var character = player.IsLoaded ? player.ContentId : 0;
            Prices.SetCharacter(character);
            if (character == 0) { Stop("ログアウトしました"); captures.Clear(); return; }
            var frame = captures.Count != 0 || requested != null ? port.Read() : null;
            foreach (var capture in captures.ToArray())
            {
                if (Now >= capture.Deadline || frame == null || !ListingRules.SameSession(capture.Before, frame)) { captures.Remove(capture); continue; }
                var item = ListingRules.ConfirmedPriceItem(capture.Before, frame, capture.Quantity, capture.Price);
                if (item == capture.ItemId) { Prices.Remember(item.Value, capture.Price, capture.ConfirmedAt); captures.Remove(capture); }
            }
            if (requested is { } source)
            {
                requested = null;
                if (recallBusy()) throw new InvalidOperationException("回収が実行中です");
                if (port.Visible("RetainerSell")) throw new InvalidOperationException("他プラグインのショートカットと競合しました。別のキーを設定してください");
                if (frame == null || requestedFrame == null || !ListingRules.SameSession(frame, requestedFrame) || !frame.Stock.Contains(source) || !Prices.TryGet(source.ItemId, out var price)) throw new InvalidOperationException("対象アイテム・リテイナー・保存価格が変わりました");
                var limit = ReadLimit();
                LockMarketbuddy();
                port.DismissInitialMenu();
                Run.Start(source.ItemId, source.Inventory >= 10000, price, limit, config.ListingDelaySeconds, Now);
            }
            var wasRunning = Run.Running;
            Run.Tick(Now);
            if (wasRunning && !Run.Running) chat.Print($"[Retainer Listing Helper] {Run.Status}");
            if (!Busy) UnlockMarketbuddy();
        }
        catch (Exception e) { Error(e.Message, e); }
    }
    private void Error(string message, Exception? error = null)
    {
        Stop(message);
        chat.PrintError($"[Retainer Listing Helper] {message}");
        if (error != null) log.Warning(error, "Listing automation stopped");
    }
    public void Stop(string message = "連続出品を停止しました") { requested = null; requestedFrame = null; Run.Stop(message); UnlockMarketbuddy(); }
    public void Dispose()
    {
        Stop(); Prices.Clear(); captures.Clear();
        contextHook?.Dispose(); contextHook = null;
        lifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "RetainerSell", BeforeSellEvent);
        lifecycle.UnregisterListener(AddonEvent.PreFinalize, "RetainerSell", SaleClosing);
        lifecycle.UnregisterListener(AddonEvent.PreFinalize, "RetainerSellList", ListClosed);
        client.Logout -= Logout;
    }
}

using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace RetainerRecall;

internal sealed unsafe class ListingGamePort(IGameGui gui, IPlayerState player, ICondition condition, IDataManager data) : IListingPort
{
    public bool FromRetainer;
    public uint OwnerAddonId;
    private nint ownerAddress;
    private nint menuAddress;
    private static readonly InventoryType[] PlayerBags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly InventoryType[] RetainerBags = [InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4, InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7];
    public bool Interference => new[] { "SelectYesno", "InputNumeric", "SelectString", "ItemSearchResult", "ItemHistory" }.Any(Visible)
        || FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->WindowInactive;
    public bool Visible(string name) { var addon = gui.GetAddonByName<AtkUnitBase>(name); return addon != null && addon->IsVisible; }
    private static bool Loaded(InventoryContainer* bag) => bag != null && bag->IsLoaded && bag->Items != null && bag->Size is > 0 and <= 200;
    public static bool AllowedInventory(InventoryType inventory) => PlayerBags.Contains(inventory) || RetainerBags.Contains(inventory);

    public ListingFrame? Read()
    {
        var window = gui.GetAddonByName<AtkUnitBase>("RetainerSellList");
        if (!player.IsLoaded || player.ContentId == 0 || !condition[ConditionFlag.OccupiedSummoningBell] || window == null || !window->IsVisible || !window->IsReady) return null;
        var manager = InventoryManager.Instance(); var retainers = RetainerManager.Instance();
        if (manager == null || retainers == null || !retainers->IsReady || retainers->LastSelectedRetainerId == 0) return null;
        var market = manager->GetInventoryContainer(InventoryType.RetainerMarket);
        if (!Loaded(market) || market->Size != 20) return null;
        List<Offer> offers = [];
        for (var i = 0; i < market->Size; i++)
        {
            var value = market->GetInventorySlot(i);
            if (value == null || value->IsSymbolic) return null;
            if (value->ItemId == 0) continue;
            if (value->Quantity <= 0 || value->Slot != i) return null;
            offers.Add(new(i, value->ItemId, (byte)value->Flags, value->Quantity, checked((long)manager->GetRetainerMarketPrice((short)i))));
        }
        List<Stock> stocks = [];
        foreach (var type in FromRetainer ? RetainerBags : PlayerBags)
        {
            var bag = manager->GetInventoryContainer(type);
            if (!Loaded(bag)) return null;
            for (var i = 0; i < bag->Size; i++)
            {
                var value = bag->GetInventorySlot(i);
                if (value == null || value->IsSymbolic) return null;
                if (value->ItemId == 0 || value->Quantity == 0) continue;
                var row = data.GetExcelSheet<Item>().GetRowOrDefault(value->ItemId);
                if (value->Quantity < 0 || value->Slot != i || row == null) return null;
                stocks.Add(new((int)type, i, value->ItemId, (byte)value->Flags, value->Quantity, checked((int)row.Value.StackSize)));
            }
        }
        return new(player.ContentId, retainers->LastSelectedRetainerId, (nint)window, stocks.ToArray(), offers.ToArray(), market->Size);
    }

    public Stock? ContextSource()
    {
        var agent = AgentInventoryContext.Instance();
        if (agent == null || !AllowedInventory(agent->TargetInventoryId)) return null;
        FromRetainer = RetainerBags.Contains(agent->TargetInventoryId);
        var frame = Read();
        if (frame == null) return null;
        var source = frame.Stock.SingleOrDefault(x => x.Inventory == (int)agent->TargetInventoryId && x.Slot == agent->TargetInventorySlotId);
        if (source == null || agent->TargetInventorySlot == null || agent->TargetInventorySlot->ItemId != source.ItemId) return null;
        OwnerAddonId = agent->OwnerAddonId;
        var owner = Owner();
        if (owner == null || !owner->IsVisible || !owner->IsReady) return null;
        ownerAddress = (nint)owner;
        return source;
    }
    private AtkUnitBase* Owner()
    {
        var stage = AtkStage.Instance();
        return stage == null || stage->RaptureAtkUnitManager == null || OwnerAddonId > ushort.MaxValue ? null : stage->RaptureAtkUnitManager->GetAddonById((ushort)OwnerAddonId);
    }
    private void ValidateSource(Stock source)
    {
        if (Read()?.Stock.Contains(source) != true) throw new InvalidOperationException("出品対象のスロットが変わりました");
        var owner = Owner();
        if (owner == null || (nint)owner != ownerAddress || !owner->IsVisible) throw new InvalidOperationException("元の所持品画面が閉じられました");
    }
    private static bool TargetMatches(Stock source)
    {
        var agent = AgentInventoryContext.Instance();
        return agent != null && (int)agent->TargetInventoryId == source.Inventory && agent->TargetInventorySlotId == source.Slot &&
            agent->TargetInventorySlot != null && agent->TargetInventorySlot->ItemId == source.ItemId && (byte)agent->TargetInventorySlot->Flags == source.Flags;
    }
    public void DismissInitialMenu()
    {
        var menu = gui.GetAddonByName<AtkUnitBase>("ContextMenu");
        if (menu != null && menu->IsVisible) menu->Close(true);
    }
    public void OpenMenu(Stock source)
    {
        ValidateSource(source);
        if (Visible("ContextMenu") || Visible("RetainerSell")) throw new InvalidOperationException("別のメニューが開かれています");
        AgentInventoryContext.Instance()->OpenForItemSlot((InventoryType)source.Inventory, source.Slot, 0, OwnerAddonId);
        menuAddress = (nint)gui.GetAddonByName<AtkUnitBase>("ContextMenu");
    }
    public bool SelectSale(Stock source)
    {
        ValidateSource(source);
        var menu = gui.GetAddonByName<AtkUnitBase>("ContextMenu");
        if (menu == null || !menu->IsVisible || !menu->IsReady) return false;
        if ((nint)menu != menuAddress || !TargetMatches(source)) throw new InvalidOperationException("別のアイテムのメニューが開かれました");
        var label = data.GetExcelSheet<Addon>().GetRow(99).Text.ToString();
        NativeMenu.Select(menu, label);
        return true;
    }
    public SaleDialog? Dialog(Stock source)
    {
        ValidateSource(source);
        var addon = gui.GetAddonByName<AddonRetainerSell>("RetainerSell");
        if (addon == null || !addon->IsReady || !addon->IsVisible) return null;
        var agent = AgentRetainer.Instance();
        if (agent == null || (int)agent->SellItemInventoryType != source.Inventory || agent->SellItemInventorySlot != source.Slot || agent->RetainerSellAddonId != addon->Id || addon->Quantity == null || addon->AskingPrice == null || addon->Confirm == null) throw new InvalidOperationException("出品画面と対象アイテムの対応を確認できません");
        return new((nint)addon, addon->Quantity->Value, addon->AskingPrice->Value, addon->Quantity->Data.Max, addon->AskingPrice->Data.Max);
    }
    public void Fill(Stock source, SaleDialog dialog, int quantity, int price)
    {
        var current = Dialog(source);
        if (current == null || current.Address != dialog.Address || quantity <= 0 || quantity > current.MaxQuantity || price <= 0 || price > current.MaxPrice) throw new InvalidOperationException("出品入力の範囲が不正です");
        var addon = (AddonRetainerSell*)current.Address;
        // Normal numeric-input UI callbacks; no inventory mutation or MoveToRetainerMarket call.
        NativeMenu.Callback((AtkUnitBase*)addon, 3, quantity);
        NativeMenu.Callback((AtkUnitBase*)addon, 2, price);
    }
    public void Confirm(Stock source, SaleDialog dialog, int quantity, int price)
    {
        var current = Dialog(source);
        if (current == null || current.Address != dialog.Address || current.Price != price || current.Quantity != quantity) throw new InvalidOperationException("出品確定直前の価格・数量が一致しません");
        var addon = (AddonRetainerSell*)current.Address;
        if (!addon->Confirm->IsEnabled || addon->Confirm->OwnerNode == null || addon->AtkValuesCount < 9 || addon->AtkValues[5].Int != price || addon->AtkValues[8].Int != quantity) throw new InvalidOperationException("出品ボタンが無効か、表示値と確定値が一致しません");
        var agent = AgentRetainer.Instance();
        if (agent == null || agent->SellItemUnitPrice != price || agent->SellItemQuantity != quantity) throw new InvalidOperationException("ゲーム内部の確定価格・数量が一致しません");
        // Dispatch the actual registered click event instead of inventing an event number.
        var evt = addon->Confirm->OwnerNode->AtkEventManager.Event;
        for (var i = 0; evt != null && i < 32; i++, evt = evt->NextEvent)
        {
            if (evt->State.EventType != AtkEventType.ButtonClick || evt->Listener != (AtkEventListener*)addon) continue;
            addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt);
            return;
        }
        throw new InvalidOperationException("出品ボタンの通常クリックイベントが見つかりません");
    }
}

using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace RetainerRecall;

internal sealed unsafe class GamePort(IGameGui gui, IPlayerState player, IDataManager data) : IRecallPort
{
    private nint ownedMenu;
    private int ownedRow = -1;
    public AtkUnitBase* Window => gui.GetAddonByName<AtkUnitBase>("RetainerSellList");
    public bool IsBusy => new[] { "ContextMenu", "InventoryContext", "SelectYesno", "InputNumeric", "RetainerSell", "SelectString" }
        .Any(name => { var addon = gui.GetAddonByName<AtkUnitBase>(name); return addon != null && addon->IsVisible && !(name == "ContextMenu" && (nint)addon == ownedMenu); });

    public Snapshot? Read()
    {
        var window = Window;
        var manager = InventoryManager.Instance();
        var retainer = RetainerManager.Instance();
        if (!player.IsLoaded || player.ContentId == 0 || window == null || !window->IsVisible || manager == null || retainer == null || !retainer->IsReady || retainer->LastSelectedRetainerId == 0) return null;
        var container = manager->GetInventoryContainer(InventoryType.RetainerMarket);
        if (!Valid(container) || container->Size > 20) return null;
        List<Listing> listings = [];
        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (item == null || item->IsSymbolic) return null;
            if (item->ItemId == 0) continue;
            if (item->Quantity <= 0 || item->Slot != i || item->Container != InventoryType.RetainerMarket) return null;
            listings.Add(new(i, item->ItemId, item->Quantity, (byte)item->Flags));
        }
        return new(retainer->LastSelectedRetainerId, player.ContentId, (nint)window, listings.ToArray());
    }

    private static bool Valid(InventoryContainer* container) => container != null && container->IsLoaded && container->Items != null && container->Size > 0 && container->Size <= 200;
    private static InventoryType[] Bags(Destination target) => target == Destination.Player ? InventoryScopes.PlayerBags : InventoryScopes.RetainerBags;

    public bool HasSpace(Destination target)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return false;
        var free = false;
        foreach (var bag in Bags(target))
        {
            var container = manager->GetInventoryContainer(bag);
            if (!Valid(container)) return false;
            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item == null) return false;
                if (item->ItemId == 0) free = true;
            }
        }
        return free;
    }

    public long Count(Destination target, Listing listing)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return -1;
        long count = 0;
        // The normal menu may return equipment to the Armoury Chest according to game settings.
        // Count both possible destinations, including when a full chest falls back to player bags.
        var destinations = target == Destination.Player ? InventoryScopes.PlayerItems : InventoryScopes.RetainerBags;
        foreach (var bag in destinations.Append(target == Destination.Player ? InventoryType.Crystals : InventoryType.RetainerCrystals))
        {
            var container = manager->GetInventoryContainer(bag);
            if (!Valid(container)) return -1;
            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item == null || item->IsSymbolic) return -1;
                if (item->ItemId == listing.ItemId && (byte)item->Flags == listing.Flags) count += item->Quantity;
            }
        }
        return count;
    }

    private void Validate(Listing listing)
    {
        var snapshot = Read();
        if (snapshot == null || !snapshot.Listings.Contains(listing) || IsBusy)
            throw new InvalidOperationException("回収直前の状態確認に失敗しました");
    }
    public void OpenRecallMenu(Listing listing)
    {
        Validate(listing);
        var agent = AgentRetainer.Instance();
        if (agent == null || agent->SellListEntryCount is < 1 or > 20 || agent->RetainerSellListAddonId != Window->Id) throw new InvalidOperationException("販売リストの対応を確認できません");
        ownedRow = -1;
        for (var row = 0; row < agent->SellListEntryCount; row++)
        {
            var entry = agent->SellListEntries[row];
            var itemId = entry.ItemId >= 1000000 ? entry.ItemId - 1000000 : entry.ItemId;
            if (entry.InventorySlot == listing.Slot && itemId == listing.ItemId && entry.Quantity == listing.Quantity) { ownedRow = row; break; }
        }
        if (ownedRow < 0) throw new InvalidOperationException("出品スロットに対応する販売行がありません");
        // Normal sell-list row context-menu callback; row order is not inventory slot order.
        NativeMenu.Callback(Window, 0, ownedRow, 1);
        ownedMenu = (nint)gui.GetAddonByName<AtkUnitBase>("ContextMenu");
    }
    public bool SelectRecall(Destination target, Listing listing)
    {
        Validate(listing);
        var menu = gui.GetAddonByName<AtkUnitBase>("ContextMenu");
        if (menu == null || !menu->IsVisible || !menu->IsReady) return false;
        var agent = AgentRetainer.Instance();
        if ((nint)menu != ownedMenu || agent == null || agent->ContextMenuIndex != ownedRow || ownedRow < 0 || ownedRow >= agent->SellListEntryCount || agent->SellListEntries[ownedRow].InventorySlot != listing.Slot)
            throw new InvalidOperationException("取り下げメニューの対象が変わりました");
        var context = AgentContext.Instance();
        if (context == null || context->OwnerAddon != Window->Id) throw new InvalidOperationException("取り下げメニューの所有画面が一致しません");
        if (!HasSpace(target)) throw new InvalidOperationException("移動先の空きがありません");
        var label = data.GetExcelSheet<Addon>().GetRow(target == Destination.Player ? 976u : 958u).Text.ToString();
        NativeMenu.Select(menu, label);
        return true;
    }
    public void ResetMenu() { ownedMenu = 0; ownedRow = -1; }
}

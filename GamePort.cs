using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace RetainerRecall;

internal sealed unsafe class GamePort(IGameGui gui, IPlayerState player) : IRecallPort
{
    private static readonly InventoryType[] PlayerBags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly InventoryType[] RetainerBags = [InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4, InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7];
    public AtkUnitBase* Window => gui.GetAddonByName<AtkUnitBase>("RetainerSellList");
    public bool IsBusy => new[] { "ContextMenu", "InventoryContext", "SelectYesno", "InputNumeric", "RetainerSell", "SelectString" }
        .Any(name => { var addon = gui.GetAddonByName<AtkUnitBase>(name); return addon != null && addon->IsVisible; });

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
    private static InventoryType[] Bags(Destination target) => target == Destination.Player ? PlayerBags : RetainerBags;

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
        foreach (var bag in Bags(target).Append(target == Destination.Player ? InventoryType.Crystals : InventoryType.RetainerCrystals))
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

    public void Move(Destination target, Listing listing)
    {
        // Validate again at the native boundary. Never construct packets or write inventory memory.
        var snapshot = Read();
        if (snapshot == null || !snapshot.Listings.Contains(listing) || IsBusy || !HasSpace(target))
            throw new InvalidOperationException("回収直前の状態確認に失敗しました");
        var manager = InventoryManager.Instance();
        if (target == Destination.Player)
            manager->MoveFromRetainerMarketToPlayerInventory(InventoryType.RetainerMarket, checked((ushort)listing.Slot), checked((uint)listing.Quantity));
        else
            manager->MoveFromRetainerMarketToRetainerInventory(InventoryType.RetainerMarket, checked((ushort)listing.Slot), checked((uint)listing.Quantity));
        // Return-code semantics are not assumed; source removal plus destination increment is the acknowledgement.
    }
}

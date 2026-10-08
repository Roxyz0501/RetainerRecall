using FFXIVClientStructs.FFXIV.Client.Game;

namespace RetainerRecall;

internal static class InventoryScopes
{
    public static readonly InventoryType[] PlayerBags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    // Actual equipment compartments only: no removed belt slots, soul crystals or equipped items.
    public static readonly InventoryType[] Armoury = [InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody, InventoryType.ArmoryHands, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar, InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings];
    public static readonly InventoryType[] PlayerItems = [.. PlayerBags, .. Armoury];
    public static readonly InventoryType[] RetainerBags = [InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4, InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7];
}

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace RetainerRecall;

// Read-only use of fields absent from some API 15 host assemblies.
// Adapted from FFXIVClientStructs (MIT), AgentRetainer.cs at
// b1cae2ffcb795c6037f119f49efaeaa0e691175e; see THIRD-PARTY-NOTICES.md.
// Keep the host's Instance resolution and all UI/inventory cross-checks.
[StructLayout(LayoutKind.Explicit, Size = 0x68D0)]
internal unsafe struct RetainerAgentState
{
    [FieldOffset(0x58)] public InventoryType SellItemInventoryType;
    [FieldOffset(0x5C)] public int SellItemInventorySlot;
    [FieldOffset(0x4B7C)] public int SellItemUnitPrice;
    [FieldOffset(0x4B80)] public int SellItemQuantity;
    [FieldOffset(0x4B84)] public int ContextMenuIndex;
    [FieldOffset(0x4B88)] public int SellListEntryCount;
    [FieldOffset(0x4B90)] public EntryArray SellListEntries;
    [FieldOffset(0x688C)] public uint RetainerSellListAddonId;
    [FieldOffset(0x6890)] public uint RetainerSellAddonId;

    public static RetainerAgentState* Instance() => sizeof(AgentRetainer) == sizeof(RetainerAgentState)
        ? (RetainerAgentState*)AgentRetainer.Instance() : null;

    [InlineArray(20)]
    internal struct EntryArray { private SellListEntry first; }

    [StructLayout(LayoutKind.Explicit, Size = 0x168)]
    internal struct SellListEntry
    {
        [FieldOffset(0x00)] public uint ItemId;
        [FieldOffset(0x70)] public int Quantity;
        [FieldOffset(0x148)] public ushort InventorySlot;
    }
}

using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using RetainerRecall;

unsafe class Program
{
    private static int calls, passed;
    private static bool inputPresent, inputZeroed;
    private static nint receiver, receivedEvent;
    private static AtkEventType receivedType;
    private static int receivedParam;

    // A native ABI boundary with a synthetic vtable: no game process or network is used.
    [UnmanagedCallersOnly]
    private static void Receive(AtkEventListener* self, AtkEventType type, int param, AtkEvent* evt, AtkEventData* input)
    {
        calls++; receiver = (nint)self; receivedEvent = (nint)evt; receivedType = type; receivedParam = param;
        inputPresent = input != null;
        inputZeroed = input != null;
        if (input != null)
            for (var i = 0; i < sizeof(AtkEventData); i++) inputZeroed &= ((byte*)input)[i] == 0;
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
        passed++; Console.WriteLine("PASS " + label);
    }
    private static bool Rejects(AtkUnitBase* addon, AtkComponentButton* button)
    {
        try { NativeButton.Click(addon, button); return false; }
        catch (InvalidOperationException) { return true; }
    }

    static void Main()
    {
        RetainerAgentState state = default;
        var bytes = (byte*)&state;
        *(int*)(bytes + 0x58) = 10000; *(int*)(bytes + 0x5C) = 17;
        *(int*)(bytes + 0x4B7C) = 321; *(int*)(bytes + 0x4B80) = 99;
        *(int*)(bytes + 0x4B84) = 19; *(int*)(bytes + 0x4B88) = 20;
        *(uint*)(bytes + 0x688C) = 41; *(uint*)(bytes + 0x6890) = 42;
        Check(sizeof(RetainerAgentState) == 0x68D0 && sizeof(FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentRetainer) == sizeof(RetainerAgentState), "retainer view matches host size");
        Check((int)state.SellItemInventoryType == 10000 && state.SellItemInventorySlot == 17 && state.SellItemUnitPrice == 321 && state.SellItemQuantity == 99, "retainer source and final values read official offsets");
        Check(state.ContextMenuIndex == 19 && state.SellListEntryCount == 20 && state.RetainerSellListAddonId == 41 && state.RetainerSellAddonId == 42, "retainer menu and addon validation offsets");
        for (int row = 0; row < 20; row++)
        {
            var entry = bytes + 0x4B90 + row * 0x168;
            *(uint*)entry = (uint)(1000 + row); *(int*)(entry + 0x70) = row + 1; *(ushort*)(entry + 0x148) = (ushort)(19 - row);
        }
        var rowsValid = true;
        for (int row = 0; row < 20; row++) rowsValid &= state.SellListEntries[row].ItemId == 1000 + row && state.SellListEntries[row].Quantity == row + 1 && state.SellListEntries[row].InventorySlot == 19 - row;
        Check(rowsValid, "all twenty sale rows preserve official entry stride");
        var table = stackalloc nint[3];
        table[0] = 0; table[1] = 0;
        table[2] = (nint)(delegate* unmanaged<AtkEventListener*, AtkEventType, int, AtkEvent*, AtkEventData*, void>)&Receive;
        AtkUnitBase addon = default;
        *(nint*)&addon = (nint)table;
        AtkComponentNode node = default;
        node.NodeFlags = NodeFlags.Enabled | NodeFlags.Visible;
        AtkComponentButton button = default;
        button.OwnerNode = &node;
        AtkEvent click = default;
        click.State.EventType = AtkEventType.ButtonClick;
        click.Listener = (AtkEventListener*)&addon;
        click.Param = 17;
        node.AtkEventManager.Event = &click;

        NativeButton.Click(&addon, &button);
        Check(calls == 1 && receiver == (nint)(&addon), "one native dispatch to registered addon");
        Check(inputPresent, "fifth native argument is never null (crash regression)");
        Check(inputZeroed, "input data is fully initialized without modifier keys");
        Check(receivedType == AtkEventType.ButtonClick && receivedParam == 17 && receivedEvent == (nint)(&click), "registered event type, parameter and pointer preserved");

        var before = calls;
        Check(Rejects(null, &button) && Rejects(&addon, null), "missing addon or button rejected");
        button.OwnerNode = null;
        Check(Rejects(&addon, &button), "missing owner checked before IsEnabled dereference");
        button.OwnerNode = &node; node.NodeFlags = NodeFlags.Visible;
        Check(Rejects(&addon, &button), "disabled button rejected");
        node.NodeFlags |= NodeFlags.Enabled; click.Listener = null;
        Check(Rejects(&addon, &button), "foreign listener rejected");
        click.Listener = (AtkEventListener*)&addon; click.State.EventType = AtkEventType.MouseClick;
        Check(Rejects(&addon, &button), "unrelated event rejected");
        click.NextEvent = &click;
        Check(Rejects(&addon, &button), "cyclic unrelated event list terminates");
        Check(calls == before, "invalid buttons and events never dispatch");

        AtkEvent prefix = default;
        prefix.NextEvent = &click;
        node.AtkEventManager.Event = &prefix;
        click.NextEvent = null; click.State.EventType = AtkEventType.ButtonClick;
        NativeButton.Click(&addon, &button);
        Check(calls == before + 1 && receivedEvent == (nint)(&click) && inputPresent && inputZeroed, "find matching event after unrelated entry");
        Console.WriteLine($"{passed} native boundary checks passed");
    }
}

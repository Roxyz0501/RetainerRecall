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

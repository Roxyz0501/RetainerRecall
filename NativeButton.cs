using FFXIVClientStructs.FFXIV.Component.GUI;

namespace RetainerRecall;

internal static unsafe class NativeButton
{
    public static void Click(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (addon == null || button == null || button->OwnerNode == null || !button->IsEnabled)
            throw new InvalidOperationException("出品ボタンが無効です");

        var evt = button->OwnerNode->AtkEventManager.Event;
        for (var i = 0; evt != null && i < 32; i++, evt = evt->NextEvent)
        {
            if (evt->State.EventType != AtkEventType.ButtonClick || evt->Listener != (AtkEventListener*)addon) continue;
            // RetainerSell reads MouseData.Modifier (offset 7) even for ButtonClick.
            // Omitting this fifth argument passes null and crashes the native handler.
            // Use initialized, unmodified left-button input for this synchronous dispatch.
            AtkEventData input = default;
            addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt, &input);
            return;
        }
        throw new InvalidOperationException("出品ボタンの通常クリックイベントが見つかりません");
    }
}

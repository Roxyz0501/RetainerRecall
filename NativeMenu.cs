using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;

namespace RetainerRecall;

internal static unsafe class NativeMenu
{
    // The context-menu callback contract is shared with ECommons' ContextMenu.Entry.Select.
    public static void Select(AtkUnitBase* menu, string label)
    {
        if (menu == null || !menu->IsReady || !menu->IsVisible || menu->AtkValuesCount < 9) throw new InvalidOperationException("メニュー構造を確認できません");
        var count = (int)menu->AtkValues[0].UInt;
        var list = menu->GetComponentListById(2);
        if (count is < 1 or > 32 || menu->AtkValuesCount < 8 + count || list == null || list->ListLength < count) throw new InvalidOperationException("メニュー構造を確認できません");
        for (var i = 0; i < count; i++)
        {
            var value = &menu->AtkValues[8 + i];
            if (value->Type != AtkValueType.ManagedString || value->String.Value == null) continue;
            var text = new ReadOnlySeStringSpan(value->String.Value);
            if (text.PayloadCount != 1 || text.ExtractText() != label) continue;
            var renderer = list->GetItemRenderer(i);
            if (renderer == null || !renderer->IsEnabled) throw new InvalidOperationException($"「{label}」が無効です");
            Callback(menu, 0, i, 0);
            return;
        }
        throw new InvalidOperationException($"通常メニューに「{label}」がありません");
    }
    public static void Callback(AtkUnitBase* addon, params int[] values)
    {
        var args = stackalloc AtkValue[values.Length];
        for (var i = 0; i < values.Length; i++) { args[i] = default; args[i].Type = AtkValueType.Int; args[i].Int = values[i]; }
        addon->FireCallback((uint)values.Length, args, true);
    }
}

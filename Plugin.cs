using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace RetainerRecall;

public sealed unsafe class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IAddonLifecycle lifecycle;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly GamePort port;
    private readonly RecallRun run;
    private bool settings;
    private Destination? requested;
    private bool stopRequested;
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IFramework framework, IAddonLifecycle lifecycle, IGameGui gui, IPlayerState player, IPluginLog log)
    {
        this.pi = pi; this.commands = commands; this.framework = framework; this.lifecycle = lifecycle; this.log = log;
        config = pi.GetPluginConfig() as Configuration ?? new();
        config.Player ??= new(); config.Retainer ??= new() { Offset = new(8, 42) };
        Normalize(config.Player); Normalize(config.Retainer);
        port = new(gui, player);
        run = new(port);
        commands.AddHandler("/retainerrecall", new CommandInfo((_, args) => { if (args.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase)) stopRequested = true; else settings = true; }) { HelpMessage = "出品一括回収の設定。/retainerrecall stop で停止" });
        framework.Update += Update;
        pi.UiBuilder.Draw += Draw;
        pi.UiBuilder.OpenConfigUi += Open;
        pi.UiBuilder.OpenMainUi += Open;
        lifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerSellList", Closed);
    }

    private static void Normalize(ButtonSettings button)
    {
        button.DelaySeconds = float.IsFinite(button.DelaySeconds) ? Math.Clamp(button.DelaySeconds, 0.5f, 30) : 1.5f;
        if (!float.IsFinite(button.Offset.X) || !float.IsFinite(button.Offset.Y)) button.Offset = new(8, 8);
    }
    private void Open() => settings = true;
    private void Closed(AddonEvent type, AddonArgs args) { requested = null; run.Stop("販売リストが閉じたため停止しました"); }
    private void Update(IFramework _)
    {
        try
        {
            if (stopRequested) { run.Stop(); requested = null; stopRequested = false; }
            if (requested is { } target)
            {
                requested = null;
                run.Start(target, (target == Destination.Player ? config.Player : config.Retainer).DelaySeconds, Now);
            }
            run.Tick(Now);
        }
        catch (Exception e) { run.Stop("エラーにより停止しました。ログを確認してください"); log.Error(e, "Retainer Recall stopped"); }
    }

    private void DrawButton(Destination target, ButtonSettings button, Vector2 anchor)
    {
        if (!button.Visible) return;
        ImGui.SetNextWindowPos(anchor + button.Offset, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.94f);
        if (ImGui.Begin($"##RetainerRecall{target}", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.BeginDisabled(run.Running || requested != null);
            if (ImGui.Button(target == Destination.Player ? "すべて所持品に戻す" : "すべてリテイナーに戻す")) requested = target;
            ImGui.EndDisabled();
        }
        ImGui.End();
    }

    private void Draw()
    {
        var window = port.Window;
        if (window != null && window->IsVisible)
        {
            var anchor = ImGui.GetMainViewport().Pos + new Vector2(window->X, window->Y + window->GetScaledHeight(true));
            DrawButton(Destination.Player, config.Player, anchor);
            DrawButton(Destination.Retainer, config.Retainer, anchor);
            if (run.Running || run.Status != "待機中")
            {
                ImGui.SetNextWindowPos(anchor + new Vector2(8, 82), ImGuiCond.Always);
                if (ImGui.Begin("##RetainerRecallProgress", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
                {
                    ImGui.TextUnformatted(run.Status);
                    if (run.Running && ImGui.Button("停止")) stopRequested = true;
                    if (ImGui.Button("設定")) settings = true;
                }
                ImGui.End();
            }
        }
        if (!settings) return;
        ImGui.SetNextWindowSize(new(540, 430), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Retainer Recall", ref settings))
        {
            if (ImGui.BeginTabBar("Tabs"))
            {
                if (ImGui.BeginTabItem("設定"))
                {
                    ImGui.TextWrapped("販売リストの左下を基準に表示します。位置はXが右方向、Yが下方向です。待機秒数は各アイテムの回収完了後に適用します。");
                    EditButton("プレイヤーの所持品", config.Player);
                    EditButton("リテイナーの所持品", config.Retainer);
                    ImGui.TextWrapped("空き枠が1つ以上必要です。実行中は手動で所持品・出品を操作しないでください。読み込めない場合はリテイナーの所持品を一度開いてください。");
                    ImGui.TextWrapped(run.Status);
                    if (run.Running && ImGui.Button("回収を停止")) stopRequested = true;
                    ImGui.EndTabItem();
                }
                ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.38f, 0.21f, 0.035f, 1));
                ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.55f, 0.32f, 0.06f, 1));
                ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.46f, 0.26f, 0.04f, 1));
                ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);
                var support = ImGui.BeginTabItem("支援");
                ImGui.PopStyleColor(4);
                if (support)
                {
                    ImGui.TextUnformatted("開発者: Roxyz0501");
                    ImGui.TextWrapped("支援は任意です。すべての機能を支援なしで利用できます。");
                    if (ImGui.Button("Ko-fiでRoxyz0501を支援")) Dalamud.Utility.Util.OpenLink("https://ko-fi.com/roxyz0501");
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }

    private void EditButton(string title, ButtonSettings button)
    {
        ImGui.PushID(title);
        ImGui.Separator(); ImGui.TextUnformatted(title);
        ImGui.BeginDisabled(run.Running);
        var changed = ImGui.Checkbox("ボタンを表示", ref button.Visible);
        changed |= ImGui.SliderFloat("待機秒数", ref button.DelaySeconds, 0.5f, 30, "%.1f秒");
        changed |= ImGui.DragFloat2("表示位置（X, Y）", ref button.Offset, 1, -3000, 3000, "%.0f");
        if (ImGui.Button("初期値に戻す")) { button.Visible = true; button.DelaySeconds = 1.5f; button.Offset = new(8, button == config.Player ? 8 : 42); changed = true; }
        if (changed) { Normalize(button); pi.SavePluginConfig(config); }
        ImGui.EndDisabled(); ImGui.PopID();
    }

    public void Dispose()
    {
        run.Stop();
        lifecycle.UnregisterListener(AddonEvent.PreFinalize, "RetainerSellList", Closed);
        framework.Update -= Update;
        pi.UiBuilder.Draw -= Draw;
        pi.UiBuilder.OpenConfigUi -= Open;
        pi.UiBuilder.OpenMainUi -= Open;
        commands.RemoveHandler("/retainerrecall");
    }
}

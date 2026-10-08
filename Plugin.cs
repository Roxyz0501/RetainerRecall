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
    private readonly IChatGui chat;
    private readonly Configuration config;
    private readonly GamePort port;
    private readonly RecallRun run;
    private readonly ListingService listing;
    private bool settings;
    private Destination? requested;
    private bool stopRequested;
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IFramework framework, IAddonLifecycle lifecycle, IGameGui gui, IPlayerState player, IPluginLog log, IKeyState keys, IClientState client, IChatGui chat, ICondition condition, IDataManager data, IGameInteropProvider interop)
    {
        this.pi = pi; this.commands = commands; this.framework = framework; this.lifecycle = lifecycle; this.log = log; this.chat = chat;
        config = pi.GetPluginConfig() as Configuration ?? new();
        config.Player ??= new(); config.Retainer ??= new() { Offset = new(8, 42) };
        Normalize(config.Player); Normalize(config.Retainer);
        config.ListingDelaySeconds = float.IsFinite(config.ListingDelaySeconds) ? Math.Clamp(config.ListingDelaySeconds, 0.1f, 30) : 1.5f;
        config.ListingStackLimit = Math.Clamp(config.ListingStackLimit, 1, 9999);
        if (!Enum.IsDefined(config.ListingKey)) config.ListingKey = ListingKey.RightAlt;
        port = new(gui, player, data);
        run = new(port);
        listing = new(pi, lifecycle, keys, player, client, gui, chat, log, condition, data, interop, config, () => run.Running || requested != null);
        foreach (var command in new[] { "/retainerlisting", "/retainerrecall" })
            commands.AddHandler(command, new CommandInfo((_, args) => { if (args.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase)) stopRequested = true; else settings = true; }) { HelpMessage = "リテイナー出品補助の設定。/retainerlisting stop で全操作を停止" });
        framework.Update += Update;
        pi.UiBuilder.Draw += Draw;
        pi.UiBuilder.OpenConfigUi += Open;
        pi.UiBuilder.OpenMainUi += Open;
        lifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerSellList", Closed);
    }

    private static void Normalize(ButtonSettings button)
    {
        button.DelaySeconds = float.IsFinite(button.DelaySeconds) ? Math.Clamp(button.DelaySeconds, 0.1f, 30) : 1.5f;
        if (!float.IsFinite(button.Offset.X) || !float.IsFinite(button.Offset.Y)) button.Offset = new(8, 8);
    }
    private void Open() => settings = true;
    private void Closed(AddonEvent type, AddonArgs args) { requested = null; run.Stop("販売リストが閉じたため停止しました"); }
    private void Update(IFramework _)
    {
        try
        {
            if (stopRequested) { run.Stop(); listing.Stop(); requested = null; stopRequested = false; }
            if (requested is { } target)
            {
                requested = null;
                if (!listing.Busy)
                {
                    run.Start(target, (target == Destination.Player ? config.Player : config.Retainer).DelaySeconds, Now);
                    if (!run.Running) ReportRecall();
                }
            }
            var wasRunning = run.Running;
            run.Tick(Now);
            if (wasRunning && !run.Running) ReportRecall();
            listing.Tick();
        }
        catch (Exception e) { run.Stop($"エラーにより停止: {e.Message}"); listing.Stop(); chat.PrintError($"[Retainer Listing Helper] {run.Status}"); log.Error(e, "Retainer Listing Helper stopped"); }
    }

    private void ReportRecall()
    {
        chat.Print($"[Retainer Listing Helper] {run.Status}");
        log.Information("Recall ended: {Status}", run.Status);
    }

    private void DrawButton(Destination target, ButtonSettings button, Vector2 anchor)
    {
        if (!button.Visible) return;
        ImGui.SetNextWindowPos(anchor + button.Offset, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.94f);
        if (ImGui.Begin($"##RetainerRecall{target}", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.BeginDisabled(run.Running || requested != null || listing.Busy);
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
        }
        if (!settings) return;
        ImGui.SetNextWindowSize(new(540, 430), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Retainer Listing Helper", ref settings))
        {
            if (ImGui.BeginTabBar("Tabs"))
            {
                if (ImGui.BeginTabItem("連続出品"))
                {
                    DrawListingSettings();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("出品回収"))
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
        ImGui.BeginDisabled(run.Running || listing.Busy);
        var changed = ImGui.Checkbox("ボタンを表示", ref button.Visible);
        changed |= ImGui.SliderFloat("待機秒数", ref button.DelaySeconds, 0.1f, 30, "%.1f秒");
        changed |= ImGui.DragFloat2("表示位置（X, Y）", ref button.Offset, 1, -3000, 3000, "%.0f");
        if (ImGui.Button("初期値に戻す")) { button.Visible = true; button.DelaySeconds = 1.5f; button.Offset = new(8, button == config.Player ? 8 : 42); changed = true; }
        if (changed) { Normalize(button); pi.SavePluginConfig(config); }
        ImGui.EndDisabled(); ImGui.PopID();
    }

    public void Dispose()
    {
        run.Stop();
        listing.Dispose();
        lifecycle.UnregisterListener(AddonEvent.PreFinalize, "RetainerSellList", Closed);
        framework.Update -= Update;
        pi.UiBuilder.Draw -= Draw;
        pi.UiBuilder.OpenConfigUi -= Open;
        pi.UiBuilder.OpenMainUi -= Open;
        commands.RemoveHandler("/retainerrecall");
        commands.RemoveHandler("/retainerlisting");
    }

    private void DrawListingSettings()
    {
        ImGui.TextWrapped("ログイン中に最後に出品を確定した単価で、同じ種類のアイテムを出品枠が埋まるまで出品します。価格はNQ/HQ共通です。");
        ImGui.BeginDisabled(run.Running || listing.Busy);
        var changed = ImGui.Checkbox("連続出品ショートカットを有効にする", ref config.EnableListing);
        var key = (int)config.ListingKey;
        if (ImGui.Combo("操作キー ＋ 右クリック", ref key, "右Alt\0左Alt\0右Ctrl\0左Ctrl\0右Shift\0左Shift\0")) { config.ListingKey = (ListingKey)key; changed = true; }
        changed |= ImGui.SliderFloat("出品ごとのディレイ", ref config.ListingDelaySeconds, 0.1f, 30, "%.1f秒");
        changed |= ImGui.Checkbox("Marketbuddyの出品数量設定を参照する", ref config.UseMarketbuddyLimit);
        if (ImGui.InputInt("標準の出品数量上限", ref config.ListingStackLimit)) { config.ListingStackLimit = Math.Clamp(config.ListingStackLimit, 1, 9999); changed = true; }
        ImGui.TextWrapped("Marketbuddyが有効で数量制限オンならその値、制限オフなら99個を使います。未導入・無効、または参照オフなら上の標準値を使います。数量は実行開始時に読み取ります。");
        if (changed) pi.SavePluginConfig(config);
        ImGui.EndDisabled();
        ImGui.Separator();
        ImGui.TextUnformatted($"記憶済みアイテム: {listing.Prices.Count}種類（ログアウトで消去）");
        ImGui.TextUnformatted($"数量: {listing.QuantitySource}");
        ImGui.TextWrapped(listing.Run.Status);
        if (listing.Busy && ImGui.Button("連続出品を停止")) stopRequested = true;
        ImGui.TextWrapped("価格履歴がなければ通常の操作で1回出品してください。プレイヤー側は所持品とアーマリーチェストを検索します。リテイナー側はその所持品を検索します。NQ/HQ両方が対象で、99個未満は端数、スタック不可は1個ずつ。他プラグインと重ならないキーを選んでください。");
    }
}

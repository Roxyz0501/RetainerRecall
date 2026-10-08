using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Game;
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
    private readonly LocalizedFonts fonts;
    private readonly List<CommandInfo> commandInfos = [];
    private bool settings;
    private Destination? requested;
    private bool stopRequested;
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IFramework framework, IAddonLifecycle lifecycle, IGameGui gui, IPlayerState player, IPluginLog log, IKeyState keys, IClientState client, IChatGui chat, ICondition condition, IDataManager data, IGameInteropProvider interop)
    {
        this.pi = pi; this.commands = commands; this.framework = framework; this.lifecycle = lifecycle; this.log = log; this.chat = chat;
        config = pi.GetPluginConfig() as Configuration ?? new();
        if (config.InitializeLanguage(() => client.ClientLanguage switch
            { ClientLanguage.Japanese => "ja", ClientLanguage.English => "en", ClientLanguage.German => "de", ClientLanguage.French => "fr", _ => null }, () => pi.UiLanguage))
            pi.SavePluginConfig(config);
        fonts = new(pi.UiBuilder.FontAtlas);
        config.Player ??= new(); config.Retainer ??= new() { Offset = new(8, 42) };
        Normalize(config.Player); Normalize(config.Retainer);
        config.ListingDelaySeconds = float.IsFinite(config.ListingDelaySeconds) ? Math.Clamp(config.ListingDelaySeconds, 0.1f, 30) : 1.5f;
        config.ListingStackLimit = Math.Clamp(config.ListingStackLimit, 1, 9999);
        if (!Enum.IsDefined(config.ListingKey)) config.ListingKey = ListingKey.RightAlt;
        port = new(gui, player, data);
        run = new(port);
        listing = new(pi, lifecycle, keys, player, client, gui, chat, log, condition, data, interop, config, () => run.Running || requested != null);
        foreach (var command in new[] { "/retainerlisting", "/retainerrecall" })
        {
            var info = new CommandInfo((_, args) => { if (args.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase)) stopRequested = true; else settings = true; }) { HelpMessage = L.T("CommandHelp") };
            commandInfos.Add(info); commands.AddHandler(command, info);
        }
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
    private void Closed(AddonEvent type, AddonArgs args) { requested = null; run.Stop(L.M("RecallClosed")); }
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
        catch (Exception e) { run.Stop(L.M("ErrorStopped", L.From(e))); listing.Stop(); chat.PrintError($"[Retainer Listing Helper] {run.Status}"); log.Error(e, "Retainer Listing Helper stopped"); }
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
            if (ImGui.Button(target == Destination.Player ? L.Label("ToPlayer") : L.Label("ToRetainer"))) requested = target;
            ImGui.EndDisabled();
        }
        ImGui.End();
    }

    private void Draw()
    {
        using var font = fonts.Push();
        var window = port.Window;
        if (window != null && window->IsVisible)
        {
            var anchor = ImGui.GetMainViewport().Pos + new Vector2(window->X, window->Y + window->GetScaledHeight(true));
            DrawButton(Destination.Player, config.Player, anchor);
            DrawButton(Destination.Retainer, config.Retainer, anchor);
        }
        if (!settings) return;
        ImGui.SetNextWindowSize(new(650, 640), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new(420, 300), new(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("Retainer Listing Helper", ref settings))
        {
            var language = Array.IndexOf(L.Codes, L.Code);
            ImGui.TextUnformatted("言語 / Language");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("###Language", ref language, string.Join("\0", L.Names) + "\0")) SetLanguage(L.Codes[language]);
            ImGui.Separator();
            if (ImGui.BeginTabBar("Tabs"))
            {
                if (ImGui.BeginTabItem(L.Label("ListingTab")))
                {
                    DrawListingSettings();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(L.Label("RecallTab")))
                {
                    ImGui.TextWrapped(L.T("PositionHelp"));
                    EditButton(L.T("PlayerInventory"), config.Player);
                    EditButton(L.T("RetainerInventory"), config.Retainer);
                    ImGui.TextWrapped(L.T("SpaceHelp"));
                    ImGui.TextWrapped(run.Status.ToString());
                    if (run.Running && ImGui.Button(L.Label("StopRecall"))) stopRequested = true;
                    ImGui.EndTabItem();
                }
                ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.38f, 0.21f, 0.035f, 1));
                ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.55f, 0.32f, 0.06f, 1));
                ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.46f, 0.26f, 0.04f, 1));
                ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);
                var support = ImGui.BeginTabItem(L.Label("SupportTab"));
                ImGui.PopStyleColor(4);
                if (support)
                {
                    ImGui.TextWrapped(L.T("Author"));
                    ImGui.TextWrapped(L.T("SupportHelp"));
                    if (ImGui.Button(L.Label("SupportLink"))) Dalamud.Utility.Util.OpenLink("https://ko-fi.com/roxyz0501");
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }

    private void SetLanguage(string code)
    {
        L.Set(code); config.Language = L.Code;
        foreach (var info in commandInfos) info.HelpMessage = L.T("CommandHelp");
        pi.SavePluginConfig(config);
    }

    private static string Field(string key)
    {
        ImGui.TextWrapped(L.T(key));
        ImGui.SetNextItemWidth(-1);
        return "###" + key;
    }

    private static bool Check(string key, ref bool value)
    {
        var changed = ImGui.Checkbox("###" + key, ref value);
        ImGui.SameLine(); ImGui.TextWrapped(L.T(key));
        return changed;
    }

    private void EditButton(string title, ButtonSettings button)
    {
        ImGui.PushID(button == config.Player ? "player" : "retainer");
        ImGui.Separator(); ImGui.TextUnformatted(title);
        ImGui.BeginDisabled(run.Running || listing.Busy);
        var changed = Check("ShowButton", ref button.Visible);
        changed |= ImGui.SliderFloat(Field("RecallDelay"), ref button.DelaySeconds, 0.1f, 30, L.T("SecondsFormat"));
        changed |= ImGui.DragFloat2(Field("Position"), ref button.Offset, 1, -3000, 3000, "%.0f");
        if (ImGui.Button(L.Label("Reset"))) { button.Visible = true; button.DelaySeconds = 1.5f; button.Offset = new(8, button == config.Player ? 8 : 42); changed = true; }
        if (changed) { Normalize(button); pi.SavePluginConfig(config); }
        ImGui.EndDisabled(); ImGui.PopID();
    }

    public void Dispose()
    {
        run.Stop();
        listing.Dispose();
        fonts.Dispose();
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
        ImGui.TextWrapped(L.T("ListingHelp"));
        ImGui.BeginDisabled(run.Running || listing.Busy);
        var changed = Check("EnableShortcut", ref config.EnableListing);
        var key = (int)config.ListingKey;
        if (ImGui.Combo(Field("ShortcutKey"), ref key, string.Join("\0", new[] { "RightAlt", "LeftAlt", "RightCtrl", "LeftCtrl", "RightShift", "LeftShift" }.Select(x => L.T(x))) + "\0")) { config.ListingKey = (ListingKey)key; changed = true; }
        changed |= ImGui.SliderFloat(Field("ListingDelay"), ref config.ListingDelaySeconds, 0.1f, 30, L.T("SecondsFormat"));
        changed |= Check("UseMarketbuddy", ref config.UseMarketbuddyLimit);
        if (ImGui.InputInt(Field("QuantityLimit"), ref config.ListingStackLimit)) { config.ListingStackLimit = Math.Clamp(config.ListingStackLimit, 1, 9999); changed = true; }
        ImGui.TextWrapped(L.T("QuantityHelp"));
        if (changed) pi.SavePluginConfig(config);
        ImGui.EndDisabled();
        ImGui.Separator();
        ImGui.TextWrapped(L.T("PriceCount", listing.Prices.Count));
        ImGui.TextWrapped(L.T("QuantitySource", listing.QuantitySource));
        ImGui.TextWrapped(listing.Run.Status.ToString());
        if (listing.Busy && ImGui.Button(L.Label("StopListing"))) stopRequested = true;
        ImGui.TextWrapped(L.T("ListingHint"));
    }
}

using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using RetainerRecall;

unsafe class Program
{
    static T Stub<T>() where T : class => DispatchProxy.Create<T, Dummy>();
    static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
    static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts"); Directory.CreateDirectory(output);
        ImGui.CreateContext(); var io = ImGui.GetIO(); io.DisplaySize = new(1440, 900); io.DeltaTime = 1f / 60; io.ConfigInputTrickleEventQueue = false;
        using (var plugin = new Plugin(Stub<IDalamudPluginInterface>(), Stub<ICommandManager>(), Stub<IFramework>(), Stub<IAddonLifecycle>(), Stub<IGameGui>(), Stub<IPlayerState>(), Stub<IPluginLog>(), Stub<IKeyState>(), Stub<IClientState>(), Stub<IChatGui>(), Stub<ICondition>(), Stub<IDataManager>(), Stub<IGameInteropProvider>()))
        {
            io.Fonts.Build();
            Check(Dummy.Fonts.Count == 4, "production font callback loaded all four CJK faces");
            var required = string.Concat(L.AllStrings) + "言語 / Language";
            foreach (var font in Dummy.Fonts)
            {
                foreach (var ch in required.Distinct().Where(c => !char.IsControl(c) && c != '\u200B'))
                    if (font.FindGlyphNoFallback(ch) == null) throw new Exception($"Missing glyph U+{(int)ch:X4}");
            }
            Check(true, "every translated glyph and language name covered in all font faces");
            var textures = new Dictionary<ulong, (nint, int, int)>();
            for (var i = 0; i < io.Fonts.Textures.Size; i++)
            {
                byte* pixels; int w = 0, h = 0, bpp = 0; io.Fonts.GetTexDataAsRGBA32(i, &pixels, &w, &h, &bpp); io.Fonts.SetTexID(i, new ImTextureID((ulong)i + 1)); textures[(ulong)i + 1] = ((nint)pixels, w, h);
            }
            var draw = typeof(Plugin).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var setLanguage = typeof(Plugin).GetMethod("SetLanguage", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Frame() { ImGui.NewFrame(); draw.Invoke(plugin, null); ImGui.Render(); }
            void Click(float x, float y) { io.AddMousePosEvent(x, y); Frame(); io.AddMouseButtonEvent(0, true); Frame(); io.AddMouseButtonEvent(0, false); Frame(); Frame(); }
            Dummy.Commands["/retainerlisting"].Handler("/retainerlisting", "");
            foreach (var code in L.Codes)
            {
                setLanguage.Invoke(plugin, [code]);
                for (var i = 0; i < 3; i++) Frame();
                Check(Dummy.Config.Language == code && Dummy.Commands["/retainerlisting"].HelpMessage == L.T("CommandHelp"), code + " switches settings and command help immediately");
                var persisted = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(Dummy.Saved)!;
                Check(persisted.Language == code && !persisted.InitializeLanguage(() => "ja", () => "en"), code + " saved choice survives restart");
                Renderer.Render(ImGui.GetDrawData(), textures, Path.Combine(output, code + "-listing.bmp"));
            }
            setLanguage.Invoke(plugin, ["en"]); Frame(); Frame();
            // Coordinates are within the fixed default window at (60,60), after the language field.
            var enabled = Dummy.Config.EnableListing;
            Click(79, 220);
            Check(Dummy.Config.EnableListing != enabled, "checkbox remains usable after seven language switches");
            Click(79, 220);
            Click(400, 123);
            Renderer.Render(ImGui.GetDrawData(), textures, Path.Combine(output, "language-menu.bmp"));
            Click(120, 152);
            Check(Dummy.Config.Language == "ja", "language dropdown changes and saves selection through mouse input");
            Renderer.Render(ImGui.GetDrawData(), textures, Path.Combine(output, "en-interaction.bmp"));
            foreach (var code in new[] { "de", "fr", "ko", "zh-Hans", "zh-Hant", "ja", "en" })
            {
                setLanguage.Invoke(plugin, [code]); Frame(); Frame();
                // Select tabs using their measured labels in the default window.
                var firstWidth = ImGui.CalcTextSize(L.T("ListingTab")).X + ImGui.GetStyle().FramePadding.X * 2;
                Click(70 + firstWidth + 20, 162);
                Renderer.Render(ImGui.GetDrawData(), textures, Path.Combine(output, code + "-recall.bmp"));
                var secondWidth = ImGui.CalcTextSize(L.T("RecallTab")).X + ImGui.GetStyle().FramePadding.X * 2;
                Click(70 + firstWidth + secondWidth + 35, 162);
                Renderer.Render(ImGui.GetDrawData(), textures, Path.Combine(output, code + "-support.bmp"));
            }
            Dummy.Commands["/retainerrecall"].Handler("/retainerrecall", ""); Frame();
            Check(ImGui.GetDrawData().TotalVtxCount > 0, "legacy command opens settings");
        }
        Check(Dummy.Commands.Count == 0, "command handlers disposed");
        ImGui.DestroyContext();
        foreach (var pin in Dummy.Pins) pin.Free();
    }
}

public unsafe class Dummy : DispatchProxy
{
    public static Configuration Config = new() { Language = "en" };
    public static string Saved = "";
    public static Dictionary<string, CommandInfo> Commands = [];
    public static List<ImFontPtr> Fonts = [];
    public static List<GCHandle> Pins = [];
    public ImFontPtr Font;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "GetPluginConfig": return Config;
            case "SavePluginConfig": Saved = Newtonsoft.Json.JsonConvert.SerializeObject(args![0]); return null;
            case "AddHandler": return Commands.TryAdd((string)args![0]!, (CommandInfo)args[1]!);
            case "RemoveHandler": return Commands.Remove((string)args![0]!);
            case "get_UiBuilder": case "get_FontAtlas": return DispatchProxy.Create(method.ReturnType, typeof(Dummy));
            case "get_BuildStep": return FontAtlasBuildStep.PreBuild;
            case "NewDelegateFontHandle":
                var toolkit = DispatchProxy.Create<IFontAtlasBuildToolkitPreBuild, Dummy>();
                ((FontAtlasBuildStepDelegate)args![0]!)(toolkit);
                var handle = DispatchProxy.Create<IFontHandle, Dummy>(); ((Dummy)(object)handle).Font = Fonts.Last(); return handle;
            case "AddDalamudAssetFont":
                var safe = (SafeFontConfig)args![1]!;
                var pin = GCHandle.Alloc(safe.GlyphRanges, GCHandleType.Pinned); Pins.Add(pin);
                var native = ImGui.ImFontConfig();
                native.FontDataOwnedByAtlas = true; native.OversampleH = 2; native.OversampleV = 1; native.RasterizerMultiply = 1; native.GlyphMaxAdvanceX = float.MaxValue; native.FontNo = safe.FontNo;
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher/dalamudAssets/dev/UIRes/NotoSansCJK-Regular.ttc");
                var font = ImGui.GetIO().Fonts.AddFontFromFileTTF(path, safe.SizePx, native, (ushort*)pin.AddrOfPinnedObject());
                native.Destroy(); Fonts.Add(font); return font;
            case "Push": ImGui.PushFont(Font); return new PopFont();
        }
        return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }
    private sealed class PopFont : IDisposable { public void Dispose() => ImGui.PopFont(); }
}

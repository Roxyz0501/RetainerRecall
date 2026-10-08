using Dalamud;
using Dalamud.Interface.ManagedFontAtlas;

namespace RetainerRecall;

internal sealed class LocalizedFonts : IDisposable
{
    private readonly IFontHandle[] fonts;
    public LocalizedFonts(IFontAtlas atlas)
    {
        var glyphs = (string.Concat(L.AllStrings) + "言語 / Language" + new string(Enumerable.Range(32, 95).Select(x => (char)x).ToArray()) + "\u202F\u00A0…").ToGlyphRange();
        fonts = Enumerable.Range(0, 4).Select(face => atlas.NewDelegateFontHandle(step => step.OnPreBuild(tk =>
            tk.AddDalamudAssetFont(DalamudAsset.NotoSansCjkRegular, new SafeFontConfig { SizePx = 17, FontNo = face, GlyphRanges = glyphs })))).ToArray();
    }
    // Face ordering is documented by the installed SDK's SafeFontConfig.FontNo API.
    public static int Face(string code) => code switch { "zh-Hant" => 1, "zh-Hans" => 2, "ko" => 3, _ => 0 };
    public IDisposable Push() => fonts[Face(L.Code)].Push();
    public void Dispose() { foreach (var font in fonts) font.Dispose(); }
}

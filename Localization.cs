using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace RetainerRecall;

public sealed record Text(string Key, params object[] Args)
{
    public override string ToString() => L.T(Key, Args);
    public static implicit operator string(Text value) => value.ToString();
}

public sealed class LocalizedException(Text description) : InvalidOperationException
{
    public Text Description { get; } = description;
    public override string Message => Description.ToString();
}

public static class L
{
    public static readonly string[] Codes = ["ja", "en", "de", "fr", "ko", "zh-Hans", "zh-Hant"];
    public static readonly string[] Names = ["日本語", "English", "Deutsch", "Français", "한국어", "简体中文", "繁體中文"];
    private static readonly Dictionary<string, Dictionary<string, string>> Resources = Load();
    public static string Code { get; private set; } = "en";
    public static IEnumerable<string> AllStrings => Resources.Values.SelectMany(x => x.Values).Concat(Names);
    public static IReadOnlyDictionary<string, string> Table(string code) => Resources[code];
    public static void Set(string code) => Code = Normalize(code) ?? "en";
    public static Text M(string key, params object[] args) => new(key, args);
    public static Text From(Exception error) => error is LocalizedException local ? local.Description : M("UnexpectedError");
    public static string Label(string key) => T(key) + "###" + key;
    public static string T(string key, params object[] args)
    {
        var format = Resources[Code].GetValueOrDefault(key) ?? Resources["en"].GetValueOrDefault(key) ?? key;
        try { return string.Format(CultureInfo.GetCultureInfo(Code), format, args); }
        catch (FormatException) { return key; }
    }
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Trim().Replace('_', '-').ToLowerInvariant().Split('-');
        var code = parts[0];
        if (code == "zh")
        {
            if (parts.Contains("hans")) return "zh-Hans";
            if (parts.Contains("hant")) return "zh-Hant";
            if (parts.Any(x => x is "cn" or "sg")) return "zh-Hans";
            if (parts.Any(x => x is "tw" or "hk" or "mo")) return "zh-Hant";
            return null;
        }
        return code is "ja" or "en" or "de" or "fr" or "ko" ? code : null;
    }
    public static string Resolve(string? saved, Func<string?> game, Func<string?> dalamud, Func<string?>? launcher = null)
    {
        if (Normalize(saved) is { } valid) return valid;
        foreach (var source in new[] { game, dalamud, launcher })
        {
            try { if (source != null && Normalize(source()) is { } detected) return detected; }
            catch { /* An unavailable public source must not block the next source. */ }
        }
        return "en";
    }
    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var assembly = typeof(L).Assembly;
        return Codes.ToDictionary(code => code, code =>
        {
            using var stream = assembly.GetManifestResourceStream($"RetainerRecall.Localization.{code}.json")
                ?? throw new InvalidOperationException($"Missing localization resource: {code}");
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        });
    }
}

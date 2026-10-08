using System.Text;
using System.Text.RegularExpressions;
using RetainerRecall;

static class LocalizationTests
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var code in L.Codes)
        {
            L.Set(code);
            var table = L.Table(code);
            check(table.Keys.Order().SequenceEqual(L.Table("en").Keys.Order()), code + " complete key set");
            foreach (var (key, value) in table)
            {
                if (string.IsNullOrWhiteSpace(value)) throw new Exception(code + ": empty " + key);
                var format = CompositeFormat.Parse(value);
                if (format.MinimumArgumentCount != CompositeFormat.Parse(L.Table("en")[key]).MinimumArgumentCount) throw new Exception(code + ": argument count " + key);
                string Signature(string text) => string.Join("|", Regex.Matches(text, @"\{[^{}]+\}|%\.\df").Select(m => m.Value).Order());
                if (Signature(value) != Signature(L.Table("en")[key])) throw new Exception(code + ": argument format " + key);
                if (L.T(key, [99, 200]) != string.Format(System.Globalization.CultureInfo.GetCultureInfo(code), value, 99, 200)) throw new Exception(code + ": rendering " + key);
            }
            check(true, code + " nonempty text, placeholders and rendering");
            check(L.Resolve(code, () => throw new Exception(), () => throw new Exception()) == code, code + " saved selection skips detection");
            var c = new Configuration { Language = code, ListingKey = ListingKey.RightShift, ListingDelaySeconds = 0.1f, EnableListing = false };
            var saved = Newtonsoft.Json.JsonConvert.SerializeObject(c);
            var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(saved)!;
            check(!restored.InitializeLanguage(() => "ja", () => "en") && restored.Language == code && restored.ListingKey == ListingKey.RightShift && !restored.EnableListing && restored.ListingDelaySeconds == 0.1f, code + " restart persistence preserves language and settings");
        }
        foreach (var (input, result) in new (string, string?)[] { ("ja-JP","ja"), ("EN_us","en"), ("en-GB","en"), ("de-DE","de"), ("fr-FR","fr"), ("ko_KR","ko"), ("zh-CN","zh-Hans"), ("zh-SG","zh-Hans"), ("zh-TW","zh-Hant"), ("zh-HK","zh-Hant"), ("zh-MO","zh-Hant"), ("zh-Hant-CN","zh-Hant"), ("ZH_HANS_TW","zh-Hans"), ("zh",null), ("auto",null), ("unknown",null), ("",null) })
            check(L.Normalize(input) == result, "normalize " + input);
        check(L.Resolve(null, () => "ja", () => "en") == "ja", "game precedes Dalamud");
        check(L.Resolve("auto", () => "zh", () => "ko-KR") == "ko", "ambiguous game Chinese falls through");
        check(L.Resolve(null, () => throw new Exception(), () => "de") == "de", "unavailable game falls through");
        check(L.Resolve(null, () => "unsupported", () => null, () => "fr") == "fr", "optional public launcher source ordering");
        check(L.Resolve(null, () => throw new Exception(), () => throw new Exception()) == "en", "unavailable sources fall back to English");
        check(L.Resolve(null, () => "unknown", () => "zh") == "en", "unsupported sources fall back to English");
        var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>("{\"Version\":1,\"EnableListing\":false,\"ListingKey\":3,\"Player\":{\"DelaySeconds\":0.1,\"Visible\":false}}")!;
        check(legacy.Language == null && legacy.InitializeLanguage(() => "ja", () => "en") && legacy.Language == "ja" && !legacy.EnableListing && legacy.ListingKey == ListingKey.LeftControl && !legacy.Player.Visible && legacy.Player.DelaySeconds == 0.1f, "pre-language config migrates without changing settings or enum values");
        legacy.Language = "invalid";
        check(legacy.InitializeLanguage(() => "en", () => "ja") && legacy.Language == "en", "invalid config repaired once");
        check(!legacy.InitializeLanguage(() => "ja", () => "fr") && legacy.Language == "en", "subsequent character/client language changes do not override saved choice");
        var price = new SessionPrices(); price.SetCharacter(1); price.Remember(100, 250);
        var status = L.M("RecallProgress", 2, 10); L.Set("ja"); var japanese = status.ToString(); L.Set("de");
        check(status.ToString() != japanese && price.TryGet(100, out var priceValue) && priceValue == 250, "existing status switches language without losing prices");
        var error = new LocalizedException(L.M("MenuDisabled", "Game-provided item")); var old = error.Message; L.Set("ko");
        check(error.Message != old && error.Message.Contains("Game-provided item"), "errors translate but game text is preserved");
        check(L.T("missing-diagnostic-key") == "missing-diagnostic-key", "unknown key remains diagnosable");
        var korean = (Dictionary<string, string>)L.Table("ko");
        var translated = korean["CommandHelp"];
        korean.Remove("CommandHelp");
        try { check(L.T("CommandHelp") == L.Table("en")["CommandHelp"], "missing translation falls back to English"); }
        finally { korean["CommandHelp"] = translated; }
        check(L.Codes.Length == 7 && !L.Codes.Contains("auto"), "exactly seven concrete language options");
        L.Set("en");
    }
}

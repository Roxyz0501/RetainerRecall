using Roxyz0501.Dalamud.CommonUi;

namespace RetainerRecall;

internal static class PluginDependencies
{
    public static readonly PluginDependency[] Items =
    [
        new("Marketbuddy", "Marketbuddy", false,
            new("Read its stack limit and pause automatic pricing during batch listing.",
                "数量設定を参照し、連続出品中の自動価格操作を一時停止します。",
                "Stapelgrenze übernehmen und automatische Preisänderungen während der Serienangebote pausieren.",
                "Lire sa limite de quantité et suspendre les prix automatiques pendant les ventes en série.",
                "수량 제한을 참조하고 연속 출품 중 자동 가격 조작을 일시 중지합니다.",
                "读取数量限制，并在连续上架期间暂停自动定价。",
                "讀取數量限制，並在連續上架期間暫停自動定價。"),
            new("Optional. Listing and retrieval work without it; the local quantity limit is used.",
                "任意の連携です。未導入でも出品・回収でき、本プラグインの数量設定を使います。",
                "Optional. Ohne dieses Plugin funktionieren Angebote und Rücknahme mit der lokalen Mengenbegrenzung.",
                "Facultatif. Les ventes et retraits fonctionnent sans lui, avec la limite de quantité locale.",
                "선택 연동입니다. 없어도 출품과 회수가 가능하며 이 플러그인의 수량 설정을 사용합니다.",
                "可选联动。未安装时仍可上架和取回，并使用本插件的数量设置。",
                "選用整合。未安裝時仍可上架及取回，並使用本外掛程式的數量設定。"),
            "https://github.com/PunishXIV/Marketbuddy#installation")
    ];

    // Include the exact common source's UI text without modifying the shared implementation.
    public static string FontText
    {
        get
        {
            using var reader = new StreamReader(typeof(PluginDependencies).Assembly.GetManifestResourceStream("RetainerRecall.StandardPluginInfo.source")!);
            return reader.ReadToEnd() + string.Concat(Items.SelectMany(d => L.Codes.Select(c => d.Purpose.Get(c) + d.Note?.Get(c))));
        }
    }
}

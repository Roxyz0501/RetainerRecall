using System.Numerics;
using Dalamud.Configuration;

namespace RetainerRecall;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string? Language { get; set; }
    public bool InitializeLanguage(Func<string?> game, Func<string?> dalamud)
    {
        var resolved = L.Resolve(Language, game, dalamud);
        var changed = Language != resolved;
        Language = resolved;
        L.Set(resolved);
        return changed;
    }
    public ButtonSettings Player = new();
    public ButtonSettings Retainer = new() { Offset = new(8, 42) };
    public bool EnableListing = true;
    public ListingKey ListingKey = ListingKey.RightAlt;
    public float ListingDelaySeconds = 1.5f;
    public bool UseMarketbuddyLimit = true;
    public int ListingStackLimit = 99;
}

public enum ListingKey { RightAlt, LeftAlt, RightControl, LeftControl, RightShift, LeftShift }

[Serializable]
public sealed class ButtonSettings
{
    public bool Visible = true;
    public float DelaySeconds = 1.5f;
    public Vector2 Offset = new(8, 8);
}

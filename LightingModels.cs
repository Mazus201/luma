internal static class LightingModes
{
    public const string Static = "Static";
    public const string Effect = "Effect";
    public const string DirectLighting = "DirectLighting";
}

internal sealed record LedInfo(int AreaIndex, string Name);

internal sealed record EffectInfo(
    string Id,
    string Name,
    bool SupportsSpeed);

internal sealed record ZoneCapability(
    string DeviceType,
    int Index,
    string Name,
    IReadOnlyList<string> Styles,
    string? CurrentStyle,
    uint CurrentSpeed,
    uint MaximumSpeed,
    IReadOnlyList<LedInfo> Leds,
    IReadOnlyList<string> Warnings)
{
    public bool SupportsSpeed => MaximumSpeed > 0;
    public bool SupportsDirectLighting =>
        Styles.Any(style => style.Equals("Direct Lighting Control", StringComparison.OrdinalIgnoreCase)) && Leds.Count > 0;
}

internal sealed record DeviceCapabilities(
    IReadOnlyList<ZoneCapability> Zones,
    IReadOnlyList<EffectInfo> SharedEffects,
    int InitialSpeedPercent,
    bool SupportsDirectLighting,
    IReadOnlyList<string> Warnings)
{
    public static DeviceCapabilities Empty { get; } = new([], [], 50, false, []);

    public EffectInfo? FindEffect(string? id) =>
        SharedEffects.FirstOrDefault(effect => effect.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

internal sealed class LightingPreset
{
    public string Color { get; set; } = "#4F8CFF";
    public int Brightness { get; set; } = 80;
    public bool Enabled { get; set; } = true;
    public string Mode { get; set; } = LightingModes.Static;
    public string EffectId { get; set; } = string.Empty;
    public int SpeedPercent { get; set; } = 50;
    public string GradientStart { get; set; } = "#4F8CFF";
    public string GradientEnd { get; set; } = "#FF70A6";
    public Dictionary<string, string> LedColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public LightingPreset Copy() => new()
    {
        Color = Color,
        Brightness = Brightness,
        Enabled = Enabled,
        Mode = Mode,
        EffectId = EffectId,
        SpeedPercent = SpeedPercent,
        GradientStart = GradientStart,
        GradientEnd = GradientEnd,
        LedColors = new Dictionary<string, string>(LedColors, StringComparer.OrdinalIgnoreCase)
    };
}

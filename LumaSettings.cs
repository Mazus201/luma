using System.Drawing;

internal sealed class LumaSettings
{
    public const int CurrentSchemaVersion = 4;
    private static readonly string[] DefaultPalette = ["#4F8CFF", "#FFFFFF", "#F04452", "#F59E0B", "#31C48D", "#22B8CF", "#A78BFA", "#FF70A6"];

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Color { get; set; } = "#4F8CFF";
    public int Brightness { get; set; } = 80;
    public bool Enabled { get; set; } = true;
    public string[] Presets { get; set; } = ["#4F8CFF", "#FFFFFF", "#31C48D", "#F59E0B"];
    public LightingPreset[]? LightingPresets { get; set; }
    public int[] CustomColors { get; set; } = new int[16];
    public string[] RecentColors { get; set; } = DefaultPalette.ToArray();
    public string Mode { get; set; } = LightingModes.Static;
    public string EffectId { get; set; } = string.Empty;
    public bool EffectColorControlAvailable { get; set; }
    public int SpeedPercent { get; set; } = 50;
    public string GradientStart { get; set; } = "#4F8CFF";
    public string GradientEnd { get; set; } = "#FF70A6";
    public Dictionary<string, string> LedColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        Brightness = Math.Clamp(Brightness, 0, 100);
        Color = ToHex(SafeColor(Color));

        var defaultPresets = new[] { "#4F8CFF", "#FFFFFF", "#31C48D", "#F59E0B" };
        Presets = Enumerable.Range(0, 4)
            .Select(index => ToHex(SafeColor(Presets is not null && index < Presets.Length ? Presets[index] : defaultPresets[index])))
            .ToArray();

        if (LightingPresets is null || LightingPresets.Length != 4)
        {
            LightingPresets = Presets.Select(color => new LightingPreset
            {
                Color = color,
                Brightness = Brightness,
                Enabled = Enabled
            }).ToArray();
        }
        else
        {
            for (var i = 0; i < LightingPresets.Length; i++)
            {
                var preset = LightingPresets[i] ?? new LightingPreset { Color = Presets[i] };
                preset.Color = ToHex(SafeColor(preset.Color));
                preset.Brightness = Math.Clamp(preset.Brightness, 0, 100);
                preset.SpeedPercent = Math.Clamp(preset.SpeedPercent, 0, 100);
                preset.GradientStart = ToHex(SafeColor(preset.GradientStart));
                preset.GradientEnd = ToHex(SafeColor(preset.GradientEnd));
                preset.LedColors = NormalizeLedColors(preset.LedColors);
                preset.Mode = NormalizeMode(preset.Mode);
                preset.EffectId ??= string.Empty;
                LightingPresets[i] = preset;
            }
        }
        Presets = LightingPresets.Select(preset => preset.Color).ToArray();

        Mode = NormalizeMode(Mode);
        EffectId ??= string.Empty;
        SpeedPercent = Math.Clamp(SpeedPercent, 0, 100);
        GradientStart = ToHex(SafeColor(GradientStart));
        GradientEnd = ToHex(SafeColor(GradientEnd));
        LedColors = NormalizeLedColors(LedColors);

        if (CustomColors is null || CustomColors.Length != 16)
            CustomColors = new int[16];

        var recent = new List<string>();
        Add(Color);
        if (RecentColors is not null)
            foreach (var color in RecentColors) Add(color);
        foreach (var color in DefaultPalette) Add(color);
        RecentColors = recent.Take(8).ToArray();

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var normalized = ToHex(SafeColor(value));
            if (!recent.Contains(normalized, StringComparer.OrdinalIgnoreCase)) recent.Add(normalized);
        }
    }

    public void RememberColor(string value)
    {
        Color = ToHex(SafeColor(value));
        RecentColors = new[] { Color }
            .Concat((RecentColors ?? []).Where(item => !string.Equals(ToHex(SafeColor(item)), Color, StringComparison.OrdinalIgnoreCase)))
            .Take(8)
            .ToArray();
    }

    private static Color SafeColor(string? value)
    {
        try { return ColorTranslator.FromHtml(string.IsNullOrWhiteSpace(value) ? "#4F8CFF" : value); }
        catch (ArgumentException) { return ColorTranslator.FromHtml("#4F8CFF"); }
    }

    private static string NormalizeMode(string? mode) =>
        mode is LightingModes.Effect or LightingModes.DirectLighting ? mode : LightingModes.Static;

    private static Dictionary<string, string> NormalizeLedColors(Dictionary<string, string>? colors)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (colors is null) return result;
        foreach (var (key, value) in colors)
            if (!string.IsNullOrWhiteSpace(key)) result[key] = ToHex(SafeColor(value));
        return result;
    }

    private static string ToHex(Color value) => $"#{value.R:X2}{value.G:X2}{value.B:X2}";
}

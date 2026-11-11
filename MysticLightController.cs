using System.Drawing;
using System.Collections.Concurrent;

internal sealed record SdkApplyResult(
    int ZonesFound,
    int ColorChanged,
    int BrightnessChanged,
    int StyleChanged,
    IReadOnlyList<string> Errors,
    int SpeedChanged = 0,
    bool ModeApplied = false,
    bool ColorControlAvailable = false,
    bool SpeedControlRejected = false)
{
    public int Changes => ColorChanged + BrightnessChanged + StyleChanged + SpeedChanged;
    public bool HasErrors => Errors.Count != 0;
}

internal static class MysticLightController
{
    private static readonly ConcurrentDictionary<string, uint> minimumSpeedCache = new(StringComparer.OrdinalIgnoreCase);

    public static DeviceCapabilities LoadCapabilities() => SdkSession.Run(LoadCapabilitiesCore);

    public static int? ReadBrightnessPercent() => SdkSession.Run<int?>(() =>
    {
        var inventory = ReadInventory();
        var zones = GetMotherboardZones(inventory, out _);
        var values = new List<int>();
        foreach (var zone in zones)
        {
            var currentStatus = Api.GetLedBright(zone.Type, zone.Index, out var current);
            var maximumStatus = Api.GetLedMaxBright(zone.Type, zone.Index, out var maximum);
            if (currentStatus != Status.Ok || maximumStatus != Status.Ok || maximum == 0) continue;
            values.Add((int)Math.Clamp(((ulong)current * 100 + maximum / 2u) / maximum, 0UL, 100UL));
        }
        if (values.Count == 0) return null;
        var median = values.Order().ElementAt(values.Count / 2);
        return median == 0 ? null : median;
    });

    public static SdkApplyResult ApplySettings(LumaSettings settings, DeviceCapabilities? capabilities = null)
    {
        if (settings.Mode == LightingModes.Effect)
            return capabilities is null || capabilities.FindEffect(settings.EffectId) is null
                ? Failure(capabilities?.Zones.Count ?? 0, $"Эффект «{settings.EffectId}» не поддерживается подключёнными зонами.")
                : ApplyEffect(settings, capabilities);

        if (settings.Mode == LightingModes.DirectLighting)
            return capabilities is null || !capabilities.SupportsDirectLighting
                ? Failure(capabilities?.Zones.Count ?? 0, "Direct Lighting Control недоступен для всех подключённых зон.")
                : ApplyGradient(settings, capabilities);

        return ApplyStaticSettings(settings);
    }

    public static SdkApplyResult ApplyColor(byte r, byte g, byte b) => SdkSession.Run(() => ApplyStaticCore(
        Color.FromArgb(r, g, b), enabled: true, brightnessPercent: 100));

    public static SdkApplyResult TurnOff() => SdkSession.Run(() => ApplyStaticCore(Color.Black, enabled: false, brightnessPercent: 0));

    public static SdkApplyResult AdjustBrightness(int direction) => SdkSession.Run(() =>
    {
        var inventory = ReadInventory();
        var errors = new List<string>(inventory.Errors);
        var zones = GetMotherboardZones(inventory, out var zoneErrors);
        errors.AddRange(zoneErrors);
        var changed = 0;
        foreach (var zone in zones)
        {
            var currentStatus = Api.GetLedBright(zone.Type, zone.Index, out var current);
            var maximumStatus = Api.GetLedMaxBright(zone.Type, zone.Index, out var maximum);
            if (currentStatus != Status.Ok || maximumStatus != Status.Ok || maximum == 0)
            {
                errors.Add($"{zone.Label}: GetLedBright={currentStatus}; GetLedMaxBright={maximumStatus}");
                continue;
            }
            var step = Math.Max(1u, (maximum + 9u) / 10u);
            var target = direction < 0
                ? Math.Max(1u, current > step ? current - step : 1u)
                : Math.Min(maximum, current + step);
            if (target == current) { changed++; continue; }
            var status = Api.SetLedBright(zone.Type, zone.Index, target);
            if (status == Status.Ok) changed++;
            else errors.Add($"{zone.Label}: SetLedBright={status}");
        }
        return new SdkApplyResult(zones.Count, 0, changed, 0, errors, ModeApplied: changed == zones.Count && zones.Count > 0);
    });

    public static SdkApplyResult ApplyEffectSpeed(DeviceCapabilities capabilities, int speedPercent) => SdkSession.Run(() =>
    {
        var errors = new List<string>();
        var changed = 0;
        var speedRejected = false;
        var zones = capabilities.Zones.Where(zone => zone.SupportsSpeed).ToArray();
        foreach (var zone in zones)
        {
            var minimum = minimumSpeedCache.TryGetValue(SpeedCacheKey(zone), out var cachedMinimum) ? cachedMinimum : 0;
            var level = ToDeviceSpeed(zone.MaximumSpeed, speedPercent, minimum);
            var status = SetEffectSpeed(zone, level);
            if (status == Status.Ok) changed++;
            else
            {
                speedRejected |= status == Status.NotSupported;
                errors.Add($"{zone.Name}: SetLedSpeed={status}");
            }
        }
        return new SdkApplyResult(zones.Length, 0, 0, 0, errors,
            SpeedChanged: changed, ModeApplied: changed == zones.Length && zones.Length > 0,
            SpeedControlRejected: speedRejected);
    });

    internal static uint ToDeviceBrightness(uint maximum, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (maximum == 0) return 0;
        if (percent == 0) return 1;
        return Math.Clamp((uint)(((ulong)maximum * (uint)percent + 50UL) / 100UL), 1u, maximum);
    }

    internal static uint ToDeviceSpeed(uint maximum, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        return (uint)(((ulong)maximum * (uint)percent + 50UL) / 100UL);
    }

    internal static uint ToDeviceSpeed(uint maximum, int percent, uint minimum)
    {
        percent = Math.Clamp(percent, 0, 100);
        minimum = Math.Min(minimum, maximum);
        return minimum + (uint)(((ulong)(maximum - minimum) * (uint)percent + 50UL) / 100UL);
    }

    internal static Color[] BuildGradient(Color start, Color end, int count)
    {
        if (count <= 0) return [];
        if (count == 1) return [start];
        var colors = new Color[count];
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / (count - 1);
            colors[i] = Color.FromArgb(
                (int)Math.Round(start.R + (end.R - start.R) * t),
                (int)Math.Round(start.G + (end.G - start.G) * t),
                (int)Math.Round(start.B + (end.B - start.B) * t));
        }
        return colors;
    }

    private static SdkApplyResult ApplyStaticSettings(LumaSettings settings) => SdkSession.Run(() => ApplyStaticCore(
        ColorTranslator.FromHtml(settings.Color), settings.Enabled, settings.Brightness));

    private static SdkApplyResult ApplyStaticCore(Color color, bool enabled, int brightnessPercent)
    {
        var inventory = ReadInventory();
        var errors = new List<string>(inventory.Errors);
        var zones = GetMotherboardZones(inventory, out var zoneErrors);
        errors.AddRange(zoneErrors);
        var colorChanged = 0;
        var brightnessChanged = 0;
        var styleChanged = 0;

        foreach (var zone in zones)
        {
            var infoStatus = Api.GetLedInfo(zone.Type, zone.Index, out var area, out var styles);
            if (infoStatus != Status.Ok)
            {
                errors.Add($"{zone.Label}: GetLedInfo={infoStatus}");
                continue;
            }

            var label = string.IsNullOrWhiteSpace(area) ? zone.Label : $"{zone.Label} {area}";
            var style = StaticStyle(styles);
            if (style is null) errors.Add($"{label}: нет поддерживаемого статического режима");
            else
            {
                var status = Api.SetLedStyle(zone.Type, zone.Index, style);
                if (status == Status.Ok) styleChanged++;
                else errors.Add($"{label}: SetLedStyle={status}");
            }

            ApplyBrightness(zone, label, enabled ? brightnessPercent : 0, ref brightnessChanged, errors);
            var actualColor = enabled && brightnessPercent > 0 ? color : Color.Black;
            if (SetZoneColor(zone, area, actualColor) == Status.Ok) colorChanged++;
            else errors.Add($"{label}: SetLedColor/Sync/Ex завершились ошибкой");
        }

        var modeApplied = zones.Count > 0 && styleChanged == zones.Count;
        return new SdkApplyResult(zones.Count, colorChanged, brightnessChanged, styleChanged, errors,
            ModeApplied: modeApplied, ColorControlAvailable: colorChanged == zones.Count && zones.Count > 0);
    }

    private static SdkApplyResult ApplyEffect(LumaSettings settings, DeviceCapabilities capabilities) => SdkSession.Run(() =>
    {
        var errors = new List<string>();
        var stylesChanged = 0;
        var colorsChanged = 0;
        var speedsChanged = 0;
        var brightnessChanged = 0;
        var speedRejected = false;
        var color = ColorTranslator.FromHtml(settings.Color);
        var effect = capabilities.FindEffect(settings.EffectId)!;
        var changedZones = new List<ZoneCapability>();

        foreach (var zone in capabilities.Zones)
        {
            var style = zone.Styles.FirstOrDefault(value => value.Equals(effect.Id, StringComparison.OrdinalIgnoreCase));
            if (style is null)
            {
                errors.Add($"{zone.Name}: стиль «{effect.Id}» больше не поддерживается.");
                continue;
            }
            var effectColor = settings.Enabled && settings.Brightness > 0 ? color : Color.Black;
            var colorStatus = SetZoneColor(zone, zone.Name, effectColor);
            if (colorStatus == Status.Ok) colorsChanged++;
            else errors.Add($"{zone.Name}: SDK не принял цветовой параметр для «{effect.Name}».");

            // Color setters may switch Mystic Light back to its solid-color style.
            // Set the selected effect after color so the requested mode stays active.
            var styleStatus = Api.SetLedStyle(zone.DeviceType, zone.Index, style);
            if (styleStatus != Status.Ok)
            {
                errors.Add($"{zone.Name}: SetLedStyle({style})={styleStatus}");
                continue;
            }
            changedZones.Add(zone);

            ApplyBrightness(zone, zone.Name, settings.Enabled ? settings.Brightness : 0, ref brightnessChanged, errors);

            if (effect.SupportsSpeed && settings.SpeedPercent >= 0)
            {
                var minimum = minimumSpeedCache.TryGetValue(SpeedCacheKey(zone), out var cachedMinimum) ? cachedMinimum : 0;
                var speedStatus = SetEffectSpeed(zone, ToDeviceSpeed(zone.MaximumSpeed, settings.SpeedPercent, minimum));
                if (speedStatus == Status.Ok) speedsChanged++;
                else
                {
                    speedRejected |= speedStatus == Status.NotSupported;
                    errors.Add($"{zone.Name}: SetLedSpeed={speedStatus}");
                }
            }

            // Brightness or speed setters can also reset a style on some SDK
            // implementations. Re-apply the selected effect last and verify
            // it through the SDK so a color change cannot leave Static active.
            styleStatus = Api.SetLedStyle(zone.DeviceType, zone.Index, style);
            if (styleStatus != Status.Ok)
            {
                errors.Add($"{zone.Name}: повторный SetLedStyle({style})={styleStatus}");
                continue;
            }
            var readStyleStatus = Api.GetLedStyle(zone.DeviceType, zone.Index, out var appliedStyle);
            if (readStyleStatus == Status.Ok && !string.Equals(appliedStyle, style, StringComparison.OrdinalIgnoreCase))
            {
                styleStatus = Api.SetLedStyle(zone.DeviceType, zone.Index, style);
                if (styleStatus != Status.Ok)
                {
                    errors.Add($"{zone.Name}: эффект «{effect.Name}» сбросился после применения цвета; SetLedStyle={styleStatus}");
                    continue;
                }
                readStyleStatus = Api.GetLedStyle(zone.DeviceType, zone.Index, out appliedStyle);
                if (readStyleStatus == Status.Ok && !string.Equals(appliedStyle, style, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{zone.Name}: устройство оставило стиль «{appliedStyle}» вместо «{style}».");
                    continue;
                }
            }
            stylesChanged++;
        }

        if (stylesChanged != capabilities.Zones.Count)
            RestorePreviousStyles(changedZones, errors);

        return new SdkApplyResult(capabilities.Zones.Count, colorsChanged, brightnessChanged,
            stylesChanged, errors, speedsChanged,
            ModeApplied: capabilities.Zones.Count > 0 && stylesChanged == capabilities.Zones.Count,
            ColorControlAvailable: capabilities.Zones.Count > 0 && colorsChanged == capabilities.Zones.Count,
            SpeedControlRejected: speedRejected);
    });

    private static SdkApplyResult ApplyGradient(LumaSettings settings, DeviceCapabilities capabilities) => SdkSession.Run(() =>
    {
        var errors = new List<string>();
        var stylesChanged = 0;
        var colorsChanged = 0;
        var brightnessChanged = 0;
        var start = ColorTranslator.FromHtml(settings.GradientStart);
        var end = ColorTranslator.FromHtml(settings.GradientEnd);
        var changedZones = new List<ZoneCapability>();

        foreach (var zone in capabilities.Zones)
        {
            var style = zone.Styles.FirstOrDefault(value => value.Equals("Direct Lighting Control", StringComparison.OrdinalIgnoreCase));
            if (style is null || zone.Leds.Count == 0)
            {
                errors.Add($"{zone.Name}: Direct Lighting Control или список LED недоступен.");
                continue;
            }
            var styleStatus = Api.SetLedStyle(zone.DeviceType, zone.Index, style);
            if (styleStatus != Status.Ok)
            {
                errors.Add($"{zone.Name}: SetLedStyle(Direct Lighting Control)={styleStatus}");
                continue;
            }
            stylesChanged++;
            changedZones.Add(zone);
            ApplyBrightness(zone, zone.Name, settings.Enabled ? settings.Brightness : 0, ref brightnessChanged, errors);

            var gradient = settings.Enabled && settings.Brightness > 0
                ? BuildGradient(start, end, zone.Leds.Count)
                : BuildGradient(Color.Black, Color.Black, zone.Leds.Count);
            var names = zone.Leds.Select(led => led.Name).ToArray();
            var colors = zone.Leds.Select((led, index) =>
            {
                if (!settings.Enabled) return Color.Black;
                var key = $"{zone.DeviceType}|{zone.Index}|{led.AreaIndex}|{led.Name}";
                if (!settings.LedColors.TryGetValue(key, out var hex)) return gradient[index];
                try { return ColorTranslator.FromHtml(hex); }
                catch (ArgumentException) { return gradient[index]; }
            }).ToArray();
            var red = colors.Select(value => (int)value.R).ToArray();
            var green = colors.Select(value => (int)value.G).ToArray();
            var blue = colors.Select(value => (int)value.B).ToArray();
            var status = Api.SetLedColors(zone.DeviceType, zone.Index, ref names, red, green, blue);
            if (status == Status.Ok) colorsChanged++;
            else errors.Add($"{zone.Name}: SetLedColors={status}");
        }

        var succeeded = capabilities.Zones.Count > 0 && stylesChanged == capabilities.Zones.Count && colorsChanged == capabilities.Zones.Count;
        if (!succeeded) RestorePreviousStyles(changedZones, errors);
        return new SdkApplyResult(capabilities.Zones.Count, colorsChanged, brightnessChanged,
            stylesChanged, errors, ModeApplied: succeeded);
    });

    private static void RestorePreviousStyles(IEnumerable<ZoneCapability> changedZones, List<string> errors)
    {
        foreach (var zone in changedZones)
        {
            if (string.IsNullOrWhiteSpace(zone.CurrentStyle))
            {
                errors.Add($"{zone.Name}: предыдущий стиль неизвестен, откат невозможен.");
                continue;
            }

            var status = Api.SetLedStyle(zone.DeviceType, zone.Index, zone.CurrentStyle);
            if (status != Status.Ok)
                errors.Add($"{zone.Name}: не удалось восстановить стиль «{zone.CurrentStyle}»: SetLedStyle={status}.");
        }
    }

    private static Status SetEffectSpeed(ZoneCapability zone, uint requested)
    {
        var key = SpeedCacheKey(zone);
        if (minimumSpeedCache.TryGetValue(key, out var minimum))
            return Api.SetLedSpeed(zone.DeviceType, zone.Index, Math.Max(minimum, requested));

        var status = Api.SetLedSpeed(zone.DeviceType, zone.Index, requested);
        if (status != Status.InvalidArgument) return status;

        // Mystic Light exposes the maximum but not the minimum speed. When a
        // low value is rejected, find the lowest accepted value between it and
        // the current known-valid value, then use that range for future input.
        var currentStatus = Api.GetLedSpeed(zone.DeviceType, zone.Index, out var current);
        if (currentStatus != Status.Ok) return status;
        var low = requested;
        var high = current > requested ? current : zone.MaximumSpeed;
        if (high <= low) return status;

        while (high - low > 1)
        {
            var middle = low + (high - low) / 2;
            var probe = Api.SetLedSpeed(zone.DeviceType, zone.Index, middle);
            if (probe == Status.Ok) high = middle;
            else if (probe == Status.InvalidArgument) low = middle;
            else return probe;
        }

        status = Api.SetLedSpeed(zone.DeviceType, zone.Index, high);
        if (status == Status.Ok) minimumSpeedCache[key] = high;
        return status;
    }

    private static string SpeedCacheKey(ZoneCapability zone) => $"{zone.DeviceType}|{zone.Index}";

    private static DeviceCapabilities LoadCapabilitiesCore()
    {
        var inventory = ReadInventory();
        var errors = new List<string>(inventory.Errors);
        var motherboardZones = GetMotherboardZones(inventory, out var zoneErrors);
        errors.AddRange(zoneErrors);
        if (motherboardZones.Count == 0) return new([], [], 50, false, errors);

        var ledNamesByDevice = new Dictionary<string, (bool Available, List<LedInfo> Leds, string? Error)>(StringComparer.OrdinalIgnoreCase);
        var zones = new List<ZoneCapability>();
        foreach (var zone in motherboardZones)
        {
            var warnings = new List<string>();
            var infoStatus = Api.GetLedInfo(zone.Type, zone.Index, out var name, out var styles);
            if (infoStatus != Status.Ok || styles is null)
            {
                warnings.Add($"GetLedInfo={infoStatus}");
                zones.Add(new(zone.Type, zone.Index, zone.Label, [], null, 0, 0, [], warnings));
                continue;
            }

            var currentStyleStatus = Api.GetLedStyle(zone.Type, zone.Index, out var currentStyle);
            if (currentStyleStatus != Status.Ok) warnings.Add($"GetLedStyle={currentStyleStatus}");
            var speedStatus = Api.GetLedSpeed(zone.Type, zone.Index, out var speed);
            var maxSpeedStatus = Api.GetLedMaxSpeed(zone.Type, zone.Index, out var maxSpeed);
            if (speedStatus != Status.Ok || maxSpeedStatus != Status.Ok)
                warnings.Add($"Скорость: GetLedSpeed={speedStatus}; GetLedMaxSpeed={maxSpeedStatus}");

            if (!ledNamesByDevice.TryGetValue(zone.Type, out var ledNames))
            {
                var ledNameStatus = Api.GetLedName(zone.Type, out var rawLedNames);
                var parsed = ledNameStatus == Status.Ok ? ParseLedNames(rawLedNames) : [];
                ledNames = (ledNameStatus == Status.Ok, parsed,
                    ledNameStatus == Status.Ok ? null : $"GetLedName={ledNameStatus}");
                ledNamesByDevice.Add(zone.Type, ledNames);
            }
            if (!ledNames.Available && styles.Any(style => style.Equals("Direct Lighting Control", StringComparison.OrdinalIgnoreCase)))
                warnings.Add(ledNames.Error ?? "Список индивидуальных LED недоступен.");

            var areaLeds = ledNames.Leds.Where(led => led.AreaIndex == zone.Index).ToArray();
            var label = string.IsNullOrWhiteSpace(name) ? zone.Label : name;
            zones.Add(new(zone.Type, zone.Index, label, styles.Where(style => !string.IsNullOrWhiteSpace(style)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                currentStyleStatus == Status.Ok ? currentStyle : null,
                speedStatus == Status.Ok ? speed : 0,
                maxSpeedStatus == Status.Ok ? maxSpeed : 0,
                areaLeds, warnings));
        }

        var sharedStyles = GetSharedStyles(zones);
        var effects = sharedStyles
            .Where(style => !IsStaticStyle(style) && !style.Equals("Direct Lighting Control", StringComparison.OrdinalIgnoreCase))
            .OrderBy(style => style, StringComparer.CurrentCultureIgnoreCase)
            .Select(style => new EffectInfo(style, TranslateEffectName(style), zones.All(zone => zone.SupportsSpeed)))
            .ToArray();
        var supportsDirect = zones.Count > 0 && zones.All(zone => zone.SupportsDirectLighting);
        var speedPercent = zones.Where(zone => zone.SupportsSpeed)
            .Select(zone => (int)Math.Clamp(((ulong)zone.CurrentSpeed * 100 + zone.MaximumSpeed / 2u) / zone.MaximumSpeed, 0UL, 100UL))
            .DefaultIfEmpty(50).Order().ElementAt(zones.Count(zone => zone.SupportsSpeed) / 2);
        return new(zones, effects, speedPercent, supportsDirect, errors);
    }

    internal static IReadOnlyList<string> GetSharedStyles(IReadOnlyList<ZoneCapability> zones)
    {
        if (zones.Count == 0 || zones.Any(zone => zone.Styles.Count == 0)) return [];
        return zones[0].Styles
            .Where(style => zones.Skip(1).All(zone => zone.Styles.Any(candidate => candidate.Equals(style, StringComparison.OrdinalIgnoreCase))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string TranslateEffectName(string style) => style.Trim().ToLowerInvariant() switch
    {
        "breathing" => "Дыхание",
        "flashing" => "Мигание",
        "double flashing" => "Двойное мигание",
        "rainbow" => "Радуга",
        "color cycle" => "Смена цветов",
        "meteor" => "Метеор",
        "lightning" => "Молния",
        "marquee" => "Бегущий свет",
        "rainbow wave" => "Радужная волна",
        "color ring" => "Цветное кольцо",
        "stack" => "Стопка",
        "stacking" => "Накопление",
        "planetary" => "Планетарный",
        "steady" => "Постоянное свечение",
        "static" => "Статический цвет",
        "no animation" => "Без анимации",
        "direct all sync" => "Общий статический цвет",
        _ => style
    };

    private static bool IsStaticStyle(string style) =>
        style.Equals("Direct All Sync", StringComparison.OrdinalIgnoreCase) ||
        style.Equals("Static", StringComparison.OrdinalIgnoreCase) ||
        style.Equals("Steady", StringComparison.OrdinalIgnoreCase) ||
        style.Equals("Direct", StringComparison.OrdinalIgnoreCase) ||
        style.Equals("No animation", StringComparison.OrdinalIgnoreCase);

    private static string? StaticStyle(string[]? styles) => styles?.FirstOrDefault(style =>
        style.Equals("Direct All Sync", StringComparison.OrdinalIgnoreCase)) ??
        styles?.FirstOrDefault(IsStaticStyle);

    private static List<LedInfo> ParseLedNames(string[]? names)
    {
        var leds = new List<LedInfo>();
        foreach (var raw in names ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var separator = raw.IndexOf(':');
            if (separator <= 0 || separator == raw.Length - 1 ||
                !int.TryParse(raw.AsSpan(0, separator), out var areaIndex)) continue;
            var name = raw[(separator + 1)..].Trim();
            if (name.Length > 0) leds.Add(new(areaIndex, name));
        }
        return leds;
    }

    private static void ApplyBrightness(ZoneCapability zone, string label, int percent, ref int changed, List<string> errors)
    {
        var maxStatus = Api.GetLedMaxBright(zone.DeviceType, zone.Index, out var maximum);
        if (maxStatus != Status.Ok)
        {
            errors.Add($"{label}: GetLedMaxBright={maxStatus}");
            return;
        }
        var status = Api.SetLedBright(zone.DeviceType, zone.Index, percent == 0 ? 0 : ToDeviceBrightness(maximum, percent));
        if (status == Status.Ok) changed++;
        else errors.Add($"{label}: SetLedBright={status}");
    }

    private static void ApplyBrightness(Zone zone, string label, int percent, ref int changed, List<string> errors)
    {
        var maxStatus = Api.GetLedMaxBright(zone.Type, zone.Index, out var maximum);
        if (maxStatus != Status.Ok)
        {
            errors.Add($"{label}: GetLedMaxBright={maxStatus}");
            return;
        }
        var status = Api.SetLedBright(zone.Type, zone.Index, percent == 0 ? 0 : ToDeviceBrightness(maximum, percent));
        if (status == Status.Ok) changed++;
        else errors.Add($"{label}: SetLedBright={status}");
    }

    private static Status SetZoneColor(ZoneCapability zone, string? area, Color color)
        => SetZoneColor(zone.DeviceType, zone.Index, area, color);

    private static Status SetZoneColor(Zone zone, string? area, Color color)
        => SetZoneColor(zone.Type, zone.Index, area, color);

    private static Status SetZoneColor(string type, int index, string? area, Color color)
    {
        var status = Api.SetLedColor(type, index, color.R, color.G, color.B);
        if (status == Status.Ok || string.IsNullOrWhiteSpace(area)) return status;
        status = Api.SetLedColorSync(type, index, area, color.R, color.G, color.B, 1);
        if (status == Status.InvalidArgument)
            status = Api.SetLedColorSync(type, index, area, color.R, color.G, color.B, 0);
        if (status == Status.Ok) return status;
        status = Api.SetLedColorEx(type, index, area, color.R, color.G, color.B, 1);
        if (status == Status.InvalidArgument)
            status = Api.SetLedColorEx(type, index, area, color.R, color.G, color.B, 0);
        return status;
    }

    private static Inventory ReadInventory()
    {
        var status = Api.GetDeviceInfo(out var types, out var counts);
        if (status != Status.Ok) return new([], [$"GetDeviceInfo={status}"]);
        if (types is null || counts is null) return new([], ["GetDeviceInfo вернул пустой список устройств."]);
        var devices = new List<Device>();
        var errors = new List<string>();
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            if (string.IsNullOrWhiteSpace(type)) { errors.Add($"GetDeviceInfo[{i}]: пустой тип устройства"); continue; }
            var nameStatus = Api.GetDeviceName(type, out var names);
            if (nameStatus != Status.Ok) errors.Add($"{type}: GetDeviceName={nameStatus}");
            int.TryParse(i < counts.Length ? counts[i] : null, out var count);
            devices.Add(new(type, string.Join(" / ", names ?? []), Math.Max(0, count)));
        }
        if (counts.Length < types.Length)
            errors.Add($"GetDeviceInfo вернул {types.Length} типов устройств, но только {counts.Length} значений количества зон.");
        return new(devices, errors);
    }

    private static List<Zone> GetMotherboardZones(Inventory inventory, out List<string> errors)
    {
        errors = [];
        var result = new List<Zone>();
        foreach (var device in inventory.Devices)
        {
            if (!device.Type.Contains("MB", StringComparison.OrdinalIgnoreCase) &&
                !device.Name.Contains("motherboard", StringComparison.OrdinalIgnoreCase) &&
                !device.Name.Contains("материн", StringComparison.OrdinalIgnoreCase)) continue;
            for (var i = 0; i < device.Count; i++) result.Add(new(device.Type, i, $"{device.Type}[{i}]"));
        }
        if (result.Count == 0) errors.Add("Материнская плата не найдена среди устройств Mystic Light.");
        return result;
    }

    private static SdkApplyResult Failure(int zones, string message) =>
        new(zones, 0, 0, 0, [message]);

    private sealed record Device(string Type, string Name, int Count);
    private sealed record Zone(string Type, int Index, string Label);
    private sealed record Inventory(List<Device> Devices, List<string> Errors);
}

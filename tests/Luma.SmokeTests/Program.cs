using System.Drawing;

var root = Path.Combine(Path.GetTempPath(), "Luma.SmokeTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    TestMissingSettings(root);
    TestSettingsRoundTrip(root);
    TestCorruptBackup(root);
    TestFutureSchemaPreserved(root);
    TestLegacyPresetMigration(root);
    TestBrightnessMapping();
    TestSpeedAndGradientMath();
    TestEffectTranslations();
    TestSharedStyleIntersection();
    TestMutexTimeout();
    TestCommandArguments();
    Console.WriteLine("PASS: smoke checks");
}
finally
{
    try { Directory.Delete(root, true); }
    catch (IOException ex) { Console.Error.WriteLine($"Temporary test cleanup: {ex.Message}"); }
}

static void TestMissingSettings(string root)
{
    var store = new SettingsStore(Path.Combine(root, "missing", "settings.json"));
    var load = store.Load();
    Check(load.State == SettingsLoadState.Missing, "missing file must use defaults");
    Check(load.Settings.SchemaVersion == LumaSettings.CurrentSchemaVersion, "defaults carry current schema");
}

static void TestSettingsRoundTrip(string root)
{
    var path = Path.Combine(root, "roundtrip", "settings.json");
    var store = new SettingsStore(path);
    var settings = new LumaSettings { Color = "#0AC48D", Brightness = 37, Enabled = false };
    settings.Presets[2] = "#C01020";
    settings.RecentColors = ["#0AC48D", "#123456"];
    settings.CustomColors[4] = 0x00332211;
    settings.LedColors["MSI_MB|0|0|LED 1"] = "#AABBCC";
    settings.LightingPresets = settings.Presets.Select(color => new LightingPreset { Color = color }).ToArray();
    settings.LightingPresets[0].Mode = LightingModes.DirectLighting;
    settings.LightingPresets[0].GradientStart = "#102030";
    settings.LightingPresets[0].GradientEnd = "#405060";
    settings.LightingPresets[0].LedColors["MSI_MB|0|0|LED 1"] = "#F0E0D0";
    Check(store.Save(settings).Success, "settings save succeeds");
    var loaded = store.Load();
    Check(loaded.Settings.Color == "#0AC48D" && loaded.Settings.Brightness == 37 && !loaded.Settings.Enabled, "basic settings persist");
    Check(loaded.Settings.Presets[2] == "#C01020" && loaded.Settings.RecentColors.Contains("#123456"), "presets and history persist");
    Check(loaded.Settings.CustomColors[4] == 0x00332211, "custom dialog colors persist");
    Check(loaded.Settings.LedColors["MSI_MB|0|0|LED 1"] == "#AABBCC", "individual LED colors persist");
    Check(loaded.Settings.LightingPresets![0].LedColors["MSI_MB|0|0|LED 1"] == "#F0E0D0",
        "individual LED colors persist inside lighting presets");
    Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp-*").Any(), "atomic save leaves no temporary files");
}

static void TestCorruptBackup(string root)
{
    var path = Path.Combine(root, "corrupt", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    const string invalid = "{ this is not JSON";
    File.WriteAllText(path, invalid);
    var load = new SettingsStore(path).Load();
    Check(load.State == SettingsLoadState.CorruptBackedUp, "corrupt JSON detected and copied");
    Check(File.ReadAllText(path) == invalid, "corrupt source is not overwritten on load");
    Check(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "settings.json.corrupt-*").Any(), "corrupt backup exists");
}

static void TestFutureSchemaPreserved(string root)
{
    var path = Path.Combine(root, "future", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    const string future = "{\"SchemaVersion\":999,\"Color\":\"#010203\"}";
    File.WriteAllText(path, future);
    var load = new SettingsStore(path).Load();
    Check(load.State == SettingsLoadState.FutureVersion, "newer schema detected");
    Check(File.ReadAllText(path) == future, "newer schema source is not overwritten");
}

static void TestBrightnessMapping()
{
    Check(MysticLightController.ToDeviceBrightness(100, 0) == 1, "zero percent maps to lowest valid SDK value");
    Check(MysticLightController.ToDeviceBrightness(100, 100) == 100, "100 percent maps to maximum");
    Check(MysticLightController.ToDeviceBrightness(0, 100) == 0, "empty SDK range stays zero");
    Check(MysticLightController.ToDeviceBrightness(13, 50) == 7, "brightness rounding is stable");
}

static void TestLegacyPresetMigration(string root)
{
    var path = Path.Combine(root, "legacy", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, """
        {"SchemaVersion":2,"Color":"#0AC48D","Brightness":37,"Enabled":true,
         "Presets":["#112233","#445566","#778899","#AABBCC"],"CustomColors":[1,2,3]}
        """);
    var load = new SettingsStore(path).Load();
    Check(load.Settings.SchemaVersion == LumaSettings.CurrentSchemaVersion, "legacy settings migrate to current schema");
    Check(load.Settings.LightingPresets is { Length: 4 }, "legacy presets are converted to effect-aware presets");
    Check(load.Settings.LightingPresets![2].Color == "#778899" && load.Settings.LightingPresets[2].Mode == LightingModes.Static,
        "legacy preset colors survive migration as static presets");
}

static void TestSpeedAndGradientMath()
{
    Check(MysticLightController.ToDeviceSpeed(80, 50) == 40, "speed percent maps to the device-reported maximum");
    Check(MysticLightController.ToDeviceSpeed(80, 100) == 80, "100 percent speed maps to SDK maximum");
    Check(MysticLightController.ToDeviceSpeed(80, 0, 16) == 16, "speed mapping respects the discovered device minimum");
    Check(MysticLightController.ToDeviceSpeed(80, 15, 16) == 26, "low slider speeds map inside the supported device range");
    var gradient = MysticLightController.BuildGradient(Color.Black, Color.White, 3);
    Check(gradient.Length == 3 && gradient[0].R == 0 && gradient[1].R == 128 && gradient[2].R == 255,
        "gradient interpolates endpoints in SDK LED order");
    var single = MysticLightController.BuildGradient(Color.Red, Color.Blue, 1).Single();
    Check(single.R == Color.Red.R && single.B == Color.Red.B,
        "single LED uses the start color");
}

static void TestEffectTranslations()
{
    Check(MysticLightController.TranslateEffectName("Breathing") == "Дыхание", "Breathing has a Russian display name");
    Check(MysticLightController.TranslateEffectName("Double Flashing") == "Двойное мигание", "Double Flashing has a Russian display name");
    Check(MysticLightController.TranslateEffectName("Rainbow") == "Радуга", "Rainbow has a Russian display name");
}

static void TestSharedStyleIntersection()
{
    ZoneCapability[] zones =
    [
        new("MSI_MB", 0, "Zone A", ["Static", "Breathing", "Rainbow"], "Static", 3, 10, [], []),
        new("MSI_MB", 1, "Zone B", ["Direct All Sync", "Breathing", "Flash"], "Breathing", 4, 10, [], [])
    ];
    var common = MysticLightController.GetSharedStyles(zones);
    Check(common.Count == 1 && common[0] == "Breathing", "only styles supported by every controlled zone are exposed");
}

static void TestMutexTimeout()
{
    using var held = NamedMutexGate.Acquire(TimeSpan.FromSeconds(1));
    var timedOut = Task.Run(() =>
    {
        try { using var ignored = NamedMutexGate.Acquire(TimeSpan.FromMilliseconds(25)); return false; }
        catch (SdkBusyException) { return true; }
    }).GetAwaiter().GetResult();
    Check(timedOut, "a concurrent process times out while the SDK mutex is held");
}

static void TestCommandArguments()
{
    Check(LumaProgram.TryParseCommand(["off"], out _), "off command parses");
    Check(LumaProgram.TryParseCommand(["brightness-up"], out _), "brightness-up command parses");
    Check(LumaProgram.TryParseCommand(["brightness-down"], out _), "brightness-down command parses");
    Check(LumaProgram.TryParseCommand(["255", "128", "0"], out _), "RGB command parses");
    Check(!LumaProgram.TryParseCommand(["256", "0", "0"], out _), "out-of-range RGB is rejected");
    Check(!LumaProgram.TryParseCommand(["brightness-up", "extra"], out _), "extra command arguments are rejected");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
}

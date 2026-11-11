using System.Runtime.InteropServices;
using System.Windows.Forms;

internal enum Status : int
{
    Ok = 0, Error = -1, Timeout = -2, NotImplemented = -3, NotInitialized = -4,
    InvalidArgument = -101, DeviceNotFound = -102, NotSupported = -103
}

internal static class Api
{
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_Initialize")] internal static extern Status Initialize();
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_Release")] internal static extern Status Release();
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_GetErrorMessage")] internal static extern Status GetErrorMessage(int code, [MarshalAs(UnmanagedType.BStr)] out string message);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_GetDeviceInfo")] internal static extern Status GetDeviceInfo([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] types, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] counts);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_GetDeviceName")] internal static extern Status GetDeviceName([MarshalAs(UnmanagedType.BStr)] string type, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] names);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_GetLedInfo")] internal static extern Status GetLedInfo([MarshalAs(UnmanagedType.BStr)] string type, int index, [MarshalAs(UnmanagedType.BStr)] out string name, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] styles);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_GetLedStyle")] internal static extern Status GetLedStyle([MarshalAs(UnmanagedType.BStr)] string type, int index, [MarshalAs(UnmanagedType.BStr)] out string style);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_GetLedName")] internal static extern Status GetLedName([MarshalAs(UnmanagedType.BStr)] string type, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] names);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_SetLedStyle")] internal static extern Status SetLedStyle([MarshalAs(UnmanagedType.BStr)] string type, int index, [MarshalAs(UnmanagedType.BStr)] string style);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_SetLedColor")] internal static extern Status SetLedColor([MarshalAs(UnmanagedType.BStr)] string type, int index, int r, int g, int b);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_SetLedColorEx")] internal static extern Status SetLedColorEx([MarshalAs(UnmanagedType.BStr)] string type, int areaIndex, [MarshalAs(UnmanagedType.BStr)] string ledName, int r, int g, int b, int sync);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_SetLedColorSync")] internal static extern Status SetLedColorSync([MarshalAs(UnmanagedType.BStr)] string type, int areaIndex, [MarshalAs(UnmanagedType.BStr)] string ledName, int r, int g, int b, int sync);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_GetLedMaxBright")] internal static extern Status GetLedMaxBright([MarshalAs(UnmanagedType.BStr)] string type, int index, out uint level);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_GetLedBright")] internal static extern Status GetLedBright([MarshalAs(UnmanagedType.BStr)] string type, int index, out uint level);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_GetLedMaxSpeed")] internal static extern Status GetLedMaxSpeed([MarshalAs(UnmanagedType.BStr)] string type, int index, out uint level);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_GetLedSpeed")] internal static extern Status GetLedSpeed([MarshalAs(UnmanagedType.BStr)] string type, int index, out uint level);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_SetLedBright")] internal static extern Status SetLedBright([MarshalAs(UnmanagedType.BStr)] string type, int index, uint level);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_SetLedSpeed")] internal static extern Status SetLedSpeed([MarshalAs(UnmanagedType.BStr)] string type, int index, uint level);
    [DllImport("MysticLight_SDK_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MLAPI_SetLedColors")] internal static extern Status SetLedColors([MarshalAs(UnmanagedType.BStr)] string type, int areaIndex, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] ref string[] ledNames, int[] red, int[] green, int[] blue);
}

internal static class LumaProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 0 || Is(args[0], "tray", "preview", "window"))
        {
            Application.Run(new TrayWindow(args.Length > 0 && Is(args[0], "preview", "window")));
            return 0;
        }

        if (!TryParseCommand(args, out var command))
        {
            MessageBox.Show("Использование: Luma.exe [tray | off | brightness-up | brightness-down | R G B]", "Luma", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        try
        {
            var result = command switch
            {
                Command.Off => MysticLightController.TurnOff(),
                Command.BrightnessUp => MysticLightController.AdjustBrightness(1),
                Command.BrightnessDown => MysticLightController.AdjustBrightness(-1),
                Command.Rgb rgb => MysticLightController.ApplyColor(rgb.R, rgb.G, rgb.B),
                _ => throw new InvalidOperationException("Неизвестная команда.")
            };
            if (result.Changes == 0)
            {
                WriteCommandLog("Команда не изменила ни одну зону.", result.Errors);
                return 4;
            }
            if (result.HasErrors)
            {
                WriteCommandLog($"Команда выполнена частично: зон={result.ZonesFound}, цвет={result.ColorChanged}, яркость={result.BrightnessChanged}, режим={result.StyleChanged}.", result.Errors);
                return 5;
            }
            return 0;
        }
        catch (SdkBusyException ex) { WriteCommandLog(ex.Message, []); return 6; }
        catch (SdkOperationException ex) { WriteCommandLog(ex.Message, [ex.Status.ToString()]); return 2; }
        catch (Exception ex) { WriteCommandLog("Ошибка Mystic Light.", [ex.ToString()]); return 3; }
    }

    private static bool Is(string value, params string[] options) => options.Any(option => value.Equals(option, StringComparison.OrdinalIgnoreCase));

    internal static bool TryParseCommand(string[] args, out Command command)
    {
        command = null!;
        if (args.Length == 1 && Is(args[0], "off")) { command = new Command.Off(); return true; }
        if (args.Length == 1 && Is(args[0], "brightness-up")) { command = new Command.BrightnessUp(); return true; }
        if (args.Length == 1 && Is(args[0], "brightness-down")) { command = new Command.BrightnessDown(); return true; }
        if (args.Length == 3 && byte.TryParse(args[0], out var r) && byte.TryParse(args[1], out var g) && byte.TryParse(args[2], out var b))
        { command = new Command.Rgb(r, g, b); return true; }
        return false;
    }

    private static void WriteCommandLog(string summary, IReadOnlyList<string> details)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MysticLightControl");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "errors.log"), $"{DateTime.Now:O} {summary}{Environment.NewLine}{string.Join(Environment.NewLine, details)}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal abstract record Command
    {
        public sealed record Off : Command;
        public sealed record BrightnessUp : Command;
        public sealed record BrightnessDown : Command;
        public sealed record Rgb(byte R, byte G, byte B) : Command;
    }
}

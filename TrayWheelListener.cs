using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Windows.Forms;

internal sealed class TrayWheelListener : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseWheel = 0x020A;
    private const int HcAction = 0;
    private const int WheelDelta = 120;
    private static readonly ConcurrentDictionary<IntPtr, HookProc> CallbackRoots = new();
    private readonly HookProc callback;
    private readonly IntPtr hook;
    private NotifyIconIdentifier identifier;
    private int partialDelta;
    private readonly Action<string>? diagnostics;
    private bool disposed;

    public event Action<int>? Wheel;

    public TrayWheelListener(NotifyIcon icon, Action<string>? diagnostics = null)
    {
        this.diagnostics = diagnostics;
        if (!icon.Visible) throw new InvalidOperationException("Значок NotifyIcon должен быть видимым до установки обработки колёсика.");
        identifier = GetIdentifier(icon);
        callback = HookCallback;
        hook = SetWindowsHookEx(WhMouseLl, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
            throw new InvalidOperationException("Не удалось включить обработку колёсика мыши для значка Luma.");
        CallbackRoots[hook] = callback;
    }

    private static NotifyIconIdentifier GetIdentifier(NotifyIcon icon)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(NotifyIcon);
        var idField = type.GetField("_id", flags) ?? type.GetField("id", flags)
            ?? throw new InvalidOperationException("В этой версии WinForms не найден идентификатор NotifyIcon.");
        var windowField = type.GetField("_window", flags) ?? type.GetField("window", flags)
            ?? throw new InvalidOperationException("В этой версии WinForms не найдено окно NotifyIcon.");
        var nativeWindow = windowField.GetValue(icon)
            ?? throw new InvalidOperationException("Окно значка Luma ещё не создано.");
        var handleProperty = nativeWindow.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Не удалось получить окно значка Luma.");
        var hwnd = (IntPtr)(handleProperty.GetValue(nativeWindow) ?? IntPtr.Zero);
        var id = Convert.ToUInt32(idField.GetValue(icon));
        if (hwnd == IntPtr.Zero || id == 0)
            throw new InvalidOperationException("Значок Luma ещё не зарегистрирован в системном трее.");
        return new NotifyIconIdentifier { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = hwnd, Id = id };
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code == HcAction && wParam.ToInt32() == WmMouseWheel)
            {
                var mouse = Marshal.PtrToStructure<MouseHookData>(lParam);
                if (Shell_NotifyIconGetRect(ref identifier, out var bounds) == 0 &&
                    mouse.Point.X >= bounds.Left && mouse.Point.X < bounds.Right &&
                    mouse.Point.Y >= bounds.Top && mouse.Point.Y < bounds.Bottom)
                {
                    partialDelta += unchecked((short)(mouse.MouseData >> 16));
                    var steps = partialDelta / WheelDelta;
                    partialDelta %= WheelDelta;
                    if (steps != 0) Wheel?.Invoke(steps);
                }
                else
                {
                    partialDelta = 0;
                }
            }
        }
        catch (Exception ex)
        {
            // Never let a managed exception escape through the global Win32 hook.
            try { diagnostics?.Invoke($"Tray mouse-hook callback failed: {ex}"); } catch (Exception) { }
        }
        try { return CallNextHookEx(hook, code, wParam, lParam); }
        catch (Exception ex)
        {
            try { diagnostics?.Invoke($"CallNextHookEx failed: {ex}"); } catch (Exception) { }
            return IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (hook != IntPtr.Zero && UnhookWindowsHookEx(hook))
            CallbackRoots.TryRemove(hook, out _);
        else if (hook != IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            try { diagnostics?.Invoke($"UnhookWindowsHookEx failed with Win32 error {error}."); } catch (Exception) { }
        }
        Wheel = null;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData { public Point Point; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect iconLocation);
}

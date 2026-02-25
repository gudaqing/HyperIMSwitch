using System;
using System.Runtime.InteropServices;

namespace HyperIMSwitch.Interop;

internal static partial class HotkeyNative
{
    public const uint WM_HOTKEY  = 0x0312;
    public const uint WM_QUIT    = 0x0012;
    public const uint WM_USER    = 0x0400;

    // MOD_ flags
    public const uint MOD_ALT      = 0x0001;
    public const uint MOD_CONTROL  = 0x0002;
    public const uint MOD_SHIFT    = 0x0004;
    public const uint MOD_WIN      = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
}

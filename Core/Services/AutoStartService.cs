using System.Diagnostics;
using Microsoft.Win32;

namespace HyperIMSwitch.Core.Services;

public sealed class AutoStartService
{
    private const string RunKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "HyperIMSwitch";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) != null;
        }
    }

    public void Enable()
    {
        string? exe = Process.GetCurrentProcess().MainModule?.FileName;
        if (exe == null) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.SetValue(AppName, $"\"{exe}\"");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    public void SetEnabled(bool enable)
    {
        if (enable) Enable(); else Disable();
    }
}

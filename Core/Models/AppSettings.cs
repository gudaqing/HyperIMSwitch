using System.Collections.Generic;

namespace HyperIMSwitch.Core.Models;

public sealed class AppSettings
{
    public List<HotkeyBinding> Hotkeys   { get; set; } = new();
    public bool                AutoStart { get; set; } = false;
}

using System.Collections.Generic;
using HyperIMSwitch.Core.Models;
using HyperIMSwitch.Tray;
using Microsoft.UI.Dispatching;

namespace HyperIMSwitch.Core.Services;

public sealed class HotkeyService
{
    private readonly ImeSwitchService _switcher;
    private readonly DispatcherQueue  _uiDispatcher;
    private HotkeyMessageLoop?        _loop;

    public HotkeyService(ImeSwitchService switcher, SettingsService settings,
        DispatcherQueue uiDispatcher)
    {
        _switcher     = switcher;
        _uiDispatcher = uiDispatcher;
    }

    public void Start()
    {
        _loop = new HotkeyMessageLoop(_switcher, _uiDispatcher);
        _loop.Start();
    }

    public void ApplyBindings(IEnumerable<HotkeyBinding> bindings)
    {
        _loop?.ApplyBindings(bindings);
    }

    public void Suspend() => _loop?.Suspend();
    public void Resume()  => _loop?.Resume();

    public void Stop()
    {
        _loop?.Stop();
    }
}

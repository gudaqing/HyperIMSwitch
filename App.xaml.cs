using System;
using System.Runtime.InteropServices;
using HyperIMSwitch.Core.Infrastructure;
using HyperIMSwitch.Core.Services;
using HyperIMSwitch.Tray;
using HyperIMSwitch.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace HyperIMSwitch;

public partial class App : Application
{
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();
    [DllImport("kernel32.dll")] private static extern bool SetConsoleOutputCP(uint wCodePageID);

    private SingleInstanceGuard? _guard;
    private SettingsService?      _settingsService;
    private TsfProfileEnumerator? _enumerator;
    private ImeSwitchService?     _switcher;
    private HotkeyService?        _hotkeyService;
    private TrayIconManager?      _trayManager;
    private AutoStartService?     _autoStartService;
    private MainWindow?           _mainWindow;

    public new static App Current => (App)Application.Current;
    public DispatcherQueue UIDispatcher { get; private set; } = null!;

    public ImeSwitchService?     Switcher      => _switcher;
    public SettingsService?      Settings      => _settingsService;
    public TsfProfileEnumerator? Enumerator    => _enumerator;
    public AutoStartService?     AutoStart     => _autoStartService;
    public HotkeyService?        HotkeyService => _hotkeyService;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AllocConsole();
        SetConsoleOutputCP(65001); // UTF-8，否则中文乱码
        // .NET WinExe 的 stdout 句柄无效，必须重定向到 CONOUT$
        var conOut = new System.IO.StreamWriter("CONOUT$", append: false,
            encoding: new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            { AutoFlush = true };
        Console.SetOut(conOut);
        Console.SetError(conOut);
        Console.WriteLine("[App] OnLaunched");

        // Catch all unhandled exceptions so crashes aren't silent
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            ShowFatalError(e.Exception);
        };

        try
        {
            _guard = new SingleInstanceGuard("HyperIMSwitch");
            if (!_guard.IsFirstInstance)
            {
                _guard.Dispose();
                Exit();
                return;
            }

            UIDispatcher = DispatcherQueue.GetForCurrentThread();

            _settingsService = new SettingsService();
            _settingsService.Load();
            // 修复旧格式 settings.json 中 SlotId=0 的情况
            FixSlotIds(_settingsService.Settings);

            _enumerator = new TsfProfileEnumerator();
            _enumerator.Enumerate();
            BackfillBindingRuntimeFields(_settingsService.Settings, _enumerator);

            _switcher = new ImeSwitchService();
            _switcher.UpdateBindings(_settingsService.Settings.Hotkeys);
            _hotkeyService = new HotkeyService(_switcher, _settingsService, UIDispatcher);
            _hotkeyService.Start();
            _hotkeyService.ApplyBindings(_settingsService.Settings.Hotkeys);

            _autoStartService = new AutoStartService();

            // Must Activate() to keep WinUI 3 message loop alive.
            // Immediately hide the window afterwards — tray icon is the UI entry point.
            _mainWindow = new MainWindow();
            _mainWindow.Activate();
            _mainWindow.HideImmediately();

            _trayManager = new TrayIconManager(_mainWindow.GetWindowHandle(), UIDispatcher);
            _trayManager.Initialize();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[App] Fatal startup error: {ex}");
            ShowFatalError(ex);
            Exit();
        }
    }

    public void ShowSettings()
    {
        _mainWindow?.ShowWindow();
    }

    public void OnExit()
    {
        _trayManager?.Dispose();
        _hotkeyService?.Stop();
        _guard?.Dispose();
        Exit();
    }

    private static void FixSlotIds(Core.Models.AppSettings settings)
    {
        bool needsFix = settings.Hotkeys.Exists(b => b.SlotId <= 0);
        if (!needsFix) return;
        int slot = 1;
        foreach (var b in settings.Hotkeys)
            b.SlotId = slot++;
    }

    private static void BackfillBindingRuntimeFields(
        Core.Models.AppSettings settings, TsfProfileEnumerator enumerator)
    {
        foreach (var b in settings.Hotkeys)
        {
            if (b.ProfileType != Interop.TsfConstants.TF_PROFILETYPE_KEYBOARDLAYOUT || b.Hkl != 0)
                continue;

            foreach (var p in enumerator.Profiles)
            {
                if (p.ProfileType != b.ProfileType) continue;
                if (p.LangId != b.LangId) continue;
                b.Hkl = p.Hkl.ToInt64();
                break;
            }
        }
    }

    private static void ShowFatalError(Exception ex)
    {
        // Write to a log file next to the exe so we can diagnose startup crashes
        try
        {
            string logPath = System.IO.Path.Combine(
                AppContext.BaseDirectory, "crash.log");
            System.IO.File.WriteAllText(logPath,
                $"{DateTime.Now:u}\n{ex}\n");
        }
        catch { /* last resort */ }
    }
}

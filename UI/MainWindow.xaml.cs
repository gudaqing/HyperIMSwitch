using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace HyperIMSwitch.UI;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;

    public MainWindow()
    {
        InitializeComponent();

        var hwnd   = WindowNative.GetWindowHandle(this);
        var winId  = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(winId);

        // Intercept close → hide instead of destroying
        _appWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            HideAndResumeHotkeys();
        };

        // If the settings window loses focus (e.g. minimized / switched away),
        // resume global hotkeys so the app doesn't stay suspended indefinitely.
        Activated += (_, e) =>
        {
            if (!_appWindow.IsVisible) return;
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                App.Current.HotkeyService?.Resume();
            else
                App.Current.HotkeyService?.Suspend();
        };

        RootFrame.Navigate(typeof(SettingsPage));
    }

    public IntPtr GetWindowHandle() =>
        WindowNative.GetWindowHandle(this);

    public void HideImmediately() => HideAndResumeHotkeys();

    public void ShowWindow()
    {
        // 設定ウィンドウが開いている間はホットキーを停止
        // （そうしないとホットキー録制コントロールにキーが届かない）
        App.Current.HotkeyService?.Suspend();
        _appWindow.Show();
        _appWindow.MoveAndResize(new RectInt32(100, 100, 640, 520));
    }

    private void HideAndResumeHotkeys()
    {
        _appWindow.Hide();
        App.Current.HotkeyService?.Resume();
    }
}

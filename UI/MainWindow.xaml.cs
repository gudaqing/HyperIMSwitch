using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.Graphics;
using WinRT.Interop;

namespace HyperIMSwitch.UI;

public sealed partial class MainWindow : Window
{
    private const int DefaultWindowWidth  = 1200;
    private const int DefaultWindowHeight = 860;
    private const int MinWindowWidth      = 1000;
    private const int MinWindowHeight     = 760;

    private readonly AppWindow _appWindow;
    private bool _enforcingMinSize;
    private bool _placedOnce;

    public MainWindow()
    {
        InitializeComponent();

        var hwnd   = WindowNative.GetWindowHandle(this);
        var winId  = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(winId);
        ConfigureWindowChrome();
        SetCustomTitleBarEnabled(true);
        _appWindow.Changed += (_, _) =>
        {
            EnforceMinimumSize();
            UpdateTitleBarInsets();
        };

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
        try
        {
            // 設定ウィンドウが開いている間はホットキーを停止
            // （そうしないとホットキー録制コントロールにキーが届かない）
            App.Current.HotkeyService?.Suspend();

            if (!_appWindow.IsVisible)
                _appWindow.Show();
            Activate();

            // First open: apply default size and centered position.
            // Subsequent restores should not force move/resize.
            if (!_placedOnce)
            {
                var centered = GetCenteredRect(DefaultWindowWidth, DefaultWindowHeight);
                _appWindow.MoveAndResize(centered);
                _placedOnce = true;
            }

            EnforceMinimumSize();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Window] ShowWindow failed: {ex}");
        }
    }

    private void HideAndResumeHotkeys()
    {
        _appWindow.Hide();
        App.Current.HotkeyService?.Resume();
    }

    private void ConfigureWindowChrome()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, true);
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }
    }

    // WinUI Gallery style:
    // custom title bar: ExtendsContentIntoTitleBar=true + SetTitleBar(element)
    // system title bar: ExtendsContentIntoTitleBar=false + SetTitleBar(null)
    private void SetCustomTitleBarEnabled(bool enabled)
    {
        if (enabled)
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            }
            UpdateTitleBarInsets();
        }
        else
        {
            ExtendsContentIntoTitleBar = false;
            SetTitleBar(null);
        }
    }

    private void EnforceMinimumSize()
    {
        if (!_appWindow.IsVisible) return;
        if (_appWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            return;
        }

        if (_enforcingMinSize) return;

        var size = _appWindow.Size;
        int targetWidth = Math.Max(size.Width, MinWindowWidth);
        int targetHeight = Math.Max(size.Height, MinWindowHeight);
        if (targetWidth == size.Width && targetHeight == size.Height) return;

        _enforcingMinSize = true;
        try
        {
            _appWindow.Resize(new SizeInt32(targetWidth, targetHeight));
        }
        finally
        {
            _enforcingMinSize = false;
        }
    }

    private RectInt32 GetCenteredRect(int width, int height)
    {
        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        int x = work.X + Math.Max(0, (work.Width - width) / 2);
        int y = work.Y + Math.Max(0, (work.Height - height) / 2);
        return new RectInt32(x, y, width, height);
    }

    private void UpdateTitleBarInsets()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        double left = _appWindow.TitleBar.LeftInset;
        double right = _appWindow.TitleBar.RightInset;

        // Some lifecycle moments can report invalid inset values.
        if (double.IsNaN(left) || double.IsInfinity(left) || left < 0) left = 0;
        if (double.IsNaN(right) || double.IsInfinity(right) || right < 0) right = 0;

        try
        {
            LeftInsetColumn.Width = new GridLength(left);
            RightInsetColumn.Width = new GridLength(right);
        }
        catch (ArgumentException)
        {
            LeftInsetColumn.Width = new GridLength(0);
            RightInsetColumn.Width = new GridLength(0);
        }
    }
}

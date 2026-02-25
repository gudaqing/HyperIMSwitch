using System;
using System.Runtime.InteropServices;
using System.Threading;
using HyperIMSwitch.Interop;
using Microsoft.UI.Dispatching;

namespace HyperIMSwitch.Tray;

/// <summary>
/// Manages the Windows system tray icon using Shell_NotifyIcon.
/// Runs its own STA Win32 message loop on a background thread so
/// tray messages are received independently of the WinUI thread.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private const uint TRAY_CALLBACK  = 0x0401; // WM_USER + 1
    private const uint WM_COMMAND     = 0x0111;
    private const uint WM_QUIT        = 0x0012;
    private const string WndClassName = "HyperIMSwitch_Tray";

    private readonly IntPtr          _ownerHwnd;
    private readonly DispatcherQueue _uiDispatcher;
    private Thread?  _thread;
    private IntPtr   _trayHwnd;
    private uint     _taskbarCreatedMsg;
    private bool     _disposed;
    private readonly ManualResetEventSlim _ready = new(false);

    // Keep delegate alive to prevent GC
    private WndProcDelegate? _wndProcDelegate;
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public TrayIconManager(IntPtr ownerHwnd, DispatcherQueue uiDispatcher)
    {
        _ownerHwnd    = ownerHwnd;
        _uiDispatcher = uiDispatcher;
    }

    public void Initialize()
    {
        _thread = new Thread(TrayThreadProc)
        {
            IsBackground = true,
            Name = "TrayIconThread"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RemoveTrayIcon();
    }

    // ---- Win32 P/Invoke ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint    cbSize;
        public uint    style;
        public IntPtr  lpfnWndProc;
        public int     cbClsExtra;
        public int     cbWndExtra;
        public IntPtr  hInstance;
        public IntPtr  hIcon;
        public IntPtr  hCursor;
        public IntPtr  hbrBackground;
        public string? lpszMenuName;
        public string  lpszClassName;
        public IntPtr  hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint   message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint   time;
        public int    ptX, ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private void TrayThreadProc()
    {
        _taskbarCreatedMsg = TrayIconNative.RegisterWindowMessageW("TaskbarCreated");

        _wndProcDelegate = WndProc;
        var wc = new WNDCLASSEXW
        {
            cbSize        = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            lpszClassName = WndClassName
        };
        RegisterClassExW(ref wc);

        _trayHwnd = CreateWindowExW(0, WndClassName, null, 0,
            0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        AddTrayIcon();
        _ready.Set();

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == TRAY_CALLBACK)
        {
            uint notifyMsg = (uint)(lParam.ToInt64() & 0xFFFF);

            if (notifyMsg == TrayIconNative.WM_LBUTTONDBLCLK)
            {
                _uiDispatcher.TryEnqueue(() => App.Current.ShowSettings());
            }
            else if (notifyMsg == TrayIconNative.WM_RBUTTONUP ||
                     notifyMsg == TrayIconNative.WM_CONTEXTMENU)
            {
                ShowContextMenu(hWnd);
            }
            return IntPtr.Zero;
        }

        if (msg == _taskbarCreatedMsg)
        {
            // Explorer restarted — re-add the tray icon
            AddTrayIcon();
            return IntPtr.Zero;
        }

        if (msg == WM_COMMAND)
        {
            HandleMenuCommand((int)(wParam.ToInt64() & 0xFFFF));
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu(IntPtr hWnd)
    {
        IntPtr hMenu = TrayIconNative.CreatePopupMenu();

        TrayIconNative.AppendMenuW(hMenu, TrayIconNative.MF_STRING,
            new IntPtr(1001), "设置 / Settings");
        TrayIconNative.AppendMenuW(hMenu, TrayIconNative.MF_SEPARATOR,
            IntPtr.Zero, string.Empty);

        bool autoStart = App.Current.AutoStart?.IsEnabled ?? false;
        uint autoFlags = TrayIconNative.MF_STRING | (autoStart ? TrayIconNative.MF_CHECKED : 0);
        TrayIconNative.AppendMenuW(hMenu, autoFlags,
            new IntPtr(1002), "开机自启 / AutoStart");

        TrayIconNative.AppendMenuW(hMenu, TrayIconNative.MF_SEPARATOR,
            IntPtr.Zero, string.Empty);
        TrayIconNative.AppendMenuW(hMenu, TrayIconNative.MF_STRING,
            new IntPtr(1004), "自动诊断 / Run Diagnostics");

        TrayIconNative.AppendMenuW(hMenu, TrayIconNative.MF_SEPARATOR,
            IntPtr.Zero, string.Empty);
        TrayIconNative.AppendMenuW(hMenu, TrayIconNative.MF_STRING,
            new IntPtr(1003), "退出 / Exit");

        TrayIconNative.SetForegroundWindow(hWnd);
        GetCursorPos(out var pt);
        TrayIconNative.TrackPopupMenu(hMenu,
            TrayIconNative.TPM_RIGHTBUTTON | TrayIconNative.TPM_BOTTOMALIGN,
            pt.x, pt.y, 0, hWnd, IntPtr.Zero);
        TrayIconNative.DestroyMenu(hMenu);
    }

    private void HandleMenuCommand(int cmd)
    {
        switch (cmd)
        {
            case 1001:
                _uiDispatcher.TryEnqueue(() => App.Current.ShowSettings());
                break;
            case 1002:
                _uiDispatcher.TryEnqueue(() =>
                {
                    var svc = App.Current.AutoStart;
                    if (svc != null) svc.SetEnabled(!svc.IsEnabled);
                });
                break;
            case 1003:
                _uiDispatcher.TryEnqueue(() => App.Current.OnExit());
                break;
            case 1004:
                _uiDispatcher.TryEnqueue(() => App.Current.RunAutoDiagnostics());
                break;
        }
    }

    private void AddTrayIcon()
    {
        var nid = new TrayIconNative.NOTIFYICONDATA
        {
            cbSize           = (uint)Marshal.SizeOf<TrayIconNative.NOTIFYICONDATA>(),
            hWnd             = _trayHwnd,
            uID              = 1,
            uFlags           = TrayIconNative.NIF_ICON | TrayIconNative.NIF_MESSAGE | TrayIconNative.NIF_TIP,
            uCallbackMessage = TRAY_CALLBACK,
            hIcon            = TrayIconNative.LoadIconW(IntPtr.Zero, new IntPtr(TrayIconNative.IDI_APPLICATION)),
            szTip            = "HyperIMSwitch",
            szInfo           = string.Empty,
            szInfoTitle      = string.Empty,
        };
        TrayIconNative.Shell_NotifyIconW(TrayIconNative.NIM_ADD, ref nid);

        nid.uVersion = TrayIconNative.NOTIFYICON_VERSION_4;
        TrayIconNative.Shell_NotifyIconW(TrayIconNative.NIM_SETVERSION, ref nid);
    }

    private void RemoveTrayIcon()
    {
        if (_trayHwnd == IntPtr.Zero) return;
        var nid = new TrayIconNative.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<TrayIconNative.NOTIFYICONDATA>(),
            hWnd   = _trayHwnd,
            uID    = 1,
            szTip        = string.Empty,
            szInfo       = string.Empty,
            szInfoTitle  = string.Empty,
        };
        TrayIconNative.Shell_NotifyIconW(TrayIconNative.NIM_DELETE, ref nid);
        PostMessageW(_trayHwnd, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}

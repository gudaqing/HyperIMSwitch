using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using HyperIMSwitch.Core.Models;
using HyperIMSwitch.Core.Services;
using HyperIMSwitch.Interop;
using Microsoft.UI.Dispatching;

namespace HyperIMSwitch.Tray;

/// <summary>
/// Runs a Win32 message loop on a dedicated background MTA thread.
/// Uses thread-based hotkeys (NULL hWnd) so no HWND is required.
/// WM_HOTKEY → DispatcherQueue.TryEnqueue → STA ImeSwitchService.
/// </summary>
public sealed class HotkeyMessageLoop
{
    private const uint WM_USER_APPLY_BINDINGS = HotkeyNative.WM_USER + 10;
    private const uint WM_USER_SUSPEND        = HotkeyNative.WM_USER + 11;
    private const uint WM_USER_RESUME         = HotkeyNative.WM_USER + 12;

    private readonly ImeSwitchService _switcher;
    private readonly DispatcherQueue  _uiDispatcher;
    private Thread?   _thread;
    private uint      _threadId;
    private readonly ManualResetEventSlim _ready = new(false);

    private readonly object _bindLock = new();
    private List<HotkeyBinding> _pendingBindings = new();
    private bool _bindingsDirty;

    // Tracks which slot IDs currently have registered hotkeys on the loop thread
    private readonly HashSet<int> _registeredSlotIds = new();
    private bool _suspended;

    public HotkeyMessageLoop(ImeSwitchService switcher, DispatcherQueue uiDispatcher)
    {
        _switcher     = switcher;
        _uiDispatcher = uiDispatcher;
    }

    public void Start()
    {
        _thread = new Thread(MessageLoopProc)
        {
            IsBackground = true,
            Name = "HotkeyMessageLoop"
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _ready.Wait();
    }

    public void ApplyBindings(IEnumerable<HotkeyBinding> bindings)
    {
        lock (_bindLock)
        {
            _pendingBindings = new List<HotkeyBinding>(bindings);
            _bindingsDirty   = true;
        }
        if (_threadId != 0)
            HotkeyNative.PostThreadMessageW(_threadId, WM_USER_APPLY_BINDINGS, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Unregisters all hotkeys while the settings window is open.</summary>
    public void Suspend()
    {
        if (_threadId != 0)
            HotkeyNative.PostThreadMessageW(_threadId, WM_USER_SUSPEND, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Re-registers hotkeys after the settings window is closed/hidden.</summary>
    public void Resume()
    {
        if (_threadId != 0)
            HotkeyNative.PostThreadMessageW(_threadId, WM_USER_RESUME, IntPtr.Zero, IntPtr.Zero);
    }

    public void Stop()
    {
        if (_threadId != 0)
            HotkeyNative.PostThreadMessageW(_threadId, HotkeyNative.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(2000);
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

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

    private void MessageLoopProc()
    {
        _threadId = GetCurrentThreadId();
        Console.WriteLine($"[Hotkey] Message loop thread started, threadId={_threadId}");
        _ready.Set();

        ApplyPendingBindings();

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == HotkeyNative.WM_HOTKEY)
            {
                int id = (int)msg.wParam;
                Console.WriteLine($"[Hotkey] WM_HOTKEY received  id={id}  lParam=0x{msg.lParam:X}");
                _uiDispatcher.TryEnqueue(() => _switcher.SwitchById(id));
            }
            else if (msg.message == WM_USER_APPLY_BINDINGS)
            {
                Console.WriteLine("[Hotkey] WM_USER_APPLY_BINDINGS received → ApplyPendingBindings");
                ApplyPendingBindings();
            }
            else if (msg.message == WM_USER_SUSPEND)
            {
                Console.WriteLine("[Hotkey] Suspend — unregistering all hotkeys");
                _suspended = true;
                foreach (int id in _registeredSlotIds)
                    HotkeyNative.UnregisterHotKey(IntPtr.Zero, id);
                _registeredSlotIds.Clear();
            }
            else if (msg.message == WM_USER_RESUME)
            {
                Console.WriteLine("[Hotkey] Resume — re-registering hotkeys");
                _suspended = false;
                ApplyPendingBindings(force: true);
            }
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        // Cleanup: unregister all currently registered hotkeys
        foreach (int slotId in _registeredSlotIds)
            HotkeyNative.UnregisterHotKey(IntPtr.Zero, slotId);
        _registeredSlotIds.Clear();
    }

    private void ApplyPendingBindings(bool force = false)
    {
        // サスペンド中は登録しない（force=true はResume時の強制再登録）
        if (_suspended && !force) return;

        List<HotkeyBinding> bindings;
        lock (_bindLock)
        {
            if (!_bindingsDirty && !force) return;
            bindings       = _pendingBindings;
            _bindingsDirty = false;
        }

        Console.WriteLine($"[Hotkey] ApplyPendingBindings  count={bindings.Count}");

        // Unregister all currently registered hotkeys
        foreach (int slotId in _registeredSlotIds)
            HotkeyNative.UnregisterHotKey(IntPtr.Zero, slotId);
        _registeredSlotIds.Clear();

        foreach (var b in bindings)
        {
            if (b.IsValid)
            {
                bool ok = HotkeyNative.RegisterHotKey(IntPtr.Zero, b.SlotId,
                    b.Modifiers | HotkeyNative.MOD_NOREPEAT, b.VirtualKey);
                Console.WriteLine($"[Hotkey] RegisterHotKey  slot={b.SlotId}  name=\"{b.DisplayName}\"  mod=0x{b.Modifiers:X}  vk=0x{b.VirtualKey:X}  ok={ok}  err={Marshal.GetLastWin32Error()}");
                if (ok) _registeredSlotIds.Add(b.SlotId);
            }
            else
            {
                Console.WriteLine($"[Hotkey] Skipped invalid binding  slot={b.SlotId}  name=\"{b.DisplayName}\"");
            }
        }
    }
}

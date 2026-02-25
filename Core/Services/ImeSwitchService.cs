using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using HyperIMSwitch.Core.Models;
using HyperIMSwitch.Interop;

namespace HyperIMSwitch.Core.Services;

public sealed class ImeSwitchService
{
    private Dictionary<int, HotkeyBinding> _bindingsById = new();
    private readonly BlockingCollection<Action> _queue = new();
    private readonly SwitchDiagnosticsOptions _diag;
    private static long _switchSequence;

    public ImeSwitchService(SwitchDiagnosticsOptions? diagnostics = null)
    {
        _diag = diagnostics ?? new SwitchDiagnosticsOptions();
        var t = new Thread(StaWorker)
        {
            IsBackground = true,
            Name = "ImeSwitchSTA"
        };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    private void StaWorker()
    {
        Console.WriteLine("[Switch] STA worker thread started");
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try { action(); }
            catch (Exception ex)
            {
                Console.WriteLine($"[Switch] STA worker exception: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public void UpdateBindings(IEnumerable<HotkeyBinding> bindings)
    {
        _bindingsById = new Dictionary<int, HotkeyBinding>();
        foreach (var b in bindings)
            _bindingsById[b.SlotId] = b;
    }

    public void SwitchById(int slotId)
    {
        if (_bindingsById.TryGetValue(slotId, out var b))
        {
            long switchId = Interlocked.Increment(ref _switchSequence);
            _queue.Add(() => ActivateProfileOnSta(b, switchId, _diag));
        }
        else
        {
            Console.WriteLine($"[Switch] SwitchById slotId={slotId} NOT FOUND");
        }
    }

    public bool SwitchByIdSync(int slotId, int timeoutMs = 1500)
    {
        if (!_bindingsById.TryGetValue(slotId, out var b))
            return false;

        long switchId = Interlocked.Increment(ref _switchSequence);
        using var done = new ManualResetEventSlim(false);
        _queue.Add(() =>
        {
            try { ActivateProfileOnSta(b, switchId, _diag); }
            finally { done.Set(); }
        });
        return done.Wait(timeoutMs);
    }

    public ushort? GetCurrentLanguageSync(int timeoutMs = 1000)
    {
        ushort? result = null;
        using var done = new ManualResetEventSlim(false);
        _queue.Add(() =>
        {
            object? raw = null;
            try
            {
                raw = Activator.CreateInstance(
                    Type.GetTypeFromCLSID(TsfConstants.CLSID_TF_InputProcessorProfiles)!)!;
                var profiles = (ITfInputProcessorProfiles)raw;
                int hr = profiles.GetCurrentLanguage(out ushort langId);
                if (hr == 0) result = langId;
            }
            catch
            {
                result = null;
            }
            finally
            {
                if (raw != null) Marshal.ReleaseComObject(raw);
                done.Set();
            }
        });
        return done.Wait(timeoutMs) ? result : null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint Flags);
    private const uint KLF_SETFORPROCESS = 0x00000100;
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_IME_CONTROL = 0x0283;
    private const int IMC_GETCONVERSIONMODE = 0x0001;
    private const int IMC_SETCONVERSIONMODE = 0x0002;
    private const int IMC_SETSENTENCEMODE = 0x0004;
    private const int IMC_GETOPENSTATUS = 0x0005;
    private const int IMC_SETOPENSTATUS = 0x0006;

    private static void ActivateProfileOnSta(HotkeyBinding b, long switchId, SwitchDiagnosticsOptions diag)
    {
        var totalSw = Stopwatch.StartNew();
        string tag = $"[Switch#{switchId}]";
        void Log(string msg) => Console.WriteLine($"{tag} {msg}");

        Log($"ActivateProfile slot={b.SlotId} name=\"{b.DisplayName}\" lang=0x{b.LangId:X4} type={b.ProfileType}");
        Log($"Profile clsid={b.Clsid} guidProfile={b.GuidProfile}");

        object? raw = null;
        try
        {
            raw = Activator.CreateInstance(
                Type.GetTypeFromCLSID(TsfConstants.CLSID_TF_InputProcessorProfiles)!)!;
            var profiles = (ITfInputProcessorProfiles)raw;

            if (diag.LogCurrentLanguageState)
                LogCurrentLanguage(profiles, Log, "Before", diag.LogStepElapsedMs);

            if (b.ProfileType == TsfConstants.TF_PROFILETYPE_INPUTPROCESSOR)
            {
                ActivateInputProcessor(b, profiles, diag, Log);
            }
            else
            {
                ActivateKeyboardLayoutProfile(b, diag, Log);
            }

            if (diag.LogCurrentLanguageState)
                LogCurrentLanguage(profiles, Log, "After", diag.LogStepElapsedMs);
        }
        finally
        {
            if (raw != null) Marshal.ReleaseComObject(raw);
            Log($"Done elapsed={totalSw.ElapsedMilliseconds}ms");
        }

        if (b.ConversionMode.HasValue)
        {
            if (WaitForCurrentLanguage(TsfConstants.LANGID_JAPANESE, 300, Log))
                SetJapaneseConversionMode(b.ConversionMode.Value, Log);
            else
                Log("Skip SetConversionMode: current language is not 0x0411 after timeout");
        }
    }

    private static void ActivateInputProcessor(
        HotkeyBinding b,
        ITfInputProcessorProfiles profiles,
        SwitchDiagnosticsOptions diag,
        Action<string> log)
    {
        Guid clsid = b.Clsid;
        Guid guid = b.GuidProfile;

        int hr = CallHr(() => profiles.ActivateLanguageProfile(ref clsid, b.LangId, ref guid),
            "ActivateLanguageProfile", diag.LogStepElapsedMs, log);
        if (hr == 0 || !diag.EnableRetryChain) return;

        int hrEnabled = profiles.IsEnabledLanguageProfile(ref clsid, b.LangId, ref guid, out bool enabled);
        log($"IsEnabledLanguageProfile hr=0x{(uint)hrEnabled:X8} enabled={enabled}");

        if (diag.RetryEnableProfile && hrEnabled == 0 && !enabled)
        {
            CallHr(() => profiles.EnableLanguageProfile(ref clsid, b.LangId, ref guid, true),
                "EnableLanguageProfile(true)", diag.LogStepElapsedMs, log);
        }

        if (diag.RetryChangeCurrentLanguage)
        {
            CallHr(() => profiles.ChangeCurrentLanguage(b.LangId),
                "ChangeCurrentLanguage", diag.LogStepElapsedMs, log);
        }

        if (diag.RetrySetDefaultProfile)
        {
            CallHr(() => profiles.SetDefaultLanguageProfile(b.LangId, ref clsid, ref guid),
                "SetDefaultLanguageProfile", diag.LogStepElapsedMs, log);
        }

        if (diag.RetryForegroundLangRequest)
        {
            IntPtr langHkl = new(((long)b.LangId << 16) | b.LangId);
            RequestForegroundLanguageSwitch(langHkl, log, diag);
        }

        CallHr(() => profiles.ActivateLanguageProfile(ref clsid, b.LangId, ref guid),
            "ActivateLanguageProfile(Retry)", diag.LogStepElapsedMs, log);
    }

    private static void ActivateKeyboardLayoutProfile(
        HotkeyBinding b,
        SwitchDiagnosticsOptions diag,
        Action<string> log)
    {
        long hklValue = b.Hkl != 0 ? b.Hkl : ((long)b.LangId << 16) | b.LangId;
        IntPtr hkl = new(hklValue);

        RequestForegroundLanguageSwitch(hkl, log, diag);

        var sw = Stopwatch.StartNew();
        var result = ActivateKeyboardLayout(hkl, KLF_SETFORPROCESS);
        if (diag.LogStepElapsedMs)
            log($"ActivateKeyboardLayout(Fallback) hkl=0x{hkl:X} result=0x{result:X} err={Marshal.GetLastWin32Error()} elapsed={sw.ElapsedMilliseconds}ms");
        else
            log($"ActivateKeyboardLayout(Fallback) hkl=0x{hkl:X} result=0x{result:X} err={Marshal.GetLastWin32Error()}");
    }

    private static int CallHr(Func<int> call, string name, bool logElapsed, Action<string> log)
    {
        var sw = Stopwatch.StartNew();
        int hr = call();
        if (logElapsed)
            log($"{name} hr=0x{(uint)hr:X8} elapsed={sw.ElapsedMilliseconds}ms");
        else
            log($"{name} hr=0x{(uint)hr:X8}");
        return hr;
    }

    private static void LogCurrentLanguage(
        ITfInputProcessorProfiles profiles,
        Action<string> log,
        string phase,
        bool logElapsed)
    {
        var sw = Stopwatch.StartNew();
        int hr = profiles.GetCurrentLanguage(out ushort langId);
        if (logElapsed)
            log($"{phase} GetCurrentLanguage hr=0x{(uint)hr:X8} lang=0x{langId:X4} elapsed={sw.ElapsedMilliseconds}ms");
        else
            log($"{phase} GetCurrentLanguage hr=0x{(uint)hr:X8} lang=0x{langId:X4}");
    }

    private static void SetJapaneseConversionMode(int mode, Action<string> log)
    {
        object? threadMgrRaw = null;
        try
        {
            threadMgrRaw = Activator.CreateInstance(
                Type.GetTypeFromCLSID(TsfConstants.CLSID_TF_ThreadMgr)!)!;

            var threadMgr = (ITfThreadMgr)threadMgrRaw;
            if (threadMgr.Activate(out uint clientId) != 0) return;
            if (threadMgr.GetGlobalCompartment(out var compMgr) != 0 || compMgr == null) return;

            var convGuid = TsfConstants.GUID_COMPARTMENT_KEYBOARD_INPUTMODE_CONVERSION;
            if (compMgr.GetCompartment(ref convGuid, out var compartment) != 0 || compartment == null) return;

            object value = mode;
            compartment.SetValue(clientId, ref value);
            log($"SetConversionMode mode=0x{mode:X2}");

            threadMgr.Deactivate();
            Marshal.ReleaseComObject(compartment);
            Marshal.ReleaseComObject(compMgr);
        }
        catch (Exception ex)
        {
            log($"SetJapaneseConversionMode ex={ex.Message}");
        }
        finally
        {
            if (threadMgrRaw != null) Marshal.ReleaseComObject(threadMgrRaw);
        }

        // Also force mode on the foreground window's IME context.
        // TSF global compartment alone is not always enough for some apps.
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            IntPtr imeWnd = ImmGetDefaultIMEWnd(hwnd);
            if (imeWnd == IntPtr.Zero) return;

            var openRet = SendMessageW(imeWnd, WM_IME_CONTROL,
                new IntPtr(IMC_SETOPENSTATUS), new IntPtr(1));
            var convRet = SendMessageW(imeWnd, WM_IME_CONTROL,
                new IntPtr(IMC_SETCONVERSIONMODE), new IntPtr(mode));
            // 0 means no special sentence mode preference.
            var sentRet = SendMessageW(imeWnd, WM_IME_CONTROL,
                new IntPtr(IMC_SETSENTENCEMODE), IntPtr.Zero);

            log($"IME_CONTROL open={openRet} conv={convRet} sent={sentRet} mode=0x{mode:X2}");

            // Some apps/controls overwrite IME state shortly after focus/language switch.
            // Re-check with staged delays and reapply when needed.
            int[] checkDelaysMs = { 80, 200 };
            for (int i = 0; i < checkDelaysMs.Length; i++)
            {
                Thread.Sleep(checkDelaysMs[i]);
                long openNow = SendMessageW(imeWnd, WM_IME_CONTROL,
                    new IntPtr(IMC_GETOPENSTATUS), IntPtr.Zero).ToInt64();
                long convNow = SendMessageW(imeWnd, WM_IME_CONTROL,
                    new IntPtr(IMC_GETCONVERSIONMODE), IntPtr.Zero).ToInt64();
                bool needRetry = openNow == 0 || (convNow & mode) != mode;
                log($"IME_CONTROL_CHECK[{i}] delay={checkDelaysMs[i]} open={openNow} conv=0x{convNow:X} needRetry={needRetry}");

                if (!needRetry) break;

                SendMessageW(imeWnd, WM_IME_CONTROL,
                    new IntPtr(IMC_SETOPENSTATUS), new IntPtr(1));
                SendMessageW(imeWnd, WM_IME_CONTROL,
                    new IntPtr(IMC_SETCONVERSIONMODE), new IntPtr(mode));
                log($"IME_CONTROL_REAPPLY[{i}] mode=0x{mode:X2}");
            }
        }
        catch (Exception ex)
        {
            log($"IME_CONTROL ex={ex.Message}");
        }
    }

    private static bool WaitForCurrentLanguage(ushort targetLang, int timeoutMs, Action<string> log)
    {
        var sw = Stopwatch.StartNew();
        object? raw = null;
        try
        {
            raw = Activator.CreateInstance(
                Type.GetTypeFromCLSID(TsfConstants.CLSID_TF_InputProcessorProfiles)!)!;
            var profiles = (ITfInputProcessorProfiles)raw;

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                int hr = profiles.GetCurrentLanguage(out ushort langId);
                if (hr == 0 && langId == targetLang)
                {
                    log($"WaitForLang success target=0x{targetLang:X4} elapsed={sw.ElapsedMilliseconds}ms");
                    return true;
                }
                Thread.Sleep(20);
            }

            int lastHr = profiles.GetCurrentLanguage(out ushort lastLang);
            log($"WaitForLang timeout target=0x{targetLang:X4} lastHr=0x{(uint)lastHr:X8} lastLang=0x{lastLang:X4} elapsed={sw.ElapsedMilliseconds}ms");
            return false;
        }
        catch (Exception ex)
        {
            log($"WaitForLang ex={ex.Message}");
            return false;
        }
        finally
        {
            if (raw != null) Marshal.ReleaseComObject(raw);
        }
    }

    private static void RequestForegroundLanguageSwitch(
        IntPtr hkl,
        Action<string> log,
        SwitchDiagnosticsOptions diag)
    {
        IntPtr hwnd = GetForegroundWindow();
        uint threadId = 0;
        uint processId = 0;
        string processName = "unknown";
        if (hwnd != IntPtr.Zero)
        {
            threadId = GetWindowThreadProcessId(hwnd, out processId);
            if (diag.LogForegroundWindowContext && processId != 0)
            {
                try { processName = Process.GetProcessById((int)processId).ProcessName; }
                catch { processName = "unavailable"; }
            }
        }

        var sw = Stopwatch.StartNew();
        bool posted = hwnd != IntPtr.Zero &&
                      PostMessageW(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
        if (diag.LogForegroundWindowContext)
        {
            if (diag.LogStepElapsedMs)
                log($"WM_INPUTLANGCHANGEREQUEST hwnd=0x{hwnd:X} tid={threadId} pid={processId} proc={processName} hkl=0x{hkl:X} ok={posted} err={Marshal.GetLastWin32Error()} elapsed={sw.ElapsedMilliseconds}ms");
            else
                log($"WM_INPUTLANGCHANGEREQUEST hwnd=0x{hwnd:X} tid={threadId} pid={processId} proc={processName} hkl=0x{hkl:X} ok={posted} err={Marshal.GetLastWin32Error()}");
        }
        else
        {
            if (diag.LogStepElapsedMs)
                log($"WM_INPUTLANGCHANGEREQUEST hwnd=0x{hwnd:X} hkl=0x{hkl:X} ok={posted} err={Marshal.GetLastWin32Error()} elapsed={sw.ElapsedMilliseconds}ms");
            else
                log($"WM_INPUTLANGCHANGEREQUEST hwnd=0x{hwnd:X} hkl=0x{hkl:X} ok={posted} err={Marshal.GetLastWin32Error()}");
        }
    }
}

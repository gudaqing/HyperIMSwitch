using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using HyperIMSwitch.Core.Models;
using HyperIMSwitch.Interop;

namespace HyperIMSwitch.Core.Services;

/// <summary>
/// Switches the active IME profile via TSF.
/// All TSF COM calls are executed on a dedicated regular-STA thread to avoid
/// WinUI 3 ASTA restrictions that block ITfInputProcessorProfileMgr::ActivateProfile.
/// </summary>
public sealed class ImeSwitchService
{
    private Dictionary<int, HotkeyBinding> _bindingsById = new();

    // Dedicated regular-STA worker thread — avoids ASTA COM restrictions
    private readonly BlockingCollection<Action> _queue = new();

    public ImeSwitchService()
    {
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
            _queue.Add(() => ActivateProfileOnSta(b));
        else
            Console.WriteLine($"[Switch] SwitchById  slotId={slotId}  NOT FOUND");
    }

    // ---- Win32 ----

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint Flags);
    private const uint KLF_SETFORPROCESS = 0x00000100;
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ---- TSF activation (runs on STA thread) ----

    private static void ActivateProfileOnSta(HotkeyBinding b)
    {
        Console.WriteLine($"[Switch] ActivateProfile  slot={b.SlotId}  name=\"{b.DisplayName}\"" +
                          $"  lang=0x{b.LangId:X4}  type={b.ProfileType}");

        Console.WriteLine($"[Switch]   clsid={b.Clsid}  guidProfile={b.GuidProfile}");

        object? raw = null;
        try
        {
            raw = Activator.CreateInstance(
                Type.GetTypeFromCLSID(TsfConstants.CLSID_TF_InputProcessorProfiles)!)!;

            var profiles = (ITfInputProcessorProfiles)raw;
            if (b.ProfileType == TsfConstants.TF_PROFILETYPE_INPUTPROCESSOR)
            {
                Guid clsid = b.Clsid;
                Guid guid  = b.GuidProfile;
                int hr = profiles.ActivateLanguageProfile(ref clsid, b.LangId, ref guid);
                Console.WriteLine($"[Switch] ActivateLanguageProfile  hr=0x{(uint)hr:X8}");

                if (hr != 0)
                {
                    int hrEnabled = profiles.IsEnabledLanguageProfile(ref clsid, b.LangId, ref guid, out bool enabled);
                    Console.WriteLine($"[Switch] IsEnabledLanguageProfile  hr=0x{(uint)hrEnabled:X8}  enabled={enabled}");

                    if (hrEnabled == 0 && !enabled)
                    {
                        int hrEnable = profiles.EnableLanguageProfile(ref clsid, b.LangId, ref guid, true);
                        Console.WriteLine($"[Switch] EnableLanguageProfile(true)  hr=0x{(uint)hrEnable:X8}");
                    }

                    int hrChangeLang = profiles.ChangeCurrentLanguage(b.LangId);
                    Console.WriteLine($"[Switch] ChangeCurrentLanguage  hr=0x{(uint)hrChangeLang:X8}");

                    int hrSetDefault = profiles.SetDefaultLanguageProfile(b.LangId, ref clsid, ref guid);
                    Console.WriteLine($"[Switch] SetDefaultLanguageProfile  hr=0x{(uint)hrSetDefault:X8}");

                    // Push language switch to the foreground window thread;
                    // this is required when current foreground app is already in 0409 layout.
                    RequestForegroundLanguageSwitch(new IntPtr(((long)b.LangId << 16) | b.LangId));

                    hr = profiles.ActivateLanguageProfile(ref clsid, b.LangId, ref guid);
                    Console.WriteLine($"[Switch] ActivateLanguageProfile(Retry)  hr=0x{(uint)hr:X8}");
                }
            }
            else
            {
                long hklValue = b.Hkl != 0 ? b.Hkl : ((long)b.LangId << 16) | b.LangId;
                IntPtr hkl = new(hklValue);

                RequestForegroundLanguageSwitch(hkl);

                // Fallback for windows that ignore WM_INPUTLANGCHANGEREQUEST.
                var result = ActivateKeyboardLayout(hkl, KLF_SETFORPROCESS);
                Console.WriteLine($"[Switch] ActivateKeyboardLayout(Fallback)  hkl=0x{hkl:X}  result=0x{result:X}  err={Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            if (raw != null) Marshal.ReleaseComObject(raw);
        }

        if (b.ConversionMode.HasValue)
            SetJapaneseConversionMode(b.ConversionMode.Value);
    }

    private static void SetJapaneseConversionMode(int mode)
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
            Console.WriteLine($"[Switch] SetConversionMode  mode=0x{mode:X2}");

            threadMgr.Deactivate();
            Marshal.ReleaseComObject(compartment);
            Marshal.ReleaseComObject(compMgr);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Switch] SetJapaneseConversionMode  ex={ex.Message}");
        }
        finally
        {
            if (threadMgrRaw != null) Marshal.ReleaseComObject(threadMgrRaw);
        }
    }

    private static void RequestForegroundLanguageSwitch(IntPtr hkl)
    {
        IntPtr hwnd = GetForegroundWindow();
        bool posted = hwnd != IntPtr.Zero &&
                      PostMessageW(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
        Console.WriteLine($"[Switch] WM_INPUTLANGCHANGEREQUEST  hwnd=0x{hwnd:X}  hkl=0x{hkl:X}  ok={posted}  err={Marshal.GetLastWin32Error()}");
    }
}

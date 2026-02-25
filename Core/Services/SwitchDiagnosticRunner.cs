using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HyperIMSwitch.Core.Models;
using HyperIMSwitch.Interop;

namespace HyperIMSwitch.Core.Services;

public sealed class SwitchDiagnosticRunner
{
    private const int ForegroundSwitchDelayMs = 5000;
    private readonly ImeSwitchService _switcher;
    private readonly SettingsService _settings;
    private int _running;

    public SwitchDiagnosticRunner(ImeSwitchService switcher, SettingsService settings)
    {
        _switcher = switcher;
        _settings = settings;
    }

    public void RunAllScenariosAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            Console.WriteLine("[Diag] A diagnostic run is already in progress.");
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { RunAllScenariosCore(); }
            finally { Interlocked.Exchange(ref _running, 0); }
        });
    }

    private void RunAllScenariosCore()
    {
        var bindings = _settings.Settings.Hotkeys;
        var en = bindings.FirstOrDefault(b =>
            b.ProfileType == TsfConstants.TF_PROFILETYPE_KEYBOARDLAYOUT &&
            b.LangId == TsfConstants.LANGID_ENGLISH_US);
        var jp = bindings.FirstOrDefault(b =>
            b.ProfileType == TsfConstants.TF_PROFILETYPE_INPUTPROCESSOR &&
            b.LangId == TsfConstants.LANGID_JAPANESE);
        var zh = bindings.FirstOrDefault(b =>
            b.ProfileType == TsfConstants.TF_PROFILETYPE_INPUTPROCESSOR &&
            (b.DisplayName.Contains("微信", StringComparison.OrdinalIgnoreCase) ||
             b.DisplayName.Contains("WeChat", StringComparison.OrdinalIgnoreCase)))
            ?? bindings.FirstOrDefault(b =>
                b.ProfileType == TsfConstants.TF_PROFILETYPE_INPUTPROCESSOR &&
                b.LangId == TsfConstants.LANGID_CHINESE_SIMPLIFIED);

        if (en == null || jp == null || zh == null)
        {
            Console.WriteLine("[Diag] Missing required bindings. Need 0409 keyboard, 0411 IME, 0804 IME.");
            return;
        }

        var d = _settings.Settings.SwitchDiagnostics;
        d.EnableRetryChain = true;
        d.RetryEnableProfile = false;

        bool originalRetryChange = d.RetryChangeCurrentLanguage;
        bool originalRetryDefault = d.RetrySetDefaultProfile;
        bool originalRetryForeground = d.RetryForegroundLangRequest;

        Console.WriteLine("[Diag] ===== Auto diagnostics start =====");
        Console.WriteLine($"[Diag] Slots: en={en.SlotId} jp={jp.SlotId} zh={zh.SlotId}");

        var scenarios = new List<(bool change, bool setDefault, bool foreground)>
        {
            (true,  true,  true),
            (true,  true,  false),
            (true,  false, true),
            (true,  false, false),
            (false, true,  true),
            (false, true,  false),
            (false, false, true),
            (false, false, false),
        };

        int idx = 1;
        foreach (var s in scenarios)
        {
            d.RetryChangeCurrentLanguage = s.change;
            d.RetrySetDefaultProfile = s.setDefault;
            d.RetryForegroundLangRequest = s.foreground;

            Console.WriteLine($"[Diag] Scenario {idx}/8: change={s.change}, setDefault={s.setDefault}, foreground={s.foreground}");
            WaitForForegroundSwitch();
            bool ok = RunOneScenario(en.SlotId, jp.SlotId, zh.SlotId);
            Console.WriteLine($"[Diag] Scenario {idx}/8 result: {(ok ? "PASS" : "FAIL")}");
            idx++;
        }

        d.RetryChangeCurrentLanguage = originalRetryChange;
        d.RetrySetDefaultProfile = originalRetryDefault;
        d.RetryForegroundLangRequest = originalRetryForeground;

        Console.WriteLine("[Diag] ===== Auto diagnostics end =====");
    }

    private bool RunOneScenario(int enSlot, int jpSlot, int zhSlot)
    {
        bool ok = true;

        // 0409 -> 0411
        ok &= _switcher.SwitchByIdSync(enSlot);
        Thread.Sleep(150);
        ok &= _switcher.SwitchByIdSync(jpSlot);
        Thread.Sleep(180);
        var langJp = _switcher.GetCurrentLanguageSync();
        bool jpPass = langJp == TsfConstants.LANGID_JAPANESE;
        Console.WriteLine($"[Diag]   check JP lang=0x{(langJp ?? 0):X4} pass={jpPass}");
        ok &= jpPass;

        // 0409 -> 0804
        ok &= _switcher.SwitchByIdSync(enSlot);
        Thread.Sleep(150);
        ok &= _switcher.SwitchByIdSync(zhSlot);
        Thread.Sleep(180);
        var langZh = _switcher.GetCurrentLanguageSync();
        bool zhPass = langZh == TsfConstants.LANGID_CHINESE_SIMPLIFIED;
        Console.WriteLine($"[Diag]   check ZH lang=0x{(langZh ?? 0):X4} pass={zhPass}");
        ok &= zhPass;

        return ok;
    }

    private static void WaitForForegroundSwitch()
    {
        Console.WriteLine("[Diag]   Switch to target app now. Running in 5 seconds...");
        Thread.Sleep(ForegroundSwitchDelayMs);
    }
}

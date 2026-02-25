using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HyperIMSwitch.Core.Models;
using HyperIMSwitch.Interop;
using Microsoft.Win32;

namespace HyperIMSwitch.Core.Services;

/// <summary>
/// Enumerates all installed TSF profiles and keyboard layouts.
/// Uses ITfInputProcessorProfiles (avoids ITfInputProcessorProfileMgr which
/// can fail QI in ASTA/WinUI 3 environments).
/// Must be called from a COM-initialized thread.
/// </summary>
public sealed class TsfProfileEnumerator
{
    private readonly List<ImeProfile> _profiles = new();

    public IReadOnlyList<ImeProfile> Profiles => _profiles;

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHLoadIndirectString(
        string pszSource,
        StringBuilder pszOutBuf,
        int cchOutBuf,
        IntPtr ppvReserved);

    public void Enumerate()
    {
        _profiles.Clear();

        object raw = Activator.CreateInstance(
            Type.GetTypeFromCLSID(TsfConstants.CLSID_TF_InputProcessorProfiles)!)!;

        ITfInputProcessorProfiles prof;
        try
        {
            prof = (ITfInputProcessorProfiles)raw;
        }
        catch
        {
            Marshal.ReleaseComObject(raw);
            return;
        }

        try
        {
            LogGlobalTsfState(prof);
            EnumerateImeProfiles(prof);
            EnumerateKeyboardLayouts(prof);
            LogEnumInputProcessorInfo(prof);
        }
        finally
        {
            Marshal.ReleaseComObject(raw);
        }

        Console.WriteLine($"[Enum] Total profiles: {_profiles.Count}");
        foreach (var p in _profiles)
            Console.WriteLine($"[Enum]   [{p.LangId:X4}] type={p.ProfileType} hkl=0x{p.Hkl:X}  desc=\"{p.Description}\"");
    }

    // ---- TSF input processor profiles ----

    private void EnumerateImeProfiles(ITfInputProcessorProfiles prof)
    {
        // Get the list of all installed language IDs
        int hr = prof.GetLanguageList(out IntPtr ppLangId, out uint langCount);
        if (hr != 0 || ppLangId == IntPtr.Zero || langCount == 0) return;

        try
        {
            for (uint i = 0; i < langCount; i++)
            {
                ushort langid = (ushort)Marshal.ReadInt16(ppLangId + (int)(i * 2));
                EnumerateProfilesForLang(prof, langid);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(ppLangId);
        }
    }

    private void EnumerateProfilesForLang(ITfInputProcessorProfiles prof, ushort langid)
    {
        int hr = prof.EnumLanguageProfiles(langid, out IEnumTfLanguageProfiles? enumProf);
        if (hr != 0 || enumProf == null) return;

        var buffer = new TF_LANGUAGEPROFILE[16];
        try
        {
            while (true)
            {
                hr = enumProf.Next((uint)buffer.Length, buffer, out uint fetched);
                if (fetched == 0) break;

                for (uint i = 0; i < fetched; i++)
                {
                    var p = buffer[i];
                    if (p.clsid == Guid.Empty) continue;

                    string desc = GetDescription(prof, ref p);
                    LogLanguageProfileDetails(prof, ref p, desc);
                    _profiles.Add(new ImeProfile(
                        TsfConstants.TF_PROFILETYPE_INPUTPROCESSOR,
                        p.langid,
                        p.clsid,
                        p.guidProfile,
                        IntPtr.Zero,
                        desc));
                }

                if (hr != 0) break; // S_FALSE = no more
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumProf);
        }
    }

    private static string GetDescription(ITfInputProcessorProfiles prof,
        ref TF_LANGUAGEPROFILE p)
    {
        try
        {
            Guid clsid = p.clsid;
            Guid guid  = p.guidProfile;
            int hr = prof.GetLanguageProfileDescription(ref clsid, p.langid, ref guid, out string desc);
            return hr == 0 ? (desc ?? string.Empty) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ---- Keyboard layouts (for English) ----

    private void EnumerateKeyboardLayouts(ITfInputProcessorProfiles prof)
    {
        int count = GetKeyboardLayoutList(0, null);
        if (count <= 0) return;

        var hkls = new IntPtr[count];
        GetKeyboardLayoutList(count, hkls);

        foreach (var hkl in hkls)
        {
            // Low 16 bits = language ID; high 16 bits = layout ID
            ushort langid = (ushort)(hkl.ToInt64() & 0xFFFF);

            // Skip if this HKL is an IME substitute (high word != low word and high word != 0xE0xx)
            // Pure keyboard layouts have low == high, or high word = low word
            ushort layoutId = (ushort)((hkl.ToInt64() >> 16) & 0xFFFF);
            bool isIme = (layoutId & 0xF000) == 0xE000;
            Console.WriteLine(
                $"[Enum][HKL] lang=0x{langid:X4} hkl=0x{hkl.ToInt64():X} layoutId=0x{layoutId:X4} isImeSubstitute={isIme}");
            if (isIme) continue;

            // Check it's not already covered by a TSF profile with same langid
            bool alreadyAdded = false;
            foreach (var p in _profiles)
                if (p.LangId == langid && p.IsKeyboardLayout) { alreadyAdded = true; break; }
            if (alreadyAdded) continue;

            _profiles.Add(new ImeProfile(
                TsfConstants.TF_PROFILETYPE_KEYBOARDLAYOUT,
                langid,
                Guid.Empty,
                Guid.Empty,
                hkl,
                GetKeyboardLayoutDescription(langid, hkl)));
        }
    }

    private static void LogGlobalTsfState(ITfInputProcessorProfiles prof)
    {
        int hrCurrent = prof.GetCurrentLanguage(out ushort currentLang);
        Console.WriteLine($"[Enum][TSF] GetCurrentLanguage hr=0x{(uint)hrCurrent:X8} lang=0x{currentLang:X4}");
    }

    private static void LogLanguageProfileDetails(
        ITfInputProcessorProfiles prof, ref TF_LANGUAGEPROFILE p, string desc)
    {
        Guid clsid = p.clsid;
        Guid guid = p.guidProfile;
        Guid catid = p.catid;

        int hrEnabled = prof.IsEnabledLanguageProfile(ref clsid, p.langid, ref guid, out bool enabled);
        int hrDefault = prof.GetDefaultLanguageProfile(p.langid, ref catid, out Guid defaultClsid, out Guid defaultGuid);
        int hrActive = prof.GetActiveLanguageProfile(ref clsid, out ushort activeLang, out Guid activeGuid);
        bool isActiveMatch = hrActive == 0 && activeLang == p.langid && activeGuid == p.guidProfile;

        Console.WriteLine(
            "[Enum][TSFProfile] " +
            $"lang=0x{p.langid:X4} " +
            $"fActive={p.fActive} " +
            $"enabledHr=0x{(uint)hrEnabled:X8} enabled={enabled} " +
            $"defaultHr=0x{(uint)hrDefault:X8} defaultClsid={defaultClsid} defaultGuid={defaultGuid} " +
            $"activeHr=0x{(uint)hrActive:X8} activeLang=0x{activeLang:X4} activeGuid={activeGuid} activeMatch={isActiveMatch} " +
            $"catid={p.catid} clsid={p.clsid} guidProfile={p.guidProfile} desc=\"{desc}\"");
    }

    private static void LogEnumInputProcessorInfo(ITfInputProcessorProfiles prof)
    {
        int hr = prof.EnumInputProcessorInfo(out object rawEnum);
        if (hr != 0 || rawEnum == null)
        {
            Console.WriteLine($"[Enum][InputProcessorInfo] EnumInputProcessorInfo hr=0x{(uint)hr:X8}");
            return;
        }

        try
        {
            var enumProf = rawEnum as IEnumTfInputProcessorProfiles;
            if (enumProf == null)
            {
                Console.WriteLine("[Enum][InputProcessorInfo] cast to IEnumTfInputProcessorProfiles failed");
                return;
            }

            var buffer = new TF_INPUTPROCESSORPROFILE[16];
            while (true)
            {
                int hrNext = enumProf.Next((uint)buffer.Length, buffer, out uint fetched);
                if (fetched == 0) break;

                for (uint i = 0; i < fetched; i++)
                {
                    var p = buffer[i];
                    Console.WriteLine(
                        "[Enum][IPP] " +
                        $"type={p.dwProfileType} " +
                        $"lang=0x{p.langid:X4} " +
                        $"clsid={p.clsid} " +
                        $"guidProfile={p.guidProfile} " +
                        $"catid={p.catid} " +
                        $"hkl=0x{p.hkl.ToInt64():X} " +
                        $"hklSubstitute=0x{p.hklSubstitute.ToInt64():X} " +
                        $"caps=0x{p.dwCaps:X8} " +
                        $"flags=0x{p.dwFlags:X8}");
                }

                if (hrNext != 0) break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Enum][InputProcessorInfo] exception: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { Marshal.ReleaseComObject(rawEnum); } catch { /* ignore */ }
        }
    }

    private static string GetKeyboardLayoutDescription(ushort langid, IntPtr hkl)
    {
        var text = TryGetKeyboardLayoutDisplayText(hkl, langid);
        if (!string.IsNullOrWhiteSpace(text))
            return text!;

        try
        {
            string langName = CultureInfo.GetCultureInfo(langid).DisplayName;
            return $"{langName} Keyboard";
        }
        catch (CultureNotFoundException)
        {
            return $"KeyboardLayout-{langid:X4}";
        }
    }

    private static string? TryGetKeyboardLayoutDisplayText(IntPtr hkl, ushort langid)
    {
        foreach (var klid in BuildKlidCandidates(hkl, langid))
        {
            string keyPath = $@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\{klid}";
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key == null) continue;

            string? displayName = key.GetValue("Layout Display Name") as string;
            string? layoutText = key.GetValue("Layout Text") as string;

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                string resolved = ResolveIndirectString(displayName!);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }

            if (!string.IsNullOrWhiteSpace(layoutText))
                return layoutText;
        }

        return null;
    }

    private static IEnumerable<string> BuildKlidCandidates(IntPtr hkl, ushort langid)
    {
        uint hkl32 = unchecked((uint)hkl.ToInt64());
        ushort high = (ushort)((hkl32 >> 16) & 0xFFFF);

        var list = new List<string>
        {
            $"0000{langid:X4}",
            $"0000{high:X4}",
            $"{hkl32:X8}"
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            if (seen.Add(item))
                yield return item;
        }
    }

    private static string ResolveIndirectString(string input)
    {
        // Format: "@%SystemRoot%\\system32\\input.dll,-5000"
        if (!input.StartsWith("@", StringComparison.Ordinal))
            return input;

        var sb = new StringBuilder(260);
        int hr = SHLoadIndirectString(input, sb, sb.Capacity, IntPtr.Zero);
        return hr == 0 ? sb.ToString() : input;
    }
}

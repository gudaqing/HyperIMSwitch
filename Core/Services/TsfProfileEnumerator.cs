using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HyperIMSwitch.Core.Models;
using HyperIMSwitch.Interop;

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

    // Prefer US-English keyboard layout; fall back to any non-IME keyboard layout
    // (on Chinese Windows, 0x0409 may not be installed — 0x0804 keyboard layout
    //  gives raw/English keyboard input with no IME active)
    public ImeProfile? EnglishProfile =>
        FindFirst(p => p.LangId == TsfConstants.LANGID_ENGLISH_US && p.IsKeyboardLayout) ??
        FindFirst(p => p.IsKeyboardLayout);

    public ImeProfile? WeChatProfile   => FindFirst(p => p.IsWeChatIM);
    public ImeProfile? JapaneseProfile => FindFirst(p => p.IsJapanese);

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);

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
            EnumerateImeProfiles(prof);
            EnumerateKeyboardLayouts(prof);
        }
        finally
        {
            Marshal.ReleaseComObject(raw);
        }

        Console.WriteLine($"[Enum] Total profiles: {_profiles.Count}");
        foreach (var p in _profiles)
            Console.WriteLine($"[Enum]   [{p.LangId:X4}] type={p.ProfileType} hkl=0x{p.Hkl:X}  desc=\"{p.Description}\"");
        Console.WriteLine($"[Enum] English={EnglishProfile?.Description ?? "NOT FOUND"}");
        Console.WriteLine($"[Enum] WeChat={WeChatProfile?.Description ?? "NOT FOUND"}");
        Console.WriteLine($"[Enum] Japanese={JapaneseProfile?.Description ?? "NOT FOUND"}");
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
                $"KeyboardLayout-{langid:X4}"));
        }
    }

    private ImeProfile? FindFirst(Func<ImeProfile, bool> predicate)
    {
        foreach (var p in _profiles)
            if (predicate(p)) return p;
        return null;
    }
}

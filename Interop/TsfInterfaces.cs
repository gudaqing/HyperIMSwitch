using System;
using System.Runtime.InteropServices;

namespace HyperIMSwitch.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct TF_INPUTPROCESSORPROFILE
{
    public uint   dwProfileType;
    public ushort langid;
    public Guid   clsid;
    public Guid   guidProfile;
    public Guid   catid;
    public IntPtr hklSubstitute;
    public uint   dwCaps;
    public IntPtr hkl;
    public uint   dwFlags;
}

// IID: {71C6E74C-0F28-11D8-A82A-00065B84435C}
// vtable: IUnknown(3) + Clone, Next, Reset, Skip
[ComImport]
[Guid("71C6E74C-0F28-11D8-A82A-00065B84435C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumTfInputProcessorProfiles
{
    [PreserveSig] int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumTfInputProcessorProfiles ppEnum);
    [PreserveSig] int Next(uint ulCount,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] TF_INPUTPROCESSORPROFILE[] pProfile,
        out uint pcFetch);
    [PreserveSig] int Reset();
    [PreserveSig] int Skip(uint ulCount);
}


// IID: {1F02B6C5-7842-4EE6-8A0B-9A24183A95CA}
// Full 18-method interface, strictly following msctf.idl order
[ComImport]
[Guid("1F02B6C5-7842-4EE6-8A0B-9A24183A95CA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfInputProcessorProfiles
{
    [PreserveSig] int Register(ref Guid rclsid);
    [PreserveSig] int Unregister(ref Guid rclsid);
    [PreserveSig] int AddLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile,
        [MarshalAs(UnmanagedType.LPWStr)] string pchDesc, uint cchDesc,
        [MarshalAs(UnmanagedType.LPWStr)] string pchIconFile, uint cchFile, uint uIconIndex);
    [PreserveSig] int RemoveLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile);
    [PreserveSig] int EnumInputProcessorInfo([MarshalAs(UnmanagedType.Interface)] out object ppEnum);
    [PreserveSig] int GetDefaultLanguageProfile(ushort langid, ref Guid catid,
        out Guid pclsid, out Guid pguidProfile);
    [PreserveSig] int SetDefaultLanguageProfile(ushort langid, ref Guid rclsid, ref Guid guidProfiles);
    [PreserveSig] int ActivateLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfiles);
    [PreserveSig] int GetActiveLanguageProfile(ref Guid rclsid, out ushort plangid, out Guid pguidProfile);
    [PreserveSig] int GetLanguageProfileDescription(ref Guid rclsid, ushort langid,
        ref Guid guidProfile, [MarshalAs(UnmanagedType.BStr)] out string pbstrProfile);
    [PreserveSig] int GetCurrentLanguage(out ushort plangid);
    [PreserveSig] int ChangeCurrentLanguage(ushort langid);
    [PreserveSig] int GetLanguageList(out IntPtr ppLangId, out uint pulCount);
    [PreserveSig] int EnumLanguageProfiles(ushort langid,
        [MarshalAs(UnmanagedType.Interface)] out IEnumTfLanguageProfiles ppEnum);
    [PreserveSig] int EnableLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile, bool fEnable);
    [PreserveSig] int IsEnabledLanguageProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile, out bool pfEnable);
    [PreserveSig] int EnableLanguageProfileByDefault(ref Guid rclsid, ushort langid, ref Guid guidProfile, bool fEnable);
    [PreserveSig] int SubstituteKeyboardLayout(ref Guid rclsid, ushort langid, ref Guid guidProfile, IntPtr hKL);
}

// TF_LANGUAGEPROFILE — returned by IEnumTfLanguageProfiles.Next
[StructLayout(LayoutKind.Sequential)]
internal struct TF_LANGUAGEPROFILE
{
    public Guid   clsid;
    public ushort langid;
    public Guid   catid;
    [MarshalAs(UnmanagedType.Bool)]
    public bool   fActive;
    public Guid   guidProfile;
}

// ITfInputProcessorProfileMgr - IID: {71C6E74D-0F28-11D8-A82A-00065B84435C}
// Modern API: ActivateProfile works for both InputProcessors and KeyboardLayouts.
// Only the first vtable slot (ActivateProfile) is used here; the rest are stubbed.
[ComImport]
[Guid("71C6E74D-0F28-11D8-A82A-00065B84435C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfInputProcessorProfileMgr
{
    // slot 3 — ActivateProfile
    [PreserveSig] int ActivateProfile(
        uint dwProfileType, ushort langid,
        ref Guid rclsid, ref Guid guidProfile,
        IntPtr hkl, uint dwFlags);
    // slots 4-10 — stub (never called)
    void _DeactivateProfile();
    void _GetProfile();
    void _EnumProfiles();
    void _ReleaseInputProcessor();
    void _RegisterProfile();
    void _UnregisterProfile();
    void _GetActiveProfile();
}

// IID: {3D61BF11-AC5F-42C8-A4CB-931BCC28C744}
// vtable: Clone, Next, Reset, Skip
[ComImport]
[Guid("3D61BF11-AC5F-42C8-A4CB-931BCC28C744")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumTfLanguageProfiles
{
    [PreserveSig] int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumTfLanguageProfiles ppEnum);
    [PreserveSig] int Next(uint ulCount,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] TF_LANGUAGEPROFILE[] pProfile,
        out uint pcFetch);
    [PreserveSig] int Reset();
    [PreserveSig] int Skip(uint ulCount);
}

// ITfThreadMgr - IID: {AA80E801-2021-11D2-93E0-0060B067B86E}
[ComImport]
[Guid("AA80E801-2021-11D2-93E0-0060B067B86E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfThreadMgr
{
    [PreserveSig] int Activate(out uint ptid);
    [PreserveSig] int Deactivate();
    [PreserveSig] int CreateDocumentMgr([MarshalAs(UnmanagedType.Interface)] out object ppdim);
    [PreserveSig] int EnumDocumentMgrs([MarshalAs(UnmanagedType.Interface)] out object ppEnum);
    [PreserveSig] int GetFocus([MarshalAs(UnmanagedType.Interface)] out object ppdimFocus);
    [PreserveSig] int SetFocus([MarshalAs(UnmanagedType.Interface)] object pdimFocus);
    [PreserveSig] int AssociateFocus(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] object pdimNew,
        [MarshalAs(UnmanagedType.Interface)] out object ppdimPrev);
    [PreserveSig] int IsThreadFocus([MarshalAs(UnmanagedType.Interface)] object pdimFocus, out bool pfThreadFocus);
    [PreserveSig] int GetFunctionProvider(ref Guid clsid, [MarshalAs(UnmanagedType.Interface)] out object ppFuncProv);
    [PreserveSig] int EnumFunctionProviders([MarshalAs(UnmanagedType.Interface)] out object ppEnum);
    [PreserveSig] int GetGlobalCompartment([MarshalAs(UnmanagedType.Interface)] out ITfCompartmentMgr ppCompMgr);
}

// ITfCompartmentMgr - IID: {7DCF57AC-18AD-438B-824D-979BFFB74B7C}
[ComImport]
[Guid("7DCF57AC-18AD-438B-824D-979BFFB74B7C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfCompartmentMgr
{
    [PreserveSig] int GetCompartment(ref Guid rguid, [MarshalAs(UnmanagedType.Interface)] out ITfCompartment ppcomp);
    [PreserveSig] int ClearCompartment(uint tid, ref Guid rguid);
    [PreserveSig] int EnumCompartments([MarshalAs(UnmanagedType.Interface)] out object ppEnum);
}

// ITfCompartment - IID: {BB08F7A9-607A-4384-8623-056892B64371}
[ComImport]
[Guid("BB08F7A9-607A-4384-8623-056892B64371")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfCompartment
{
    [PreserveSig] int SetValue(uint tid, [In] ref object pvarValue);
    [PreserveSig] int GetValue([MarshalAs(UnmanagedType.Struct)] out object pvarValue);
    [PreserveSig] int OnChange(ref Guid rguid);
}

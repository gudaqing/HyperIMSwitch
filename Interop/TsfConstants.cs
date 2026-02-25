using System;

namespace HyperIMSwitch.Interop;

internal static class TsfConstants
{
    // TSF CoCreate CLSIDs
    public static readonly Guid CLSID_TF_InputProcessorProfiles =
        new("33C53A50-F456-4884-B049-85FD643ECFED");
    public static readonly Guid CLSID_TF_ThreadMgr =
        new("529A9E6B-6587-4F23-AB9E-9C7D683E3C50");

    // IIDs
    public static readonly Guid IID_ITfInputProcessorProfileMgr =
        new("71C6E74D-0F28-11D8-A82A-00065B84435C");
    public static readonly Guid IID_ITfInputProcessorProfiles =
        new("1F02B6C5-7842-4EE6-8A0B-9A24183A95CA");
    public static readonly Guid IID_IEnumTfInputProcessorProfiles =
        new("71C6E74C-0F28-11D8-A82A-00065B84435C");
    public static readonly Guid IID_ITfThreadMgr =
        new("AA80E801-2021-11D2-93E0-0060B067B86E");
    public static readonly Guid IID_ITfCompartmentMgr =
        new("7DCF57AC-18AD-438B-824D-979BFFB74B7C");
    public static readonly Guid IID_ITfCompartment =
        new("BB08F7A9-607A-4384-8623-056892B64371");

    // Compartment GUIDs
    public static readonly Guid GUID_COMPARTMENT_KEYBOARD_INPUTMODE_CONVERSION =
        new("CCF05DD8-4A87-11D7-A6E2-00065B84435C");
    public static readonly Guid GUID_COMPARTMENT_KEYBOARD_INPUTMODE_SENTENCE =
        new("4BF10B67-4BEE-11D7-A671-00065B84435C");

    // TF_PROFILETYPE
    public const uint TF_PROFILETYPE_INPUTPROCESSOR = 1;
    public const uint TF_PROFILETYPE_KEYBOARDLAYOUT = 2;

    // TF_IPPMF flags (msctf.h)
    public const uint TF_IPPMF_FORPROCESS                   = 0x00000001;
    public const uint TF_IPPMF_FORSESSION                   = 0x00000002;  // session-wide activation
    public const uint TF_IPPMF_ENABLEPROFILE                = 0x00000008;
    public const uint TF_IPPMF_DISABLEPROFILE               = 0x00000010;  // ← 旧誤値は実はこれ
    public const uint TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE = 0x00000020;

    // Japanese conversion modes
    public const int CONVERSION_MODE_ALPHANUMERIC  = 0x00;  // 英数字
    public const int CONVERSION_MODE_HIRAGANA      = 0x09;  // 平仮名 NATIVE(1)|FULLSHAPE(8)
    public const int CONVERSION_MODE_KATAKANA_FULL = 0x0B;  // 全角カタカナ NATIVE(1)|KATAKANA(2)|FULLSHAPE(8)
    public const int CONVERSION_MODE_KATAKANA_HALF = 0x03;  // 半角カタカナ NATIVE(1)|KATAKANA(2)

    // Language IDs
    public const ushort LANGID_ENGLISH_US        = 0x0409;
    public const ushort LANGID_CHINESE_SIMPLIFIED = 0x0804;
    public const ushort LANGID_JAPANESE           = 0x0411;
}

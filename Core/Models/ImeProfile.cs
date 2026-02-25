using System;
using HyperIMSwitch.Interop;

namespace HyperIMSwitch.Core.Models;

/// <summary>Runtime-enumerated TSF profile info.</summary>
public sealed record ImeProfile(
    uint   ProfileType,
    ushort LangId,
    Guid   Clsid,
    Guid   GuidProfile,
    IntPtr Hkl,
    string Description
)
{
    public bool IsKeyboardLayout => ProfileType == TsfConstants.TF_PROFILETYPE_KEYBOARDLAYOUT;
    public bool IsInputProcessor  => ProfileType == TsfConstants.TF_PROFILETYPE_INPUTPROCESSOR;
    public bool IsEnglish   => LangId == TsfConstants.LANGID_ENGLISH_US && IsKeyboardLayout;
    public bool IsJapanese  => LangId == TsfConstants.LANGID_JAPANESE   && IsInputProcessor;
    public bool IsWeChatIM  => Description.Contains("微信输入法", StringComparison.OrdinalIgnoreCase)
                            || Description.Contains("WeChat Input", StringComparison.OrdinalIgnoreCase);
}

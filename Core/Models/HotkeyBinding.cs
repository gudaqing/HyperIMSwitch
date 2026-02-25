using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HyperIMSwitch.Core.Models;

public sealed class HotkeyBinding
{
    /// <summary>1-based slot ID — used as the RegisterHotKey id parameter.</summary>
    public int    SlotId         { get; set; }

    /// <summary>TF_PROFILETYPE_KEYBOARDLAYOUT(2) or TF_PROFILETYPE_INPUTPROCESSOR(1).</summary>
    public uint   ProfileType    { get; set; }

    public ushort LangId         { get; set; }
    public Guid   Clsid          { get; set; }
    public Guid   GuidProfile    { get; set; }
    public long   Hkl            { get; set; }

    /// <summary>null = do not change; 0x00/0x09/0x0B/0x03 for JP conversion modes.</summary>
    public int?   ConversionMode { get; set; }

    public string DisplayName    { get; set; } = string.Empty;

    public uint   Modifiers      { get; set; }
    public uint   VirtualKey     { get; set; }

    [JsonIgnore]
    public bool IsValid => VirtualKey != 0;

    [JsonIgnore]
    public string HotkeyText
    {
        get
        {
            if (!IsValid) return string.Empty;
            var parts = new List<string>();
            if ((Modifiers & 0x0002) != 0) parts.Add("Ctrl");
            if ((Modifiers & 0x0001) != 0) parts.Add("Alt");
            if ((Modifiers & 0x0004) != 0) parts.Add("Shift");
            if ((Modifiers & 0x0008) != 0) parts.Add("Win");
            parts.Add(((Windows.System.VirtualKey)VirtualKey).ToString());
            return string.Join("+", parts);
        }
    }
}

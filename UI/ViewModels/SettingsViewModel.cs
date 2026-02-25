using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperIMSwitch.Core.Models;
using HyperIMSwitch.Interop;

namespace HyperIMSwitch.UI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public ObservableCollection<BindingRowViewModel> Rows { get; } = new();

    private bool _autoStart;
    public bool AutoStart
    {
        get => _autoStart;
        set => SetProperty(ref _autoStart, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public IReadOnlyList<ProfileOption>        AvailableProfiles { get; private set; } = new List<ProfileOption>();
    public IReadOnlyList<ConversionModeOption> AvailableModes    { get; private set; } = BuildModes();

    public ObservableCollection<string> InstalledProfiles { get; } = new();

    public void LoadFromApp()
    {
        var settings   = App.Current.Settings?.Settings;
        var enumerator = App.Current.Enumerator;

        // Build profile options from enumerator
        var profileOptions = new List<ProfileOption>();
        if (enumerator != null)
        {
            InstalledProfiles.Clear();
            foreach (var p in enumerator.Profiles)
            {
                string label = BuildProfileLabel(p);
                InstalledProfiles.Add(label);
                profileOptions.Add(new ProfileOption(p, label));
            }
        }
        AvailableProfiles = profileOptions;

        // Rebuild rows from saved bindings
        Rows.Clear();
        if (settings != null)
        {
            foreach (var b in settings.Hotkeys.OrderBy(x => x.SlotId))
            {
                var row = CreateRow(b);
                // Restore selected profile
                var matchingOption = AvailableProfiles.FirstOrDefault(p =>
                    p.Profile.ProfileType == b.ProfileType &&
                    p.Profile.LangId      == b.LangId      &&
                    p.Profile.Clsid       == b.Clsid        &&
                    p.Profile.GuidProfile == b.GuidProfile &&
                    p.Profile.Hkl.ToInt64() == b.Hkl);
                row.SelectedProfile = matchingOption;

                // Restore selected mode
                if (b.ConversionMode.HasValue)
                    row.SelectedMode = AvailableModes.FirstOrDefault(m => m.Mode == b.ConversionMode.Value);

                Rows.Add(row);
            }
        }

        AutoStart = App.Current.AutoStart?.IsEnabled ?? false;
    }

    [RelayCommand]
    private void AddRow()
    {
        var binding = new HotkeyBinding { SlotId = NextSlotId() };
        Rows.Add(CreateRow(binding));
    }

    private void RemoveRow(BindingRowViewModel row)
    {
        Rows.Remove(row);
    }

    [RelayCommand]
    private void Save()
    {
        var svc = App.Current.Settings;
        if (svc == null) return;

        // Re-assign SlotIds sequentially (1-based)
        int slot = 1;
        var newBindings = new List<HotkeyBinding>();
        foreach (var row in Rows)
        {
            // Sync profile info from selected option
            if (row.SelectedProfile != null)
            {
                row.Binding.SlotId      = slot++;
                row.Binding.ProfileType = row.SelectedProfile.Profile.ProfileType;
                row.Binding.LangId      = row.SelectedProfile.Profile.LangId;
                row.Binding.Clsid       = row.SelectedProfile.Profile.Clsid;
                row.Binding.GuidProfile = row.SelectedProfile.Profile.GuidProfile;
                row.Binding.Hkl         = row.SelectedProfile.Profile.Hkl.ToInt64();
                row.Binding.DisplayName = row.SelectedProfile.DisplayName;
                row.Binding.ConversionMode = row.SelectedMode?.Mode;
                if (row.Binding.IsValid)
                    newBindings.Add(row.Binding);
            }
        }

        svc.Settings.Hotkeys   = newBindings;
        svc.Settings.AutoStart = AutoStart;
        svc.Save();

        App.Current.AutoStart?.SetEnabled(AutoStart);
        App.Current.Enumerator?.Enumerate();
        App.Current.Switcher?.UpdateBindings(newBindings);
        App.Current.HotkeyService?.ApplyBindings(newBindings);

        StatusMessage = "已保存 / Saved";
    }

    // ---- Helpers ----

    private BindingRowViewModel CreateRow(HotkeyBinding binding)
        => new BindingRowViewModel(AvailableProfiles, AvailableModes, binding, RemoveRow);

    private int NextSlotId()
    {
        int max = 0;
        foreach (var r in Rows)
            if (r.Binding.SlotId > max) max = r.Binding.SlotId;
        return max + 1;
    }

    private static IReadOnlyList<ConversionModeOption> BuildModes() =>
        new List<ConversionModeOption>
        {
            new(TsfConstants.CONVERSION_MODE_HIRAGANA,      "平仮名 / Hiragana"),
            new(TsfConstants.CONVERSION_MODE_KATAKANA_FULL, "全角カタカナ / Full Katakana"),
            new(TsfConstants.CONVERSION_MODE_KATAKANA_HALF, "半角カタカナ / Half Katakana"),
            new(TsfConstants.CONVERSION_MODE_ALPHANUMERIC,  "英数字 / Alphanumeric"),
        };

    private static string BuildProfileLabel(ImeProfile profile)
    {
        var langName = GetLanguageDisplayName(profile.LangId);
        var desc = string.IsNullOrWhiteSpace(profile.Description) ? "(No Description)" : profile.Description;
        if (profile.ProfileType == TsfConstants.TF_PROFILETYPE_KEYBOARDLAYOUT)
        {
            // For keyboard layouts, if description is just language text (eg: "日语"),
            // avoid noisy duplicate label like "日语 - 日语".
            if (string.Equals(desc, langName, System.StringComparison.CurrentCultureIgnoreCase))
                return $"[键盘] {langName} [{profile.LangId:X4}]";

            return $"[键盘] {langName} [{profile.LangId:X4}] - {desc}";
        }

        return $"[输入法] {langName} [{profile.LangId:X4}] - {desc}";
    }

    private static string GetLanguageDisplayName(ushort langId)
    {
        try
        {
            return CultureInfo.GetCultureInfo(langId).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return $"Language 0x{langId:X4}";
        }
    }
}

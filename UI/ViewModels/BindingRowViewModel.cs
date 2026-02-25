using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperIMSwitch.Core.Models;
using HyperIMSwitch.Interop;

namespace HyperIMSwitch.UI.ViewModels;

/// <summary>Represents one IME → hotkey binding row in the settings UI.</summary>
public sealed partial class BindingRowViewModel : ObservableObject
{
    public IReadOnlyList<ProfileOption>        AvailableProfiles { get; }
    public IReadOnlyList<ConversionModeOption> AvailableModes    { get; }

    [ObservableProperty]
    private ProfileOption? _selectedProfile;

    [ObservableProperty]
    private ConversionModeOption? _selectedMode;

    private bool _showModeSelector;
    public bool ShowModeSelector
    {
        get => _showModeSelector;
        private set => SetProperty(ref _showModeSelector, value);
    }

    /// <summary>The underlying hotkey binding — mutated in-place by HotkeyEditorControl.</summary>
    public HotkeyBinding Binding { get; }

    public IRelayCommand RemoveCommand { get; }

    public BindingRowViewModel(
        IReadOnlyList<ProfileOption>        availableProfiles,
        IReadOnlyList<ConversionModeOption> availableModes,
        HotkeyBinding                       binding,
        System.Action<BindingRowViewModel>  removeCallback)
    {
        AvailableProfiles = availableProfiles;
        AvailableModes    = availableModes;
        Binding           = binding;
        RemoveCommand     = new RelayCommand(() => removeCallback(this));
    }

    partial void OnSelectedProfileChanged(ProfileOption? value)
    {
        bool isJapaneseIme = value?.Profile.LangId == TsfConstants.LANGID_JAPANESE
                          && value.Profile.IsInputProcessor;
        ShowModeSelector = isJapaneseIme;

        if (isJapaneseIme && AvailableModes.Count > 0)
        {
            // Default to Hiragana (index 0 = "平仮名")
            SelectedMode = AvailableModes[0];
        }
        else
        {
            SelectedMode = null;
        }

        // Sync profile data into the binding
        if (value != null)
        {
            Binding.ProfileType = value.Profile.ProfileType;
            Binding.LangId      = value.Profile.LangId;
            Binding.Clsid       = value.Profile.Clsid;
            Binding.GuidProfile = value.Profile.GuidProfile;
            Binding.Hkl         = value.Profile.Hkl.ToInt64();
            Binding.DisplayName = value.DisplayName;
        }
    }

    partial void OnSelectedModeChanged(ConversionModeOption? value)
    {
        Binding.ConversionMode = value?.Mode;
    }
}

// ---- Option records ----

public record ProfileOption(ImeProfile Profile, string DisplayName);

public record ConversionModeOption(int? Mode, string DisplayName);

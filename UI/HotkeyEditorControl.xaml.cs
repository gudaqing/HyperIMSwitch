using HyperIMSwitch.Core.Models;
using HyperIMSwitch.Interop;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace HyperIMSwitch.UI;

public sealed partial class HotkeyEditorControl : UserControl
{
    private bool           _recording;
    private HotkeyBinding? _binding;

    public HotkeyBinding? Binding
    {
        get => _binding;
        set { _binding = value; UpdateDisplay(); }
    }

    public HotkeyEditorControl()
    {
        InitializeComponent();
    }

    private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _recording = true;
        HotkeyTextBox.PlaceholderText = "按下热键组合… / Press hotkey combo…";
        HotkeyTextBox.Text = string.Empty;
    }

    private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _recording = false;
        HotkeyTextBox.PlaceholderText = "点击录制热键 / Click to record";
        UpdateDisplay();
    }

    private void HotkeyTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording) return;

        // Ignore stand-alone modifier keys
        if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
                  or VirtualKey.LeftWindows or VirtualKey.RightWindows)
            return;

        uint modifiers = 0;
        if (IsKeyDown(VirtualKey.Control))  modifiers |= HotkeyNative.MOD_CONTROL;
        if (IsKeyDown(VirtualKey.Menu))     modifiers |= HotkeyNative.MOD_ALT;
        if (IsKeyDown(VirtualKey.Shift))    modifiers |= HotkeyNative.MOD_SHIFT;
        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
            modifiers |= HotkeyNative.MOD_WIN;

        _binding ??= new HotkeyBinding();
        _binding.Modifiers  = modifiers;
        _binding.VirtualKey = (uint)e.Key;

        _recording = false;
        UpdateDisplay();
        e.Handled = true;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_binding != null)
        {
            _binding.Modifiers  = 0;
            _binding.VirtualKey = 0;
        }
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        HotkeyTextBox.Text = (_binding?.IsValid == true) ? _binding.HotkeyText : string.Empty;
    }
}

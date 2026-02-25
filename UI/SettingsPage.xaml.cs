using HyperIMSwitch.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace HyperIMSwitch.UI;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _vm = new();

    public SettingsPage()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            _vm.LoadFromApp();

            // Bind dynamic rows list
            BindingRowsControl.ItemsSource = _vm.Rows;

            // Wire auto-start toggle
            AutoStartToggle.IsOn = _vm.AutoStart;
            AutoStartToggle.Toggled += (s, _) => _vm.AutoStart = ((ToggleSwitch)s).IsOn;

            // Wire detected profiles list
            ProfilesList.ItemsSource = _vm.InstalledProfiles;

            // Wire status text
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.StatusMessage))
                    StatusText.Text = _vm.StatusMessage;
            };
        };
    }

    private void AddRowButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _vm.AddRowCommand.Execute(null);
    }

    private void SaveButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _vm.SaveCommand.Execute(null);
    }
}

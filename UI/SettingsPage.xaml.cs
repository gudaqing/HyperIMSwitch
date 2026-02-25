using HyperIMSwitch.UI.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyperIMSwitch.UI;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _vm = new();
    private readonly DispatcherTimer _autoSaveTimer = new();
    private bool _isLoading;

    public SettingsPage()
    {
        InitializeComponent();

        _autoSaveTimer.Interval = System.TimeSpan.FromMilliseconds(500);
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            _vm.SaveCommand.Execute(null);
        };

        Loaded += (_, _) =>
        {
            _isLoading = true;
            _vm.LoadFromApp();

            // Bind dynamic rows list
            BindingRowsControl.ItemsSource = _vm.Rows;
            _vm.Rows.CollectionChanged += Rows_CollectionChanged;
            foreach (var row in _vm.Rows)
                row.PropertyChanged += Row_PropertyChanged;

            // Wire auto-start toggle
            AutoStartToggle.IsOn = _vm.AutoStart;
            AutoStartToggle.Toggled += (s, _) =>
            {
                _vm.AutoStart = ((ToggleSwitch)s).IsOn;
                ScheduleAutoSave();
            };

            // Wire detected profiles list
            ProfilesList.ItemsSource = _vm.InstalledProfiles;

            // Wire status text
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.StatusMessage))
                    StatusText.Text = _vm.StatusMessage;
            };

            _isLoading = false;
        };

        Unloaded += (_, _) =>
        {
            _autoSaveTimer.Stop();
            _vm.Rows.CollectionChanged -= Rows_CollectionChanged;
            foreach (var row in _vm.Rows)
                row.PropertyChanged -= Row_PropertyChanged;
        };
    }

    private void AddRowButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _vm.AddRowCommand.Execute(null);
        ScheduleAutoSave();
    }

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
                if (item is BindingRowViewModel oldRow)
                    oldRow.PropertyChanged -= Row_PropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
                if (item is BindingRowViewModel newRow)
                    newRow.PropertyChanged += Row_PropertyChanged;
        }

        ScheduleAutoSave();
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BindingRowViewModel.SelectedProfile) ||
            e.PropertyName == nameof(BindingRowViewModel.SelectedMode))
        {
            ScheduleAutoSave();
        }
    }

    private void HotkeyEditorControl_HotkeyChanged(object sender, System.EventArgs e)
    {
        ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        if (_isLoading) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }
}

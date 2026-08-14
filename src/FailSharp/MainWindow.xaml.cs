using System.Windows;
using FailSharp.Core;
using CheckBox = System.Windows.Controls.CheckBox;

namespace FailSharp;

public partial class MainWindow : Window
{
    private readonly FailureEngine _engine;
    private readonly ISimVarClient _simVarClient;
    private readonly MenuClient _menuClient;
    private readonly List<CheckBox> _engineCheckBoxes = new();
    private bool _initializing = true;
    private bool _enginesPopulated;

    public MainWindow(FailureEngine engine, ISimVarClient simVarClient, MenuClient menuClient)
    {
        InitializeComponent();

        _engine = engine;
        _simVarClient = simVarClient;
        _menuClient = menuClient;

        var settings = engine.Settings;
        EnabledCheckBox.IsChecked = settings.Enabled;
        ProbabilitySlider.Value = settings.ProbabilityPercent;
        IntervalSlider.Value = settings.CheckIntervalMinutes;
        _initializing = false;

        _simVarClient.ConnectionStatusChanged += (_, _) => Dispatcher.BeginInvoke(RefreshStatus);
        _menuClient.ConnectionStatusChanged += (_, _) => Dispatcher.BeginInvoke(RefreshStatus);
        RefreshStatus();

        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    private void RefreshStatus()
    {
        StatusText.Text = $"SimConnect: {(_simVarClient.IsConnected ? "connected" : "waiting for sim...")}  |  Menu: {(_menuClient.IsConnected ? "connected" : "waiting for sim...")}";

        if (_simVarClient.IsConnected && !_enginesPopulated)
        {
            _enginesPopulated = true;
            _ = PopulateEnginesAsync();
        }
    }

    private async Task PopulateEnginesAsync()
    {
        int engineCount;
        try
        {
            engineCount = await _simVarClient.GetEngineCountAsync().ConfigureAwait(false);
        }
        catch
        {
            _enginesPopulated = false;
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            EnginesPanel.Children.Clear();
            _engineCheckBoxes.Clear();

            for (var i = 1; i <= engineCount; i++)
            {
                var checkBox = new CheckBox
                {
                    Content = $"#{i}",
                    Margin = new Thickness(0, 0, 12, 0),
                    IsChecked = _engine.Settings.EligibleEngines.Contains(i),
                    Tag = i,
                };
                checkBox.Checked += OnEngineEligibilityChanged;
                checkBox.Unchecked += OnEngineEligibilityChanged;
                _engineCheckBoxes.Add(checkBox);
                EnginesPanel.Children.Add(checkBox);
            }
        });
    }

    private void OnEngineEligibilityChanged(object sender, RoutedEventArgs e)
    {
        var checkBox = (CheckBox)sender;
        var index = (int)checkBox.Tag;
        if (checkBox.IsChecked == true)
        {
            _engine.Settings.EligibleEngines.Add(index);
        }
        else
        {
            _engine.Settings.EligibleEngines.Remove(index);
        }

        SaveSettings();
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        _engine.Settings.Enabled = EnabledCheckBox.IsChecked == true;
        SaveSettings();
    }

    private void OnProbabilityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ProbabilityLabel.Text = $"Chance per check: {e.NewValue:0}%";
        if (_initializing)
        {
            return;
        }

        _engine.Settings.ProbabilityPercent = e.NewValue;
        SaveSettings();
    }

    private void OnIntervalChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        IntervalLabel.Text = $"Check interval: {e.NewValue:0} min";
        if (_initializing)
        {
            return;
        }

        _engine.Settings.CheckIntervalMinutes = e.NewValue;
        SaveSettings();
    }

    private async void OnRepairClicked(object sender, RoutedEventArgs e)
    {
        RepairButton.IsEnabled = false;
        try
        {
            await _engine.RepairAllAsync().ConfigureAwait(true);
        }
        catch
        {
            // Not connected or a transient SimConnect error; nothing to repair right now.
        }
        finally
        {
            RepairButton.IsEnabled = true;
        }
    }

    private void SaveSettings() => SettingsStore.Save(_engine.Settings);
}

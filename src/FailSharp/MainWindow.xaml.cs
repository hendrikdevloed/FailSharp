using System.Windows;
using FailSharp.Core;
using CheckBox = System.Windows.Controls.CheckBox;

namespace FailSharp;

public partial class MainWindow : Window
{
    private readonly FailureEngine _engine;
    private readonly ISimVarClient _simVarClient;
    private readonly NativeControlClient _menuClient;
    private readonly List<CheckBox> _engineCheckBoxes = new();
    private bool _initializing = true;
    private bool _enginesPopulated;

    public MainWindow(FailureEngine engine, ISimVarClient simVarClient, NativeControlClient menuClient)
    {
        InitializeComponent();

        _engine = engine;
        _simVarClient = simVarClient;
        _menuClient = menuClient;

        var settings = engine.Settings;
        EnabledCheckBox.IsChecked = settings.Enabled;
        ProbabilitySlider.Value = settings.ProbabilityPercent;
        IntervalSlider.Value = ClosestIntervalIndex(settings.CheckIntervalMinutes);
        MinSpeedSlider.Value = settings.MinSpeedKts;
        MaxSpeedSlider.Value = settings.MaxSpeedKts;
        Topmost = settings.AlwaysOnTop;

        // Slider.Value defaults to its Minimum (0), so if a loaded setting maps to that same
        // value, the assignments above are no-ops and ValueChanged never fires — the thumb still
        // ends up in the right place (it *is* Value), but a label driven only by the event would
        // be left showing its XAML placeholder text. Refresh all of them explicitly instead.
        UpdateProbabilityLabel();
        UpdateIntervalLabel();
        UpdateSpeedRangeVisuals();

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
        SetConnectionIndicator(SimConnectStatusText, "SimConnect", _simVarClient.IsConnected);
        SetConnectionIndicator(MenuStatusText, "Menu", _menuClient.IsConnected);

        if (_simVarClient.IsConnected && !_enginesPopulated)
        {
            _enginesPopulated = true;
            _ = PopulateEnginesAsync();
        }
    }

    private static void SetConnectionIndicator(System.Windows.Controls.TextBlock textBlock, string label, bool connected)
    {
        textBlock.Text = connected ? $"{label} ✓" : $"{label} ✕";
        textBlock.Foreground = connected ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Gray;
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

    /// <summary>Raised whenever Enabled changes via the window's own checkbox, so other UI (the tray menu) can mirror it.</summary>
    public event EventHandler<bool>? EnabledChanged;

    /// <summary>Sets the Enabled checkbox from an external source (e.g. the tray menu). A no-op if already at that value.</summary>
    public void SetEnabled(bool enabled) => EnabledCheckBox.IsChecked = enabled;

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        var enabled = EnabledCheckBox.IsChecked == true;
        if (_initializing)
        {
            return;
        }

        _engine.Settings.Enabled = enabled;
        SaveSettings();
        EnabledChanged?.Invoke(this, enabled);
    }

    private void UpdateProbabilityLabel() => ProbabilityLabel.Text = $"Chance: {ProbabilitySlider.Value:0}%";

    private void OnProbabilityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateProbabilityLabel();
        if (_initializing)
        {
            return;
        }

        _engine.Settings.ProbabilityPercent = e.NewValue;
        SaveSettings();
    }

    private static int ClosestIntervalIndex(double minutes)
    {
        var options = CheckIntervalOptions.Minutes;
        var bestIndex = 0;
        var bestDiff = double.MaxValue;
        for (var i = 0; i < options.Length; i++)
        {
            var diff = Math.Abs(options[i] - minutes);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private void UpdateIntervalLabel()
    {
        var minutes = CheckIntervalOptions.Minutes[(int)IntervalSlider.Value];
        IntervalLabel.Text = $"Every: {CheckIntervalOptions.Format(minutes)}";
    }

    private void OnIntervalChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateIntervalLabel();
        if (_initializing)
        {
            return;
        }

        _engine.Settings.CheckIntervalMinutes = CheckIntervalOptions.Minutes[(int)e.NewValue];
        SaveSettings();
    }

    private void UpdateSpeedRangeVisuals()
    {
        var min = MinSpeedSlider.Value;
        var max = MaxSpeedSlider.Value;
        var upper = MinSpeedSlider.Maximum;

        SpeedRangeLabel.Text = $"Active between {min:0}-{max:0} kts";
        BelowMinColumn.Width = new GridLength(min, GridUnitType.Star);
        InRangeColumn.Width = new GridLength(max - min, GridUnitType.Star);
        AboveMaxColumn.Width = new GridLength(upper - max, GridUnitType.Star);
    }

    private void OnSpeedRangeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinSpeedSlider.Value > MaxSpeedSlider.Value)
        {
            if (sender == MinSpeedSlider)
            {
                MaxSpeedSlider.Value = MinSpeedSlider.Value;
            }
            else
            {
                MinSpeedSlider.Value = MaxSpeedSlider.Value;
            }

            return; // the resulting change re-enters this handler and finishes the update
        }

        UpdateSpeedRangeVisuals();

        if (_initializing)
        {
            return;
        }

        _engine.Settings.MinSpeedKts = MinSpeedSlider.Value;
        _engine.Settings.MaxSpeedKts = MaxSpeedSlider.Value;
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

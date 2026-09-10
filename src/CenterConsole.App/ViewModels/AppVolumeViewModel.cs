using System.Collections.ObjectModel;
using System.Windows.Threading;
using CenterConsole.Core.Models;
using CenterConsole.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterConsole.App.ViewModels;

public sealed partial class AppVolumeRow : ObservableObject
{
    public string ProcessName { get; }

    [ObservableProperty]
    private double volumePercent;

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private bool isHotkeyTarget;

    public AppVolumeRow(string processName, double volumePercent, bool isMuted)
    {
        ProcessName = processName;
        this.volumePercent = volumePercent;
        this.isMuted = isMuted;
    }
}

public sealed partial class AppVolumeViewModel : ObservableObject, IDisposable
{
    private readonly IAppVolumeService _appVolumeService;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly DispatcherTimer _refreshTimer;
    private bool _suppressVolumeCallback;

    public ObservableCollection<AppVolumeRow> Sessions { get; } = new();

    [ObservableProperty]
    private HotkeyBinding volumeUpHotkey;

    [ObservableProperty]
    private HotkeyBinding volumeDownHotkey;

    [ObservableProperty]
    private int stepPercent;

    public AppVolumeViewModel(IAppVolumeService appVolumeService, AppSettings settings, Action saveSettings)
    {
        _appVolumeService = appVolumeService;
        _settings = settings;
        _saveSettings = saveSettings;

        volumeUpHotkey = settings.AppVolumeUpHotkey;
        volumeDownHotkey = settings.AppVolumeDownHotkey;
        stepPercent = settings.AppVolumeStepPercent;

        // OnSessionCreated fires on a COM callback thread, not the UI thread. Refresh() mutates
        // Sessions, an ObservableCollection bound to WPF controls, so it must run on the Dispatcher.
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        _appVolumeService.SessionsChanged += (_, _) => dispatcher.BeginInvoke(Refresh);

        // Session-removal notifications from Core Audio are unreliable on their own, so this timer
        // is the backstop that drops rows for processes that have exited (see plan: AppVolumeService).
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        Refresh();
    }

    public void Refresh()
    {
        var live = _appVolumeService.GetActiveSessions();
        var liveNames = live.Select(t => t.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            if (!liveNames.Contains(Sessions[i].ProcessName))
                Sessions.RemoveAt(i);
        }

        foreach (AppVolumeTarget target in live)
        {
            var row = Sessions.FirstOrDefault(r => string.Equals(r.ProcessName, target.ProcessName, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new AppVolumeRow(target.ProcessName, target.Volume * 100.0, target.IsMuted)
                {
                    IsHotkeyTarget = string.Equals(target.ProcessName, _settings.SelectedTargetAppProcessName, StringComparison.OrdinalIgnoreCase),
                };
                row.PropertyChanged += OnRowPropertyChanged;
                Sessions.Add(row);
            }
            else
            {
                _suppressVolumeCallback = true;
                row.VolumePercent = target.Volume * 100.0;
                row.IsMuted = target.IsMuted;
                _suppressVolumeCallback = false;
            }
        }
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not AppVolumeRow row)
            return;

        switch (e.PropertyName)
        {
            case nameof(AppVolumeRow.VolumePercent):
                if (!_suppressVolumeCallback)
                    _appVolumeService.SetVolume(row.ProcessName, (float)(row.VolumePercent / 100.0));
                break;

            case nameof(AppVolumeRow.IsMuted):
                _appVolumeService.SetMuted(row.ProcessName, row.IsMuted);
                break;

            case nameof(AppVolumeRow.IsHotkeyTarget):
                if (row.IsHotkeyTarget)
                {
                    foreach (var other in Sessions.Where(r => r != row && r.IsHotkeyTarget))
                        other.IsHotkeyTarget = false;

                    _settings.SelectedTargetAppProcessName = row.ProcessName;
                    _saveSettings();
                }
                break;
        }
    }

    public void AdjustTargetVolume(float delta)
    {
        string? target = _settings.SelectedTargetAppProcessName;
        if (target is null)
            return;

        _appVolumeService.AdjustVolume(target, delta);
        Refresh();
    }

    public void ApplyNewVolumeUpHotkey(HotkeyBinding binding)
    {
        VolumeUpHotkey = binding;
        _settings.AppVolumeUpHotkey = binding;
        _saveSettings();
    }

    public void ApplyNewVolumeDownHotkey(HotkeyBinding binding)
    {
        VolumeDownHotkey = binding;
        _settings.AppVolumeDownHotkey = binding;
        _saveSettings();
    }

    partial void OnStepPercentChanged(int value)
    {
        _settings.AppVolumeStepPercent = value;
        _saveSettings();
    }

    public void Dispose() => _refreshTimer.Stop();
}

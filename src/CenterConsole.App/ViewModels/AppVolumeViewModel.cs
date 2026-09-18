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

    /// <summary>False for a remembered target that has no active audio session right now (e.g. Spotify
    /// hasn't started playing yet this session) - kept visible and selected instead of disappearing, so
    /// the user never has to reselect it once it does make sound.</summary>
    [ObservableProperty]
    private bool isLive;

    public AppVolumeRow(string processName, double volumePercent, bool isMuted, bool isLive = true)
    {
        ProcessName = processName;
        this.volumePercent = volumePercent;
        this.isMuted = isMuted;
        this.isLive = isLive;
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
        string? rememberedTarget = _settings.SelectedTargetAppProcessName;

        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            var existingRow = Sessions[i];
            bool isLive = liveNames.Contains(existingRow.ProcessName);
            bool isRememberedTarget = string.Equals(existingRow.ProcessName, rememberedTarget, StringComparison.OrdinalIgnoreCase);

            // A row for a process with no active session is normally dropped, except the remembered
            // hotkey target: keeping it visible (as a non-live placeholder) is what lets the user see
            // it's still selected instead of it silently vanishing until the app makes sound again.
            if (!isLive && !isRememberedTarget)
                Sessions.RemoveAt(i);
            else
                existingRow.IsLive = isLive;
        }

        foreach (AppVolumeTarget target in live)
        {
            var row = Sessions.FirstOrDefault(r => string.Equals(r.ProcessName, target.ProcessName, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new AppVolumeRow(target.ProcessName, target.Volume * 100.0, target.IsMuted)
                {
                    IsHotkeyTarget = string.Equals(target.ProcessName, rememberedTarget, StringComparison.OrdinalIgnoreCase),
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

        if (rememberedTarget is not null && !Sessions.Any(r => string.Equals(r.ProcessName, rememberedTarget, StringComparison.OrdinalIgnoreCase)))
        {
            var placeholder = new AppVolumeRow(rememberedTarget, volumePercent: 100, isMuted: false, isLive: false)
            {
                IsHotkeyTarget = true,
            };
            placeholder.PropertyChanged += OnRowPropertyChanged;
            Sessions.Add(placeholder);
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

using System.IO;
using CenterConsole.Core.Models;
using CenterConsole.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterConsole.App.ViewModels;

public sealed partial class TarkovViewModel : ObservableObject
{
    private readonly ITarkovMatchWatcher _watcher;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;

    [ObservableProperty]
    private bool notificationsEnabled;

    [ObservableProperty]
    private bool playSound;

    /// <summary>The folder actually being watched (saved override, else auto-detected), or null.</summary>
    [ObservableProperty]
    private string? effectiveLogsFolder;

    public TarkovViewModel(ITarkovMatchWatcher watcher, AppSettings settings, Action saveSettings)
    {
        _watcher = watcher;
        _settings = settings;
        _saveSettings = saveSettings;
        notificationsEnabled = settings.TarkovMatchNotificationsEnabled;
        playSound = settings.TarkovMatchPlaySound;
        ApplyWatcher();
    }

    /// <summary>Raised by the "Send test notification" button; App shows the real notification.</summary>
    public event EventHandler? TestNotificationRequested;

    public void SendTestNotification() => TestNotificationRequested?.Invoke(this, EventArgs.Empty);

    public string StatusText => EffectiveLogsFolder is null
        ? "Tarkov Logs folder not found. Use Browse to select it."
        : $"Watching: {EffectiveLogsFolder}";

    partial void OnEffectiveLogsFolderChanged(string? value) => OnPropertyChanged(nameof(StatusText));

    partial void OnNotificationsEnabledChanged(bool value)
    {
        _settings.TarkovMatchNotificationsEnabled = value;
        _saveSettings();
        ApplyWatcher();
    }

    partial void OnPlaySoundChanged(bool value)
    {
        _settings.TarkovMatchPlaySound = value;
        _saveSettings();
    }

    public void SetLogsFolder(string? folder)
    {
        _settings.TarkovLogsFolder = string.IsNullOrWhiteSpace(folder) ? null : folder;
        _saveSettings();
        ApplyWatcher();
    }

    private void ApplyWatcher()
    {
        string? saved = _settings.TarkovLogsFolder;
        EffectiveLogsFolder = saved is not null && Directory.Exists(saved)
            ? saved
            : TarkovMatchWatcher.TryDetectLogsFolder();

        _watcher.Start(NotificationsEnabled ? EffectiveLogsFolder : null);
    }
}

using System.Collections.ObjectModel;
using CenterConsole.Core.Models;
using CenterConsole.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterConsole.App.ViewModels;

public sealed partial class MicrophoneViewModel : ObservableObject
{
    private readonly IMicrophoneService _microphoneService;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;

    public ObservableCollection<MicDeviceInfo> Devices { get; } = new();

    [ObservableProperty]
    private MicDeviceInfo? selectedDevice;

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private HotkeyBinding muteHotkey;

    [ObservableProperty]
    private bool muteOnStartup;

    public MicrophoneViewModel(IMicrophoneService microphoneService, AppSettings settings, Action saveSettings)
    {
        _microphoneService = microphoneService;
        _settings = settings;
        _saveSettings = saveSettings;
        muteHotkey = settings.MicMuteHotkey;
        muteOnStartup = settings.MuteMicrophoneOnStartup;

        // Keeps IsMuted mirroring the live hardware state no matter what changes it - our own
        // ToggleMute, the hotkey, the tray menu, or the user muting/unmuting via the Windows Sound
        // Control Panel. Nothing here is ever read from a locally cached flag, which is what avoids
        // the classic reference-app bug where Windows remembers the mic as muted across a reboot but
        // the app's own toggle still thinks it's unmuted and its mute button stops doing anything.
        _microphoneService.MuteStateChanged += (_, muted) => IsMuted = muted;

        Refresh();

        if (muteOnStartup && SelectedDevice is not null && !IsMuted)
        {
            _microphoneService.SetMuted(SelectedDevice.Id, true);
            RefreshMuteState();
        }
    }

    public void Refresh()
    {
        Devices.Clear();
        foreach (var device in _microphoneService.GetCaptureDevices())
            Devices.Add(device);

        // Assigning the generated property (not the backing field) runs OnSelectedDeviceChanged,
        // which persists the choice and refreshes the watched device + mute state for us.
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == _settings.SelectedMicrophoneDeviceId)
            ?? Devices.FirstOrDefault(d => d.IsDefault)
            ?? Devices.FirstOrDefault();
    }

    partial void OnSelectedDeviceChanged(MicDeviceInfo? value)
    {
        _settings.SelectedMicrophoneDeviceId = value?.Id;
        _saveSettings();
        ApplySelectedDeviceWatch();
        RefreshMuteState();
    }

    private void ApplySelectedDeviceWatch()
    {
        _microphoneService.WatchDevice(SelectedDevice?.Id);
    }

    public void RefreshMuteState()
    {
        if (SelectedDevice is null)
            return;

        IsMuted = _microphoneService.IsMuted(SelectedDevice.Id) ?? false;
    }

    public void ToggleMute()
    {
        if (SelectedDevice is null)
            return;

        _microphoneService.ToggleMute(SelectedDevice.Id);
        RefreshMuteState();
    }

    public void ApplyNewHotkey(HotkeyBinding binding)
    {
        MuteHotkey = binding;
        _settings.MicMuteHotkey = binding;
        _saveSettings();
    }

    partial void OnMuteOnStartupChanged(bool value)
    {
        _settings.MuteMicrophoneOnStartup = value;
        _saveSettings();
    }
}

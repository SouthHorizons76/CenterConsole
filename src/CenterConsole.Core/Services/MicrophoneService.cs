using CenterConsole.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace CenterConsole.Core.Services;

public interface IMicrophoneService : IDisposable
{
    IReadOnlyList<MicDeviceInfo> GetCaptureDevices();
    bool? IsMuted(string deviceId);
    void SetMuted(string deviceId, bool muted);
    void ToggleMute(string deviceId);

    /// <summary>Raised when the mute state of the currently-watched device changes, including externally
    /// (e.g. via the Windows Sound Control Panel), so the tray icon can stay in sync.</summary>
    event EventHandler<bool>? MuteStateChanged;

    /// <summary>Switches which device's mute-state changes are reported via <see cref="MuteStateChanged"/>.</summary>
    void WatchDevice(string? deviceId);
}

public sealed class MicrophoneService : IMicrophoneService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _watchedDevice;
    private AudioEndpointVolumeNotificationDelegate? _notificationHandler;

    public event EventHandler<bool>? MuteStateChanged;

    public IReadOnlyList<MicDeviceInfo> GetCaptureDevices()
    {
        string? defaultId = TryGetDefaultCaptureDeviceId();

        var devices = new List<MicDeviceInfo>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            devices.Add(new MicDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId));
            device.Dispose();
        }
        return devices;
    }

    public bool? IsMuted(string deviceId)
    {
        using var device = TryGetDevice(deviceId);
        return device?.AudioEndpointVolume.Mute;
    }

    public void SetMuted(string deviceId, bool muted)
    {
        using var device = TryGetDevice(deviceId);
        if (device is not null)
            device.AudioEndpointVolume.Mute = muted;
    }

    public void ToggleMute(string deviceId)
    {
        using var device = TryGetDevice(deviceId);
        if (device is not null)
            device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
    }

    public void WatchDevice(string? deviceId)
    {
        if (_watchedDevice is not null && _notificationHandler is not null)
        {
            _watchedDevice.AudioEndpointVolume.OnVolumeNotification -= _notificationHandler;
            _watchedDevice.Dispose();
            _watchedDevice = null;
            _notificationHandler = null;
        }

        if (deviceId is null)
            return;

        _watchedDevice = TryGetDevice(deviceId);
        if (_watchedDevice is null)
            return;

        _notificationHandler = data => MuteStateChanged?.Invoke(this, data.Muted);
        _watchedDevice.AudioEndpointVolume.OnVolumeNotification += _notificationHandler;
    }

    private MMDevice? TryGetDevice(string deviceId)
    {
        try
        {
            return _enumerator.GetDevice(deviceId);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Device was unplugged/removed since last enumeration.
            return null;
        }
    }

    private string? TryGetDefaultCaptureDeviceId()
    {
        try
        {
            using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            return defaultDevice.ID;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // No default capture device configured.
            return null;
        }
    }

    public void Dispose()
    {
        WatchDevice(null);
        _enumerator.Dispose();
    }
}

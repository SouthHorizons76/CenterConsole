using System.Collections.ObjectModel;
using CenterConsole.Core.Models;
using CenterConsole.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterConsole.App.ViewModels;

public sealed partial class CameraDeviceRow : ObservableObject
{
    public CameraDeviceInfo Device { get; }

    [ObservableProperty]
    private bool isBlocked;

    public CameraDeviceRow(CameraDeviceInfo device, bool isBlocked)
    {
        Device = device;
        this.isBlocked = isBlocked;
    }
}

public sealed partial class CameraBlockViewModel : ObservableObject
{
    private readonly ICameraBlockService _cameraBlockService;
    private readonly IElevationService _elevationService;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;

    public ObservableCollection<CameraDeviceRow> Devices { get; } = new();

    [ObservableProperty]
    private bool isElevated;

    [ObservableProperty]
    private bool autoApplyOnStartup;

    public CameraBlockViewModel(
        ICameraBlockService cameraBlockService,
        IElevationService elevationService,
        AppSettings settings,
        Action saveSettings)
    {
        _cameraBlockService = cameraBlockService;
        _elevationService = elevationService;
        _settings = settings;
        _saveSettings = saveSettings;

        isElevated = elevationService.IsElevated;
        autoApplyOnStartup = settings.CameraBlockAutoApplyOnStartup;

        Refresh();
    }

    public void Refresh()
    {
        Devices.Clear();
        foreach (var device in _cameraBlockService.GetCameraDevices())
        {
            bool blocked = _settings.SelectedCameraDeviceInstanceIds.Contains(device.DeviceInstanceId)
                && !device.IsCurrentlyEnabled;
            var row = new CameraDeviceRow(device, blocked);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CameraDeviceRow.IsBlocked))
                    OnDeviceBlockToggled(row);
            };
            Devices.Add(row);
        }
    }

    /// <summary>Re-applies the configured blocks. Called on startup when AutoApplyOnStartup is set.</summary>
    public void ApplyConfiguredBlocks()
    {
        if (!IsElevated)
            return;

        foreach (string id in _settings.SelectedCameraDeviceInstanceIds)
            _cameraBlockService.Block(id);

        Refresh();
    }

    private void OnDeviceBlockToggled(CameraDeviceRow row)
    {
        if (!IsElevated)
        {
            row.IsBlocked = !row.IsBlocked; // revert: can't apply without elevation
            return;
        }

        bool success = row.IsBlocked
            ? _cameraBlockService.Block(row.Device.DeviceInstanceId)
            : _cameraBlockService.Unblock(row.Device.DeviceInstanceId);

        if (!success)
        {
            row.IsBlocked = !row.IsBlocked; // revert on failure
            return;
        }

        if (row.IsBlocked)
        {
            if (!_settings.SelectedCameraDeviceInstanceIds.Contains(row.Device.DeviceInstanceId))
                _settings.SelectedCameraDeviceInstanceIds.Add(row.Device.DeviceInstanceId);
        }
        else
        {
            _settings.SelectedCameraDeviceInstanceIds.Remove(row.Device.DeviceInstanceId);
        }

        _saveSettings();
    }

    partial void OnAutoApplyOnStartupChanged(bool value)
    {
        _settings.CameraBlockAutoApplyOnStartup = value;
        _saveSettings();
    }
}

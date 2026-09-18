using CenterConsole.Core.Models;
using CenterConsole.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterConsole.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IStartupService _startupService;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;

    public MicrophoneViewModel Microphone { get; }
    public CameraBlockViewModel CameraBlock { get; }
    public AppVolumeViewModel AppVolume { get; }

    [ObservableProperty]
    private bool runAtStartup;

    public MainViewModel(
        IMicrophoneService microphoneService,
        ICameraBlockService cameraBlockService,
        IAppVolumeService appVolumeService,
        IElevationService elevationService,
        IStartupService startupService,
        AppSettings settings,
        Action saveSettings)
    {
        _startupService = startupService;
        _settings = settings;
        _saveSettings = saveSettings;

        Microphone = new MicrophoneViewModel(microphoneService, settings, saveSettings);
        CameraBlock = new CameraBlockViewModel(cameraBlockService, elevationService, settings, saveSettings);
        AppVolume = new AppVolumeViewModel(appVolumeService, settings, saveSettings);

        runAtStartup = settings.RunAtStartup;
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        _settings.RunAtStartup = value;
        _saveSettings();
        _startupService.SetEnabled(value);
    }
}

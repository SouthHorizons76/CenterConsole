using CenterConsole.Core.Models;
using CenterConsole.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterConsole.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MicrophoneViewModel Microphone { get; }
    public CameraBlockViewModel CameraBlock { get; }
    public AppVolumeViewModel AppVolume { get; }

    public MainViewModel(
        IMicrophoneService microphoneService,
        ICameraBlockService cameraBlockService,
        IAppVolumeService appVolumeService,
        IElevationService elevationService,
        AppSettings settings,
        Action saveSettings)
    {
        Microphone = new MicrophoneViewModel(microphoneService, settings, saveSettings);
        CameraBlock = new CameraBlockViewModel(cameraBlockService, elevationService, settings, saveSettings);
        AppVolume = new AppVolumeViewModel(appVolumeService, settings, saveSettings);
    }
}

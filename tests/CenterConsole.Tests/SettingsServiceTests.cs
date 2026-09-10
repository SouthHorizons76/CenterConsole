using CenterConsole.Core.Models;
using CenterConsole.Core.Services;

namespace CenterConsole.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CenterConsoleTests_" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Load_WhenNoFileExists_ReturnsDefaults()
    {
        var service = new SettingsService(_tempDir);

        AppSettings settings = service.Load();

        Assert.Null(settings.SelectedMicrophoneDeviceId);
        Assert.True(settings.CameraBlockAutoApplyOnStartup);
        Assert.Equal(5, settings.AppVolumeStepPercent);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var service = new SettingsService(_tempDir);
        var original = new AppSettings
        {
            SelectedMicrophoneDeviceId = "{mic-guid}",
            SelectedTargetAppProcessName = "spotify",
            CameraBlockAutoApplyOnStartup = false,
            AppVolumeStepPercent = 10,
            RunAtStartup = true,
            MicMuteHotkey = new HotkeyBinding { Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift, VirtualKey = 0x4D },
        };
        original.SelectedCameraDeviceInstanceIds.Add(@"USB\VID_1234&PID_5678\0001");

        service.Save(original);
        AppSettings loaded = service.Load();

        Assert.Equal(original.SelectedMicrophoneDeviceId, loaded.SelectedMicrophoneDeviceId);
        Assert.Equal(original.SelectedTargetAppProcessName, loaded.SelectedTargetAppProcessName);
        Assert.Equal(original.CameraBlockAutoApplyOnStartup, loaded.CameraBlockAutoApplyOnStartup);
        Assert.Equal(original.AppVolumeStepPercent, loaded.AppVolumeStepPercent);
        Assert.Equal(original.RunAtStartup, loaded.RunAtStartup);
        Assert.Equal(original.SelectedCameraDeviceInstanceIds, loaded.SelectedCameraDeviceInstanceIds);
        Assert.Equal(original.MicMuteHotkey.Modifiers, loaded.MicMuteHotkey.Modifiers);
        Assert.Equal(original.MicMuteHotkey.VirtualKey, loaded.MicMuteHotkey.VirtualKey);
    }

    [Fact]
    public void Load_WhenFileIsCorrupt_FallsBackToDefaultsWithoutThrowing()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "{ this is not valid json ");

        var service = new SettingsService(_tempDir);

        AppSettings settings = service.Load();

        Assert.NotNull(settings);
        Assert.Null(settings.SelectedMicrophoneDeviceId);
    }

    [Fact]
    public void Save_OverwritesExistingFileAtomically()
    {
        var service = new SettingsService(_tempDir);
        service.Save(new AppSettings { AppVolumeStepPercent = 1 });
        service.Save(new AppSettings { AppVolumeStepPercent = 2 });

        AppSettings loaded = service.Load();

        Assert.Equal(2, loaded.AppVolumeStepPercent);
        Assert.False(File.Exists(Path.Combine(_tempDir, "settings.json.tmp")));
    }
}

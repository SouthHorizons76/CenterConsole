namespace CenterConsole.Core.Models;

/// <summary>Persisted user configuration, round-tripped to %AppData%\CenterConsole\settings.json.</summary>
public sealed class AppSettings
{
    public string? SelectedMicrophoneDeviceId { get; set; }
    public HotkeyBinding MicMuteHotkey { get; set; } = new()
    {
        Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
        VirtualKey = 0x4D, // 'M'
    };

    public List<string> SelectedCameraDeviceInstanceIds { get; set; } = new();
    public bool CameraBlockAutoApplyOnStartup { get; set; } = true;

    public string? SelectedTargetAppProcessName { get; set; }
    public HotkeyBinding AppVolumeUpHotkey { get; set; } = new()
    {
        Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
        VirtualKey = 0x26, // VK_UP
    };
    public HotkeyBinding AppVolumeDownHotkey { get; set; } = new()
    {
        Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
        VirtualKey = 0x28, // VK_DOWN
    };
    public int AppVolumeStepPercent { get; set; } = 5;

    public bool RunAtStartup { get; set; }
}

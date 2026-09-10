namespace CenterConsole.Core.Models;

/// <summary>A running application's audio session, as surfaced to the Per-App Volume tab.</summary>
public sealed record AppVolumeTarget(
    int ProcessId,
    string ProcessName,
    string DisplayName,
    float Volume,
    bool IsMuted);

namespace CenterConsole.Core.Models;

/// <summary>
/// A camera/imaging device node discovered via SetupAPI. DeviceInstanceId (e.g. "USB\VID_...\...")
/// is the stable identifier to persist. Friendly names are not unique and can change.
/// </summary>
public sealed record CameraDeviceInfo(
    string DeviceInstanceId,
    string FriendlyName,
    string DeviceClassName,
    bool IsCurrentlyEnabled);

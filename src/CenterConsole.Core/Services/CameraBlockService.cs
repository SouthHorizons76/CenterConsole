using CenterConsole.Core.Interop;
using CenterConsole.Core.Models;

namespace CenterConsole.Core.Services;

public interface ICameraBlockService
{
    /// <summary>Enumerates camera/imaging devices present on the system (GUID_DEVCLASS_CAMERA + GUID_DEVCLASS_IMAGE).</summary>
    IReadOnlyList<CameraDeviceInfo> GetCameraDevices();

    /// <summary>Disables (blocks) the device at the driver level — the same mechanism Device Manager uses.</summary>
    bool Block(string deviceInstanceId);

    /// <summary>Re-enables a previously blocked device.</summary>
    bool Unblock(string deviceInstanceId);

    bool IsBlocked(string deviceInstanceId);
}

/// <summary>
/// Disables/enables camera hardware at the device-node level via SetupAPI, so the block applies to
/// every consumer (Win32 apps via DirectShow/Media Foundation and UWP apps via Frame Server alike) —
/// unlike the Windows privacy-toggle registry key, which only restricts UWP/Store apps.
/// Requires the process to be running elevated; SetupDiCallClassInstaller fails otherwise.
/// </summary>
public sealed class CameraBlockService : ICameraBlockService
{
    private static readonly Guid[] CameraClassGuids = { DeviceClassGuids.Camera, DeviceClassGuids.Image };

    public IReadOnlyList<CameraDeviceInfo> GetCameraDevices()
    {
        var results = new List<CameraDeviceInfo>();

        foreach (var classGuid in CameraClassGuids)
        {
            Guid guid = classGuid;
            IntPtr deviceInfoSet = SetupApi.SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, SetupApi.DIGCF_PRESENT);
            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                continue;

            try
            {
                int index = 0;
                while (true)
                {
                    var devInfoData = SP_DEVINFO_DATA.Create();
                    if (!SetupApi.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref devInfoData))
                        break;
                    index++;

                    string? instanceId = SetupApi.GetDeviceInstanceId(deviceInfoSet, ref devInfoData);
                    if (instanceId is null)
                        continue;

                    string friendlyName =
                        SetupApi.GetStringProperty(deviceInfoSet, ref devInfoData, SetupApi.SPDRP_FRIENDLYNAME)
                        ?? SetupApi.GetStringProperty(deviceInfoSet, ref devInfoData, SetupApi.SPDRP_DEVICEDESC)
                        ?? instanceId;

                    string className = classGuid == DeviceClassGuids.Camera ? "Camera" : "Imaging Device";

                    results.Add(new CameraDeviceInfo(
                        instanceId, friendlyName, className, CfgMgr32.IsDeviceEnabled(instanceId)));
                }
            }
            finally
            {
                SetupApi.SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }

        return results;
    }

    public bool Block(string deviceInstanceId) => SetEnabled(deviceInstanceId, enable: false);

    public bool Unblock(string deviceInstanceId) => SetEnabled(deviceInstanceId, enable: true);

    public bool IsBlocked(string deviceInstanceId) => !CfgMgr32.IsDeviceEnabled(deviceInstanceId);

    private static bool SetEnabled(string deviceInstanceId, bool enable)
    {
        foreach (var classGuid in CameraClassGuids)
        {
            Guid guid = classGuid;
            IntPtr deviceInfoSet = SetupApi.SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, SetupApi.DIGCF_PRESENT);
            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                continue;

            try
            {
                int index = 0;
                while (true)
                {
                    var devInfoData = SP_DEVINFO_DATA.Create();
                    if (!SetupApi.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref devInfoData))
                        break;
                    index++;

                    string? instanceId = SetupApi.GetDeviceInstanceId(deviceInfoSet, ref devInfoData);
                    if (!string.Equals(instanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return SetupApi.SetDeviceEnabled(deviceInfoSet, ref devInfoData, enable);
                }
            }
            finally
            {
                SetupApi.SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }

        return false; // Device instance ID not found among present camera/image devices.
    }
}

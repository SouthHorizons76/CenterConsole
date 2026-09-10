using System.Runtime.InteropServices;

namespace CenterConsole.Core.Interop;

/// <summary>
/// Thin wrapper over cfgmgr32.dll, used only to read live device node status
/// (SetupApi.SetDeviceEnabled is the path used to actually change it — same as Device Manager).
/// </summary>
internal static class CfgMgr32
{
    private const int CR_SUCCESS = 0;
    private const int CM_LOCATE_DEVNODE_NORMAL = 0;
    private const int DN_STARTED = 0x00000008;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out int pdnDevInst, string pDeviceId, int ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out int pulStatus, out int pulProblemNumber, int dnDevInst, int ulFlags);

    /// <summary>Returns true if the device (by instance ID) is currently enabled/started; false if disabled or not found.</summary>
    public static bool IsDeviceEnabled(string deviceInstanceId)
    {
        if (CM_Locate_DevNodeW(out int devInst, deviceInstanceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
            return false;

        if (CM_Get_DevNode_Status(out int status, out _, devInst, 0) != CR_SUCCESS)
            return false;

        return (status & DN_STARTED) != 0;
    }
}

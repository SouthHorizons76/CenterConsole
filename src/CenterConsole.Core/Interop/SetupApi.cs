using System.Runtime.InteropServices;
using System.Text;

namespace CenterConsole.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct SP_DEVINFO_DATA
{
    public int cbSize;
    public Guid ClassGuid;
    public int DevInst;
    public IntPtr Reserved;

    public static SP_DEVINFO_DATA Create() => new()
    {
        cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>(),
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct SP_CLASSINSTALL_HEADER
{
    public int cbSize;
    public int InstallFunction;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SP_PROPCHANGE_PARAMS
{
    public SP_CLASSINSTALL_HEADER ClassInstallHeader;
    public int StateChange;
    public int Scope;
    public int HwProfile;
}

internal static class SetupApi
{
    public const int DIGCF_PRESENT = 0x02;

    public const int DIF_PROPERTYCHANGE = 0x12;

    public const int DICS_ENABLE = 1;
    public const int DICS_DISABLE = 2;

    public const int DICS_FLAG_GLOBAL = 1;

    public const int SPDRP_DEVICEDESC = 0x00000000;
    public const int SPDRP_FRIENDLYNAME = 0x0000000C;

    private const int SP_CLASSINSTALL_HEADER_INSTALLFUNCTION = 0x12; // DIF_PROPERTYCHANGE, reused as header's InstallFunction

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid, string? enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, int memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, int property,
        out int propertyRegDataType, byte[]? propertyBuffer, int propertyBufferSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiSetClassInstallParamsW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        ref SP_PROPCHANGE_PARAMS classInstallParams, int classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiCallClassInstaller(
        int installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    /// <summary>Reads a string registry property (friendly name / device description) for a device node.</summary>
    public static string? GetStringProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfoData, int property)
    {
        SetupDiGetDeviceRegistryPropertyW(deviceInfoSet, ref devInfoData, property,
            out _, null, 0, out int requiredSize);
        if (requiredSize == 0)
            return null;

        var buffer = new byte[requiredSize];
        if (!SetupDiGetDeviceRegistryPropertyW(deviceInfoSet, ref devInfoData, property,
                out _, buffer, buffer.Length, out _))
            return null;

        // REG_SZ payload is UTF-16 with a trailing null terminator.
        string value = Encoding.Unicode.GetString(buffer);
        int nullIndex = value.IndexOf('\0');
        return nullIndex >= 0 ? value[..nullIndex] : value;
    }

    public static string? GetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfoData)
    {
        var sb = new StringBuilder(512);
        return SetupDiGetDeviceInstanceIdW(deviceInfoSet, ref devInfoData, sb, sb.Capacity, out _)
            ? sb.ToString()
            : null;
    }

    /// <summary>Sets the enabled/disabled state (DICS_ENABLE/DICS_DISABLE) for a device node, globally.</summary>
    public static bool SetDeviceEnabled(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfoData, bool enable)
    {
        var propChangeParams = new SP_PROPCHANGE_PARAMS
        {
            ClassInstallHeader = new SP_CLASSINSTALL_HEADER
            {
                cbSize = Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                InstallFunction = SP_CLASSINSTALL_HEADER_INSTALLFUNCTION,
            },
            StateChange = enable ? DICS_ENABLE : DICS_DISABLE,
            Scope = DICS_FLAG_GLOBAL,
            HwProfile = 0,
        };

        if (!SetupDiSetClassInstallParamsW(deviceInfoSet, ref devInfoData, ref propChangeParams,
                Marshal.SizeOf<SP_PROPCHANGE_PARAMS>()))
            return false;

        return SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, deviceInfoSet, ref devInfoData);
    }
}

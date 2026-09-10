namespace CenterConsole.Core.Interop;

/// <summary>Setup class GUIDs for the device classes CameraBlockService enumerates.</summary>
internal static class DeviceClassGuids
{
    /// <summary>Modern camera class (Frame Server-based cameras, most built-in laptop webcams on Win10/11).</summary>
    public static readonly Guid Camera = new("ca3e7ab9-b4c3-4ae6-8251-579ef933890f");

    /// <summary>Legacy imaging device class (most external USB UVC webcams).</summary>
    public static readonly Guid Image = new("6bdd1fc6-810f-11d0-bec7-08002be2092f");
}

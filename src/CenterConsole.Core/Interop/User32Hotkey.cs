using System.Runtime.InteropServices;
using System.Text;

namespace CenterConsole.Core.Interop;

internal static class User32Hotkey
{
    public const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern int GetKeyNameText(int lParam, StringBuilder lpString, int cchSize);

    private const uint MAPVK_VK_TO_VSC = 0;

    /// <summary>
    /// Resolves a virtual-key code to a human-readable name (e.g. "M", "F5", "Up") using the
    /// keyboard layout's scan-code table, so it works correctly for non-alphanumeric keys.
    /// </summary>
    public static string VirtualKeyToName(uint virtualKey)
    {
        uint scanCode = MapVirtualKey(virtualKey, MAPVK_VK_TO_VSC);
        if (scanCode == 0)
            return $"VK_0x{virtualKey:X2}";

        // Bit 24 of lParam marks "extended" keys (arrows, Home/End/Insert/Delete, numpad Enter,
        // right-hand Ctrl/Alt). Without it, GetKeyNameText returns the wrong label for these.
        bool isExtended = virtualKey is 0x21 or 0x22 or 0x23 or 0x24 // PgUp/PgDn/End/Home
            or 0x25 or 0x26 or 0x27 or 0x28                         // Left/Up/Right/Down
            or 0x2D or 0x2E or 0x2F                                 // Insert/Delete/Help
            or 0x90;                                                // NumLock

        int lParam = (int)(scanCode << 16);
        if (isExtended)
            lParam |= 1 << 24;

        var sb = new StringBuilder(64);
        int length = GetKeyNameText(lParam, sb, sb.Capacity);
        return length > 0 ? sb.ToString() : $"VK_0x{virtualKey:X2}";
    }
}

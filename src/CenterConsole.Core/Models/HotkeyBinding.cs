using CenterConsole.Core.Interop;

namespace CenterConsole.Core.Models;

/// <summary>Win32 MOD_* modifier flags, combined with bitwise OR.</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0x0000,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

/// <summary>
/// A global hotkey binding. Stores raw Win32 modifier flags + virtual-key code rather than a
/// parsed string, so RegisterHotKey can consume it directly and there's no ambiguity on reload.
/// </summary>
public sealed class HotkeyBinding
{
    public HotkeyModifiers Modifiers { get; set; }
    public uint VirtualKey { get; set; }

    /// <summary>True if this binding has a key assigned (VirtualKey == 0 means "unbound").</summary>
    public bool IsAssigned => VirtualKey != 0;

    public string DisplayString
    {
        get
        {
            if (!IsAssigned)
                return "(none)";

            var parts = new List<string>();
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
            parts.Add(User32Hotkey.VirtualKeyToName(VirtualKey));
            return string.Join(" + ", parts);
        }
    }

    public override string ToString() => DisplayString;
}

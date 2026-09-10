using CenterConsole.Core.Models;

namespace CenterConsole.Tests;

public sealed class HotkeyBindingTests
{
    [Fact]
    public void IsAssigned_WhenVirtualKeyIsZero_IsFalse()
    {
        var binding = new HotkeyBinding { VirtualKey = 0 };

        Assert.False(binding.IsAssigned);
        Assert.Equal("(none)", binding.DisplayString);
    }

    [Fact]
    public void IsAssigned_WhenVirtualKeyIsSet_IsTrue()
    {
        var binding = new HotkeyBinding { VirtualKey = 0x4D };

        Assert.True(binding.IsAssigned);
    }

    [Fact]
    public void DisplayString_IncludesAllSetModifiersInOrder()
    {
        var binding = new HotkeyBinding
        {
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win,
            VirtualKey = 0x4D, // 'M'
        };

        string display = binding.DisplayString;

        Assert.StartsWith("Ctrl + Alt + Shift + Win + ", display);
    }

    [Fact]
    public void DisplayString_WithNoModifiers_OmitsModifierPrefix()
    {
        var binding = new HotkeyBinding { Modifiers = HotkeyModifiers.None, VirtualKey = 0x4D };

        string display = binding.DisplayString;

        Assert.DoesNotContain("+", display);
    }
}

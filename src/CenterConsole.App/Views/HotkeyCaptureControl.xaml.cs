using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CenterConsole.Core.Models;

namespace CenterConsole.App.Views;

public partial class HotkeyCaptureControl : UserControl
{
    public static readonly DependencyProperty BindingValueProperty = DependencyProperty.Register(
        nameof(BindingValue),
        typeof(HotkeyBinding),
        typeof(HotkeyCaptureControl),
        new PropertyMetadata(null, OnBindingValueChanged));

    public HotkeyBinding? BindingValue
    {
        get => (HotkeyBinding?)GetValue(BindingValueProperty);
        set => SetValue(BindingValueProperty, value);
    }

    /// <summary>Raised when the user finishes pressing a new key combination. The caller is
    /// responsible for attempting registration and, on success, writing the result back into
    /// <see cref="BindingValue"/> (normally via a two-way ViewModel binding).</summary>
    public event EventHandler<HotkeyBinding>? HotkeyCaptured;

    private bool _isCapturing;

    public HotkeyCaptureControl()
    {
        InitializeComponent();
        UpdateButtonText();
    }

    private static void OnBindingValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((HotkeyCaptureControl)d).UpdateButtonText();
    }

    private void UpdateButtonText()
    {
        if (_isCapturing)
            return;

        CaptureButton.Content = BindingValue?.DisplayString ?? "(none)";
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        _isCapturing = true;
        CaptureButton.Content = "Press a key combo (Esc = none)";
        CaptureButton.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isCapturing)
            return;

        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierOnly(key))
            return; // wait for a real key while modifiers are held

        if (key == Key.Escape)
        {
            // Esc unbinds: an unassigned binding (VirtualKey 0) tells the caller to drop the hotkey.
            _isCapturing = false;
            UpdateButtonText();
            HotkeyCaptured?.Invoke(this, new HotkeyBinding());
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;

        var binding = new HotkeyBinding
        {
            Modifiers = modifiers,
            VirtualKey = (uint)KeyInterop.VirtualKeyFromKey(key),
        };

        _isCapturing = false;
        UpdateButtonText();
        HotkeyCaptured?.Invoke(this, binding);
    }

    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Clicking elsewhere cancels a capture in progress, leaving the binding unchanged.
        if (!_isCapturing)
            return;

        _isCapturing = false;
        UpdateButtonText();
    }

    private static bool IsModifierOnly(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;
}

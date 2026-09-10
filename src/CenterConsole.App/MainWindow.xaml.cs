using System.Windows;
using System.Windows.Interop;
using CenterConsole.App.ViewModels;
using CenterConsole.Core.Models;
using CenterConsole.Core.Services;

namespace CenterConsole.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IHotkeyService _hotkeyService;

    public bool IsExiting { get; set; }

    public MainWindow(MainViewModel viewModel, IHotkeyService hotkeyService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _hotkeyService = hotkeyService;
        DataContext = _viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // PresentationSource.FromVisual(this) can still be null here when the handle was created via
        // WindowInteropHelper.EnsureHandle() (as opposed to Show()). Going via the raw HWND is reliable
        // in both cases.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        HwndSource source = HwndSource.FromHwnd(hwnd)!;
        source.AddHook(WndProc);
        _hotkeyService.AttachToWindow(hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeyService.ProcessWindowMessage(msg, wParam, lParam))
            handled = true;

        return IntPtr.Zero;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (IsExiting)
            return;

        // Hide to tray instead of exiting. The HWND (and its registered hotkeys) stays alive.
        e.Cancel = true;
        Hide();
    }

    private void OnMuteCheckboxClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Microphone.ToggleMute();
    }

    private void OnMicMuteHotkeyCaptured(object? sender, HotkeyBinding binding)
    {
        TryApplyHotkey("MicMute", binding, _viewModel.Microphone.ApplyNewHotkey);
    }

    private void OnVolumeUpHotkeyCaptured(object? sender, HotkeyBinding binding)
    {
        TryApplyHotkey("VolumeUp", binding, _viewModel.AppVolume.ApplyNewVolumeUpHotkey);
    }

    private void OnVolumeDownHotkeyCaptured(object? sender, HotkeyBinding binding)
    {
        TryApplyHotkey("VolumeDown", binding, _viewModel.AppVolume.ApplyNewVolumeDownHotkey);
    }

    private void TryApplyHotkey(string actionKey, HotkeyBinding binding, Action<HotkeyBinding> apply)
    {
        if (_hotkeyService.TryRegister(actionKey, binding))
        {
            apply(binding);
        }
        else
        {
            MessageBox.Show(
                $"'{binding.DisplayString}' is already in use by another application (or CenterConsole itself). Choose a different combination.",
                "Hotkey unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

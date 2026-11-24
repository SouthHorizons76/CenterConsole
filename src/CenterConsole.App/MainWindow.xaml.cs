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

        Version? version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionLabel.Text = version is null ? string.Empty : $"v{version.ToString(3)}";
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

    private void OnBrowseTarkovLogsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Escape from Tarkov's Logs folder (contains the log_* folders)",
            InitialDirectory = _viewModel.Tarkov.EffectiveLogsFolder ?? string.Empty,
        };
        if (dialog.ShowDialog(this) == true)
            _viewModel.Tarkov.SetLogsFolder(dialog.FolderName);
    }

    private void OnTestTarkovNotificationClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Tarkov.SendTestNotification();
    }

    private void OnMicMuteHotkeyCaptured(object? sender, HotkeyBinding binding)
    {
        TryApplyHotkey("MicMute", binding, _viewModel.Microphone.ApplyNewHotkey);
    }

    private void OnTarkovAudioHotkeyCaptured(object? sender, HotkeyBinding binding)
    {
        TryApplyHotkey("TarkovAudio", binding, _viewModel.TarkovAudio.ApplyNewHotkey);
    }

    private void OnInstallTarkovAudioClick(object sender, RoutedEventArgs e)
    {
        _viewModel.TarkovAudio.Install();
    }

    private void OnRecheckTarkovAudioClick(object sender, RoutedEventArgs e)
    {
        _viewModel.TarkovAudio.Recheck();
    }

    private async void OnRestartAudioEngineClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "This restarts the Windows Audio service so every app picks up the current compressor plugin.\n\n" +
            "All sound stops for a moment, and games that are running may lose sound until you restart them. Continue?",
            "Restart audio engine", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK)
            await _viewModel.TarkovAudio.RestartAudioEngineAsync();
    }

    private void OnGetEqualizerApoClick(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://sourceforge.net/projects/equalizerapo/") { UseShellExecute = true });
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
        if (!binding.IsAssigned)
        {
            // Esc in the capture control: drop the hotkey entirely.
            _hotkeyService.Unregister(actionKey);
            apply(binding);
            return;
        }

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

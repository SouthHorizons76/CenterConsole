using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CenterConsole.App.ViewModels;
using CenterConsole.Core.Models;
using CenterConsole.Core.Services;
using H.NotifyIcon;

namespace CenterConsole.App;

public partial class App : Application
{
    private ISettingsService _settingsService = null!;
    private AppSettings _settings = null!;
    private IMicrophoneService _microphoneService = null!;
    private ICameraBlockService _cameraBlockService = null!;
    private IAppVolumeService _appVolumeService = null!;
    private IElevationService _elevationService = null!;
    private IHotkeyService _hotkeyService = null!;
    private MainWindow _mainWindow = null!;
    private MainViewModel _mainViewModel = null!;
    private TaskbarIcon _taskbarIcon = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Startup runs before the Dispatcher's message loop is pumping, so an exception here would
        // otherwise bubble past DispatcherUnhandledException straight to an invisible process exit.
        // This is the only way to guarantee the user sees why the app died instead of silent nothing.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowFatalError(args.ExceptionObject as Exception, "Unhandled exception");
        DispatcherUnhandledException += (_, args) =>
        {
            ShowFatalError(args.Exception, "Unhandled UI-thread exception");
            args.Handled = true;
        };

        try
        {
            StartServicesAndUi();
        }
        catch (Exception ex)
        {
            ShowFatalError(ex, "Startup failed");
            Shutdown(-1);
        }
    }

    private static void ShowFatalError(Exception? ex, string title)
    {
        MessageBox.Show(
            ex?.ToString() ?? "(no exception details available)",
            $"CenterConsole: {title}",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void StartServicesAndUi()
    {
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();

        _elevationService = new ElevationService();
        _microphoneService = new MicrophoneService();
        _cameraBlockService = new CameraBlockService();
        _appVolumeService = new AppVolumeService();
        _hotkeyService = new HotkeyService();

        _mainViewModel = new MainViewModel(
            _microphoneService, _cameraBlockService, _appVolumeService, _elevationService, _settings, SaveSettings);

        _mainWindow = new MainWindow(_mainViewModel, _hotkeyService);
        // Forces HWND creation (and MainWindow.OnSourceInitialized) without calling Show(). The app
        // starts tray-only, but hotkeys need a real window handle to register against.
        new WindowInteropHelper(_mainWindow).EnsureHandle();

        RegisterHotkeys();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _microphoneService.MuteStateChanged += (_, muted) => UpdateTrayIcon(muted);

        if (_settings.CameraBlockAutoApplyOnStartup)
            _mainViewModel.CameraBlock.ApplyConfiguredBlocks();

        CreateTaskbarIcon();
        UpdateTrayIcon(_mainViewModel.Microphone.IsMuted);

        // The icon is never parented to a shown Window, so its normal Loaded-triggered registration
        // never fires. ForceCreate() is H.NotifyIcon's documented escape hatch for windowless apps.
        _taskbarIcon.ForceCreate();
    }

    private void RegisterHotkeys()
    {
        _hotkeyService.TryRegister("MicMute", _settings.MicMuteHotkey);
        _hotkeyService.TryRegister("VolumeUp", _settings.AppVolumeUpHotkey);
        _hotkeyService.TryRegister("VolumeDown", _settings.AppVolumeDownHotkey);
    }

    private void OnHotkeyPressed(object? sender, string actionKey)
    {
        switch (actionKey)
        {
            case "MicMute":
                _mainViewModel.Microphone.ToggleMute();
                UpdateTrayIcon(_mainViewModel.Microphone.IsMuted);
                break;

            case "VolumeUp":
                _mainViewModel.AppVolume.AdjustTargetVolume(_settings.AppVolumeStepPercent / 100f);
                break;

            case "VolumeDown":
                _mainViewModel.AppVolume.AdjustTargetVolume(-_settings.AppVolumeStepPercent / 100f);
                break;
        }
    }

    private void SaveSettings() => _settingsService.Save(_settings);

    private void CreateTaskbarIcon()
    {
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "CenterConsole",
        };

        var muteItem = new MenuItem { Header = "Mute Microphone", IsCheckable = true };
        muteItem.Click += (_, _) =>
        {
            _mainViewModel.Microphone.ToggleMute();
            UpdateTrayIcon(_mainViewModel.Microphone.IsMuted);
        };

        var settingsItem = new MenuItem { Header = "Open Settings" };
        settingsItem.Click += (_, _) => ShowMainWindow();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApplication();

        var contextMenu = new ContextMenu();
        contextMenu.Opened += (_, _) => muteItem.IsChecked = _mainViewModel.Microphone.IsMuted;
        contextMenu.Items.Add(muteItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new Separator());

        foreach (var row in _mainViewModel.CameraBlock.Devices)
        {
            var camItem = new MenuItem
            {
                Header = $"Block Camera: {row.Device.FriendlyName}",
                IsCheckable = true,
                IsChecked = row.IsBlocked,
            };
            camItem.Click += (_, _) => row.IsBlocked = !row.IsBlocked;
            contextMenu.Items.Add(camItem);
        }

        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        _taskbarIcon.ContextMenu = contextMenu;
    }

    private void UpdateTrayIcon(bool muted)
    {
        _taskbarIcon.IconSource = new GeneratedIconSource
        {
            Text = muted ? "🔇" : "🎤", // muted-speaker vs studio-microphone emoji
            FontSize = 96,
        };
        _taskbarIcon.ToolTipText = muted ? "CenterConsole: Microphone Muted" : "CenterConsole: Microphone Live";
    }

    private void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        _mainWindow.IsExiting = true;
        _hotkeyService.UnregisterAll();
        SaveSettings();
        _taskbarIcon.Dispose();
        _microphoneService.Dispose();
        _appVolumeService.Dispose();
        _mainWindow.Close();
        Shutdown();
    }
}

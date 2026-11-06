using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CenterConsole.App.ViewModels;
using CenterConsole.Core.Models;
using CenterConsole.Core.Services;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace CenterConsole.App;

public partial class App : Application
{
    private ISettingsService _settingsService = null!;
    private AppSettings _settings = null!;
    private IMicrophoneService _microphoneService = null!;
    private ICameraBlockService _cameraBlockService = null!;
    private IAppVolumeService _appVolumeService = null!;
    private IElevationService _elevationService = null!;
    private IStartupService _startupService = null!;
    private IHotkeyService _hotkeyService = null!;
    private ITarkovMatchWatcher _tarkovMatchWatcher = null!;
    private DateTime _lastMatchNotificationUtc = DateTime.MinValue;
    private SoundPlayer? _matchFoundSound;
    private MainWindow _mainWindow = null!;
    private MainViewModel _mainViewModel = null!;
    private TaskbarIcon _taskbarIcon = null!;
    private System.Drawing.Icon? _currentTrayIcon;

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
        _startupService = new StartupService();
        _hotkeyService = new HotkeyService();
        _tarkovMatchWatcher = new TarkovMatchWatcher();

        _mainViewModel = new MainViewModel(
            _microphoneService, _cameraBlockService, _appVolumeService, _elevationService, _startupService,
            _tarkovMatchWatcher, _settings, SaveSettings);

        // Reconciles the scheduled task with the persisted setting on every launch (e.g. if the task
        // was removed externally, or this is a fresh settings.json from a copied install).
        _startupService.SetEnabled(_settings.RunAtStartup);

        _mainWindow = new MainWindow(_mainViewModel, _hotkeyService);
        // Forces HWND creation (and MainWindow.OnSourceInitialized) without calling Show(). The app
        // starts tray-only, but hotkeys need a real window handle to register against.
        new WindowInteropHelper(_mainWindow).EnsureHandle();

        RegisterHotkeys();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        // Must exist before anything below can raise BlockStateChanged/IsMuted changes - both are
        // wired straight to RefreshIcon(), which touches _taskbarIcon.
        CreateTaskbarIcon();

        // Every mute path (settings checkbox, hotkey, tray menu, Windows Sound panel) ends up updating
        // Microphone.IsMuted on the UI thread, so this is the one place the icon needs to follow it.
        _mainViewModel.Microphone.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MicrophoneViewModel.IsMuted))
                RefreshIcon();
        };
        _mainViewModel.CameraBlock.BlockStateChanged += (_, _) => RefreshIcon();
        _tarkovMatchWatcher.MatchFound += (_, _) => Dispatcher.BeginInvoke(() => NotifyTarkovMatchFound(isTest: false));
        _mainViewModel.Tarkov.TestNotificationRequested += (_, _) => NotifyTarkovMatchFound(isTest: true);

        if (_settings.CameraBlockAutoApplyOnStartup)
            _mainViewModel.CameraBlock.ApplyConfiguredBlocks();

        RefreshIcon();

        // The icon is never parented to a shown Window, so its normal Loaded-triggered registration
        // never fires. ForceCreate() is H.NotifyIcon's documented escape hatch for windowless apps.
        ForceCreateTaskbarIconWithRetry();
    }

    private void ForceCreateTaskbarIconWithRetry()
    {
        // Launched via the logon scheduled task, CenterConsole can start running before Explorer has
        // finished creating the shell's tray notification window - ForceCreate() throws
        // InvalidOperationException in that narrow window. Retrying briefly rides out the race instead
        // of crashing before the app ever gets a chance to show its icon.
        const int maxAttempts = 20;
        const int retryDelayMs = 500;

        for (int attempt = 1; attempt < maxAttempts; attempt++)
        {
            try
            {
                _taskbarIcon.ForceCreate();
                return;
            }
            catch (InvalidOperationException)
            {
                Thread.Sleep(retryDelayMs);
            }
        }

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

        // Left double-click on the tray icon opens Settings, mirroring the convention used by most
        // tray apps (single click just shows the context/status; right-click already opens the menu).
        _taskbarIcon.TrayLeftMouseDoubleClick += (_, _) => ShowMainWindow();
    }

    private void RefreshIcon()
    {
        bool muted = _mainViewModel.Microphone.IsMuted;
        bool cameraBlocked = _mainViewModel.CameraBlock.Devices.Any(d => d.IsBlocked);

        var icons = AppIconRenderer.Render(muted, cameraBlocked);
        _mainWindow.Icon = icons.WindowIcon;
        _taskbarIcon.Icon = icons.TrayIcon;

        // TaskbarIcon.Icon doesn't take ownership of the handle, so the previous one (a native GDI
        // resource) is only safe to dispose once the new one has taken its place.
        _currentTrayIcon?.Dispose();
        _currentTrayIcon = icons.TrayIcon;

        _taskbarIcon.ToolTipText =
            $"CenterConsole: Microphone {(muted ? "Muted" : "Live")}, Camera {(cameraBlocked ? "Blocked" : "Enabled")}";
    }

    private void NotifyTarkovMatchFound(bool isTest)
    {
        // MatchingCompleted is written once per match, but guard against a burst of duplicates anyway.
        // Test clicks bypass the guard (and don't arm it) so they always fire.
        if (!isTest)
        {
            if (DateTime.UtcNow - _lastMatchNotificationUtc < TimeSpan.FromSeconds(15))
                return;
            _lastMatchNotificationUtc = DateTime.UtcNow;
        }

        // The balloon's own sound is skipped (Windows silences it under Focus Assist's gaming rule, which
        // is exactly when this matters); a system sound played directly isn't subject to that.
        if (_settings.TarkovMatchPlaySound)
            PlayMatchFoundSound();

        // respectQuietTime: false so the toast still shows while a fullscreen game is focused.
        _taskbarIcon.ShowNotification(
            "Match found", isTest ? "Test notification from CenterConsole." : "Escape from Tarkov found a game.",
            NotificationIcon.Info, sound: false, respectQuietTime: false);
    }

    private void PlayMatchFoundSound()
    {
        string wav = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify Messaging.wav");

        if (!File.Exists(wav))
        {
            SystemSounds.Exclamation.Play();
            return;
        }

        // Kept in a field: SoundPlayer.Play() is asynchronous, and a collected player cuts the sound off.
        _matchFoundSound ??= new SoundPlayer(wav);
        _matchFoundSound.Play();
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
        _currentTrayIcon?.Dispose();
        _microphoneService.Dispose();
        _appVolumeService.Dispose();
        _tarkovMatchWatcher.Dispose();
        _matchFoundSound?.Dispose();
        _mainWindow.Close();
        Shutdown();
    }
}

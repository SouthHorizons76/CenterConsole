using System.IO;
using CenterConsole.Core.Models;
using CenterConsole.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterConsole.App.ViewModels;

/// <summary>
/// On/off switch and tuning for the TarkovAudio plugin. The plugin itself lives inside Equalizer APO on
/// the normal output device; this only decides whether it should be active and pushes that (plus the
/// slider values) to the registry, where the plugin picks it up within ~100 ms.
/// </summary>
public sealed partial class TarkovAudioViewModel : ObservableObject
{
    private readonly ITarkovAudioService _service;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;

    public static string BundledPluginPath => Path.Combine(AppContext.BaseDirectory, "TarkovAudio.dll");

    [ObservableProperty] private bool enabled;
    [ObservableProperty] private int boostDb;
    [ObservableProperty] private int loudThresholdDb;
    [ObservableProperty] private int presenceDb;
    [ObservableProperty] private HotkeyBinding hotkey;
    [ObservableProperty] private TarkovAudioSetupState setupState;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private string? lastError;
    [ObservableProperty] private string? notice;
    [ObservableProperty] private bool restartPending;
    [ObservableProperty] private bool isRestartingAudio;

    public bool IsElevated { get; }

    public TarkovAudioViewModel(
        ITarkovAudioService service, IElevationService elevationService, AppSettings settings, Action saveSettings)
    {
        _service = service;
        _settings = settings;
        _saveSettings = saveSettings;
        IsElevated = elevationService.IsElevated;

        enabled = settings.TarkovAudioEnabled;
        boostDb = settings.TarkovAudioBoostDb;
        loudThresholdDb = settings.TarkovAudioLoudThresholdDb;
        presenceDb = settings.TarkovAudioPresenceDb;
        hotkey = settings.TarkovAudioHotkey;

        RefreshSetupState();
        // Keeps the installed plugin in step with the one shipped next to this exe after an update.
        if (SetupState == TarkovAudioSetupState.Installed && IsElevated)
            TryInstall(showErrors: false);

        Push();
    }

    public bool IsSetUp => SetupState == TarkovAudioSetupState.Installed;
    public bool CanInstall => SetupState == TarkovAudioSetupState.NotInstalled && IsElevated;
    public bool IsEqualizerApoMissing => SetupState == TarkovAudioSetupState.EqualizerApoMissing;
    public bool CanRestartAudio => IsSetUp && IsElevated && !IsRestartingAudio;

    public string StatusText
    {
        get
        {
            if (!IsSetUp) return "Not set up";
            if (!IsActive) return "Off";
            return RestartPending ? "On (plugin updated: restart the audio engine to use it everywhere)" : "On";
        }
    }

    public string SetupText => SetupState switch
    {
        TarkovAudioSetupState.EqualizerApoMissing =>
            "Equalizer APO is not installed. Install it, tick ONLY your headset (HyperX Cloud III) in its " +
            "Configurator, reboot, then come back here.",
        TarkovAudioSetupState.NotInstalled => IsElevated
            ? "Equalizer APO found. Click Install to add the compressor plugin to it."
            : "Equalizer APO found. Restart CenterConsole as Administrator to install the plugin.",
        _ => "Installed in Equalizer APO.",
    };

    public string BoostText => $"+{BoostDb} dB";
    public string LoudThresholdText => $"-{LoudThresholdDb} dBFS";
    public string PresenceText => $"+{PresenceDb} dB";

    public void Toggle() => Enabled = !Enabled;

    public void Install() => TryInstall(showErrors: true);

    public void Recheck()
    {
        RefreshSetupState();
        Push();
    }

    public async Task RestartAudioEngineAsync()
    {
        IsRestartingAudio = true;
        Notice = "Restarting the audio engine...";
        bool ok = await Task.Run(_service.RestartAudioEngine);
        IsRestartingAudio = false;
        if (ok)
        {
            RestartPending = false;
            Notice = "Audio engine restarted. Restart any game that lost its sound.";
        }
        else
        {
            Notice = null;
            LastError = "Couldn't restart the Windows Audio service. Is CenterConsole running as Administrator?";
        }
    }

    private void RefreshSetupState() => SetupState = _service.GetSetupState();

    /// <summary>Turns processing off on exit: with CenterConsole gone nothing would turn it off again.</summary>
    public void Shutdown() => _service.Apply(BuildParameters(active: false));

    public void ApplyNewHotkey(HotkeyBinding binding)
    {
        Hotkey = binding;
        _settings.TarkovAudioHotkey = binding;
        _saveSettings();
    }

    private void TryInstall(bool showErrors)
    {
        try
        {
            if (_service.Install(BundledPluginPath))
                RestartPending = true;
            LastError = null;
        }
        catch (Exception ex)
        {
            if (showErrors)
                LastError = ex.Message;
        }
        RefreshSetupState();
        Push();
    }

    partial void OnEnabledChanged(bool value)
    {
        _settings.TarkovAudioEnabled = value;
        _saveSettings();
        Push();
    }

    partial void OnBoostDbChanged(int value)
    {
        _settings.TarkovAudioBoostDb = value;
        _saveSettings();
        OnPropertyChanged(nameof(BoostText));
        Push();
    }

    partial void OnLoudThresholdDbChanged(int value)
    {
        _settings.TarkovAudioLoudThresholdDb = value;
        _saveSettings();
        OnPropertyChanged(nameof(LoudThresholdText));
        Push();
    }

    partial void OnPresenceDbChanged(int value)
    {
        _settings.TarkovAudioPresenceDb = value;
        _saveSettings();
        OnPropertyChanged(nameof(PresenceText));
        Push();
    }

    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnRestartPendingChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnIsRestartingAudioChanged(bool value) => OnPropertyChanged(nameof(CanRestartAudio));

    partial void OnSetupStateChanged(TarkovAudioSetupState value)
    {
        OnPropertyChanged(nameof(IsSetUp));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanRestartAudio));
        OnPropertyChanged(nameof(IsEqualizerApoMissing));
        OnPropertyChanged(nameof(SetupText));
        OnPropertyChanged(nameof(StatusText));
    }

    private void Push()
    {
        bool active = IsSetUp && Enabled;
        if (!_service.Apply(BuildParameters(active)) && IsSetUp)
            LastError = "Couldn't write the on/off switch to the registry. Is CenterConsole running as Administrator?";
        IsActive = active;
        OnPropertyChanged(nameof(StatusText));
    }

    private TarkovAudioParameters BuildParameters(bool active) =>
        new(active, BoostDb, LoudThresholdDb, PresenceDb);
}

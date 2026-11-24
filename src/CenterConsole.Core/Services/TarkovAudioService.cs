using System.Diagnostics;
using Microsoft.Win32;

namespace CenterConsole.Core.Services;

public enum TarkovAudioSetupState
{
    /// <summary>Equalizer APO isn't installed, so there's nothing to host the plugin.</summary>
    EqualizerApoMissing,
    /// <summary>Equalizer APO is installed but the TarkovAudio plugin isn't wired into its config.</summary>
    NotInstalled,
    Installed,
}

public sealed record EqualizerApoPaths(string InstallPath, string ConfigPath);

/// <summary>Values the TarkovAudio plugin reads from the registry (see native/TarkovAudio/TarkovAudio.cpp).</summary>
public sealed record TarkovAudioParameters(bool Enabled, int BoostDb, int LoudThresholdDb, int PresenceDb);

public interface ITarkovAudioService
{
    EqualizerApoPaths? FindEqualizerApo();
    TarkovAudioSetupState GetSetupState();

    /// <summary>Copies the plugin into Equalizer APO and includes it from config.txt. Idempotent; requires
    /// elevation. Returns true if a new plugin build was installed (the audio engine needs a restart to be
    /// sure every stream uses it). Throws on failure with a message suitable for showing to the user.</summary>
    bool Install(string pluginDllPath);

    /// <summary>Publishes the on/off state and tuning to the plugin. Returns false if the registry write
    /// failed (e.g. not elevated).</summary>
    bool Apply(TarkovAudioParameters parameters);

    /// <summary>Restarts the Windows Audio service so audiodg reloads every plugin instance. Needs elevation.
    /// Running games may lose sound until restarted.</summary>
    bool RestartAudioEngine();
}

/// <summary>
/// Hooks the TarkovAudio VST plugin into Equalizer APO, which runs it as an APO on the user's existing
/// output device: no virtual audio devices, no device switching. CenterConsole is only the switch: it
/// writes Enabled + tuning values to HKLM, which the plugin polls from inside audiodg.exe and crossfades
/// to, so toggling never makes Equalizer APO reload its config.
/// </summary>
public sealed class TarkovAudioService : ITarkovAudioService
{
    public const string RegistryKeyPath = @"SOFTWARE\CenterConsole\TarkovAudio";
    public const string IncludeFileName = "centerconsole-tarkov.txt";
    public const string IncludeLine = "Include: " + IncludeFileName;
    public const string BackupFileName = "config.before-centerconsole.txt";
    private const string StockExampleInclude = "Include: example.txt";

    public EqualizerApoPaths? FindEqualizerApo()
    {
        string? install = null, config = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EqualizerAPO");
            install = key?.GetValue("InstallPath") as string;
            config = key?.GetValue("ConfigPath") as string;
        }
        catch
        {
            // Fall through to the default location.
        }

        install ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO");
        config ??= Path.Combine(install, "config");

        return Directory.Exists(install) && Directory.Exists(config) ? new EqualizerApoPaths(install, config) : null;
    }

    public static string PluginInstallPath(EqualizerApoPaths apo, string pluginDllPath) =>
        Path.Combine(PluginFolder(apo), $"TarkovAudio-{ContentHash(pluginDllPath)}.dll");

    private static string PluginFolder(EqualizerApoPaths apo) =>
        Path.Combine(apo.InstallPath, "VSTPlugins", "CenterConsole");

    public TarkovAudioSetupState GetSetupState()
    {
        var apo = FindEqualizerApo();
        if (apo is null)
            return TarkovAudioSetupState.EqualizerApoMissing;

        string configTxt = Path.Combine(apo.ConfigPath, "config.txt");
        string includeFile = Path.Combine(apo.ConfigPath, IncludeFileName);
        bool installed =
            File.Exists(includeFile) &&
            ReferencedPluginExists(File.ReadAllText(includeFile)) &&
            File.Exists(configTxt) && HasInclude(File.ReadAllText(configTxt));

        return installed ? TarkovAudioSetupState.Installed : TarkovAudioSetupState.NotInstalled;
    }

    private static bool ReferencedPluginExists(string includeText)
    {
        const string prefix = "VSTPlugin: Library \"";
        string? line = SplitLines(includeText).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (line is null)
            return false;
        int end = line.IndexOf('"', prefix.Length);
        return end > prefix.Length && File.Exists(line[prefix.Length..end]);
    }

    public bool Install(string pluginDllPath)
    {
        var apo = FindEqualizerApo()
            ?? throw new InvalidOperationException("Equalizer APO is not installed.");
        if (!File.Exists(pluginDllPath))
            throw new FileNotFoundException("TarkovAudio.dll was not found next to CenterConsole.exe.", pluginDllPath);

        // Each build gets its own file name. audiodg.exe keeps a loaded plugin DLL locked and, on a config
        // reload, would reuse the already-loaded module for an unchanged path; a new path guarantees
        // Equalizer APO loads the new code as soon as the include file below points at it.
        string pluginDest = PluginInstallPath(apo, pluginDllPath);
        Directory.CreateDirectory(PluginFolder(apo));
        if (!File.Exists(pluginDest))
            File.Copy(pluginDllPath, pluginDest);

        // Only rewrite when it changes: every write makes Equalizer APO reload its whole config.
        string includePath = Path.Combine(apo.ConfigPath, IncludeFileName);
        string includeText = BuildIncludeFile(pluginDest);
        bool pluginChanged = !File.Exists(includePath) || File.ReadAllText(includePath) != includeText;
        if (pluginChanged)
            File.WriteAllText(includePath, includeText);

        DeleteStalePlugins(apo, keep: pluginDest);
        RemoveFourSliderLeftovers();

        string configTxt = Path.Combine(apo.ConfigPath, "config.txt");
        string existing = File.Exists(configTxt) ? File.ReadAllText(configTxt) : string.Empty;
        string updated = AddInclude(existing);
        if (updated != existing)
        {
            string backup = Path.Combine(apo.ConfigPath, BackupFileName);
            if (existing.Length > 0 && !File.Exists(backup))
                File.WriteAllText(backup, existing);
            File.WriteAllText(configTxt, updated);
        }

        return pluginChanged;
    }

    public bool Apply(TarkovAudioParameters p)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegistryKeyPath, writable: true);
            key.SetValue("BoostDb", Math.Clamp(p.BoostDb, 0, 24), RegistryValueKind.DWord);
            key.SetValue("LoudThresholdDb", Math.Clamp(p.LoudThresholdDb, 0, 60), RegistryValueKind.DWord);
            key.SetValue("PresenceDb", Math.Clamp(p.PresenceDb, 0, 12), RegistryValueKind.DWord);
            // Written last so the plugin never switches on with stale tuning.
            key.SetValue("Enabled", p.Enabled ? 1 : 0, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>Removes what the reverted four-slider version left in the registry: its meter key (which
    /// had an ACL letting audiodg write to it) and the background floor setting.</summary>
    private static void RemoveFourSliderLeftovers()
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(RegistryKeyPath + @"\Status", throwOnMissingSubKey: false);
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath, writable: true);
            key?.DeleteValue("FloorDb", throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
        }
    }

    public bool RestartAudioEngine()
    {
        try
        {
            var startInfo = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -Command \"Restart-Service audiosrv -Force\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(startInfo)!;
            process.WaitForExit(30_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public static string BuildIncludeFile(string pluginPath) =>
        "# Managed by CenterConsole (Tarkov Audio). Turn it on/off and tune it in CenterConsole;\r\n" +
        "# the plugin stays loaded and bypasses itself when off.\r\n" +
        $"VSTPlugin: Library \"{pluginPath}\"\r\n";

    public static bool HasInclude(string configText) =>
        SplitLines(configText).Any(l => l.Trim().Equals(IncludeLine, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns config.txt with our Include line added. A stock config (the installer's demo preamp + example
    /// filters) is replaced outright, since leaving it would apply a -6 dB preamp and test EQ all the time;
    /// anything the user wrote themselves is kept and our line is appended.
    /// </summary>
    public static string AddInclude(string configText)
    {
        if (HasInclude(configText))
            return configText;

        bool isStock = SplitLines(configText).Any(l => l.Trim().Equals(StockExampleInclude, StringComparison.OrdinalIgnoreCase));
        if (isStock || string.IsNullOrWhiteSpace(configText))
            return IncludeLine + "\r\n";

        string separator = configText.EndsWith('\n') ? string.Empty : "\r\n";
        return configText + separator + IncludeLine + "\r\n";
    }

    private static IEnumerable<string> SplitLines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r'));

    private static string ContentHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))[..12].ToLowerInvariant();
    }

    /// <summary>Removes earlier plugin builds. Ones audiodg still has loaded are locked and are simply
    /// left for the next run to clean up.</summary>
    private static void DeleteStalePlugins(EqualizerApoPaths apo, string keep)
    {
        foreach (string file in Directory.EnumerateFiles(PluginFolder(apo), "TarkovAudio*"))
        {
            if (string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
                continue;
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

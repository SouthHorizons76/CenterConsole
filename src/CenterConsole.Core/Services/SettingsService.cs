using System.Text.Json;
using CenterConsole.Core.Models;

namespace CenterConsole.Core.Services;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}

/// <summary>
/// JSON-backed settings store. The base directory is injectable so tests can point it at a temp
/// folder instead of the real %AppData%. Production code should use the parameterless constructor.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;

    public SettingsService() : this(GetDefaultBaseDirectory())
    {
    }

    public SettingsService(string baseDirectory)
    {
        _settingsFilePath = Path.Combine(baseDirectory, "settings.json");
    }

    private static string GetDefaultBaseDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CenterConsole");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
                return new AppSettings();

            string json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Missing, corrupt, or unreadable settings file: never crash the app over this.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string directory = Path.GetDirectoryName(_settingsFilePath)!;
        Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(settings, JsonOptions);

        // Write-to-temp-then-move avoids a truncated/corrupt settings.json if the process dies mid-write.
        string tempPath = _settingsFilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsFilePath, overwrite: true);
    }
}

using System.IO;
using System.Text.Json;
using SonyBraviaControl.Models;

namespace SonyBraviaControl.Services;

public sealed class UserSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public UserSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SonyBraviaControl");

        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public BraviaSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new BraviaSettings();

            return JsonSerializer.Deserialize<BraviaSettings>(File.ReadAllText(_settingsPath), JsonOptions)
                   ?? new BraviaSettings();
        }
        catch
        {
            return new BraviaSettings();
        }
    }

    public void Save(BraviaSettings settings)
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}

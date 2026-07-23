using System.IO;
using System.Text.Json;
using TranslatePopup.Models;

namespace TranslatePopup.Services;

public sealed class SettingsService
{
    // Debug builds (local development/verification) keep their own settings folder, separate
    // from the one a Release build - whether an end user's install or a local Release test -
    // reads from. Without this split, an API key typed in while testing a Debug build would
    // still be sitting in the shared file the next time a Release build runs.
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TranslatePopup"
#if DEBUG
        , "Debug"
#endif
    );

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }
}

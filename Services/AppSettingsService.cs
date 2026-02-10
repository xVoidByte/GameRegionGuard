using System;
using System.IO;
using System.Text.Json;

namespace GameRegionGuard.Services
{
    /// <summary>
    /// Minimal user-scoped settings store.
    /// Saved to: %APPDATA%\GameRegionGuard\settings.json
    /// </summary>
    public static class AppSettingsService
    {
        private const string AppFolderName = "GameRegionGuard";
        private const string LegacyAppFolderName = "BlockRussianServers";
        private const string SettingsFileName = "settings.json";
        private static readonly object LockObject = new object();

        public static bool IsDarkTheme { get; private set; } = true;

        private static string GetSettingsFilePath(string folderName)
        {
            var settingsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                folderName);

            return Path.Combine(settingsDir, SettingsFileName);
        }

        public static void Load()
        {
            lock (LockObject)
            {
                try
                {
                    var path = GetSettingsFilePath(AppFolderName);

                    // Migration: older builds stored settings under %APPDATA%\BlockRussianServers.
                    if (!File.Exists(path))
                    {
                        var legacyPath = GetSettingsFilePath(LegacyAppFolderName);
                        if (File.Exists(legacyPath))
                        {
                            Logger.Info("Found legacy settings file, migrating to new app folder");
                            path = legacyPath;
                        }
                    }

                    if (!File.Exists(path))
                    {
                        IsDarkTheme = true;
                        return;
                    }

                    var json = File.ReadAllText(path);
                    var model = JsonSerializer.Deserialize<SettingsModel>(json);

                    if (model == null)
                    {
                        IsDarkTheme = true;
                        return;
                    }

                    if (string.Equals(model.Theme, "Light", StringComparison.OrdinalIgnoreCase))
                    {
                        IsDarkTheme = false;
                    }
                    else
                    {
                        // Default to Dark for unknown values.
                        IsDarkTheme = true;
                    }

                    // If we loaded from the legacy location, persist to the new one.
                    var newPath = GetSettingsFilePath(AppFolderName);
                    if (!string.Equals(path, newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        Save();
                    }
                }
                catch (Exception ex)
                {
                    // Settings should never crash the app.
                    IsDarkTheme = true;
                    Logger.Warning($"Failed to load settings: {ex.Message}");
                }
            }
        }

        public static void SetTheme(bool dark)
        {
            lock (LockObject)
            {
                IsDarkTheme = dark;
            }
        }

        public static void Save()
        {
            lock (LockObject)
            {
                try
                {
                    var path = GetSettingsFilePath(AppFolderName);
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var model = new SettingsModel
                    {
                        Theme = IsDarkTheme ? "Dark" : "Light"
                    };

                    var json = JsonSerializer.Serialize(model, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    File.WriteAllText(path, json);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to save settings: {ex.Message}");
                }
            }
        }

        private sealed class SettingsModel
        {
            public string Theme { get; set; } = "Dark";
        }
    }
}

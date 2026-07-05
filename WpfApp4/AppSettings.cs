using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WpfApp4
{
    public class HotCommand
    {
        public string Name { get; set; } = "";
        // Аргументы после "adb [-s serial]", например: shell input keyevent 26
        public string Command { get; set; } = "";
    }

    public class AppSettings
    {
        public bool UseCustomToolsDir { get; set; } = false;
        public string CustomAdbDir { get; set; } = "";
        public string CustomScrcpyDir { get; set; } = "";

        public int DeviceRefreshIntervalSeconds { get; set; } = 2;
        public bool ConfirmBeforePower { get; set; } = true;

        public List<HotCommand> HotCommands { get; set; } = new();

        private static string SettingsPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch { /* используем значения по умолчанию */ }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { /* тихо игнорируем ошибку записи */ }
        }
    }
}
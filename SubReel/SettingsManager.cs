using SubReel;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

public static class SettingsManager
{
    public static AppSettings Current { get; private set; } = new AppSettings();

    public static void Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Current = new AppSettings();
                return;
            }

            string json = File.ReadAllText(path);

            Current = JsonSerializer.Deserialize<AppSettings>(json)
                      ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Ошибка загрузки настроек: " + ex.Message);
            Current = new AppSettings();
        }
    }

    public static void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(Current, options);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Ошибка сохранения настроек: " + ex.Message);
        }
    }
}


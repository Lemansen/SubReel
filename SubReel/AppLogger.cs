using System;
using System.IO;

public static class AppLogger
{
    private static readonly string LogPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".SubReelGame",
            "launcher.log");

    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

            File.AppendAllText(
                LogPath,
                $"[{DateTime.Now:HH:mm:ss}] {text}\n");
        }
        catch { }
    }
}
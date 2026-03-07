using System;
using System.IO;
using System.Text.Json;

public class StoredAccount
{
    public string Username { get; set; } = "";
    public string Uuid { get; set; } = "";
}
public class AccountData
{
    public string Username { get; set; } = "";
    public string Uuid { get; set; } = "";
}
public static class AccountStorage
{
    private static string AccountPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".SubReelGame",
            "account.json");

    public static void Save(AccountData data)
    {
        try
        {
            string dir = Path.GetDirectoryName(AccountPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(AccountPath, json);
        }
        catch { }
    }

    public static AccountData Load()
    {
        try
        {
            if (!File.Exists(AccountPath))
                return null;

            string json = File.ReadAllText(AccountPath);
            return JsonSerializer.Deserialize<AccountData>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(AccountPath))
                File.Delete(AccountPath);
        }
        catch { }
    }
}
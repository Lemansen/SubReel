using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

public static class VersionCacheManager
{
    private static readonly string CachePath =
        Path.Combine(Path.GetTempPath(), "SubReel_versions.json");

    private static readonly HttpClient client = new HttpClient(
        new HttpClientHandler { UseProxy = false });

    private const string ManifestUrl =
        "https://launchermeta.mojang.com/mc/game/version_manifest.json";

    // ⭐ ФЛАГ НАЛИЧИЯ КЭША
    private static bool _hasCache;
    public static bool HasCache
    {
        get
        {
            return _hasCache || File.Exists(CachePath);
        }
    }

    public static async Task<string> GetManifestJsonAsync(Action<string>? log = null)
    {
        try
        {
            if (File.Exists(CachePath))
            {
                var age = DateTime.Now - File.GetLastWriteTime(CachePath);

                if (age < TimeSpan.FromHours(6))
                {
                    log?.Invoke("[CACHE] Используется кеш списка версий");
                    _hasCache = true; // ⭐ ВАЖНО
                    return await File.ReadAllTextAsync(CachePath);
                }
            }

            log?.Invoke("[CACHE] Скачивание списка версий...");
            var json = await client.GetStringAsync(ManifestUrl);

            await File.WriteAllTextAsync(CachePath, json);
            _hasCache = true; // ⭐ ВАЖНО

            return json;
        }
        catch
        {
            if (File.Exists(CachePath))
            {
                log?.Invoke("[CACHE] Сеть недоступна → используем старый кеш");
                _hasCache = true; // ⭐ ВАЖНО
                return await File.ReadAllTextAsync(CachePath);
            }

            throw;
        }
    }
}
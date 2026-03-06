using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
#nullable enable

namespace SubReel.Infrastructure.System
{
    public static class JavaResolver
    {
        public static string RuntimeRoot =
     Path.Combine(
         Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
         ".SubReelGame",
         "runtime"
     );
        private static readonly SemaphoreSlim _installLock = new SemaphoreSlim(1, 1);
        public static int? GetJavaMajorVersion(string javaPath)
        {
            try
            {
                var info = new ProcessStartInfo(javaPath, "-version")
                {
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(info);
                string output = p.StandardError.ReadToEnd();
                p.WaitForExit();

                // Поддержка форматов:
                // "1.8.0_381" → 8
                // "11.0.20"  → 11
                // "17.0.9"   → 17
                // "21.0.1"   → 21
                var match = System.Text.RegularExpressions.Regex.Match(
                    output,
                    @"version ""(\d+)(\.(\d+))?"
                );

                if (!match.Success)
                    return null;

                int major = int.Parse(match.Groups[1].Value);

                // Старый формат Java 8: 1.8
                if (major == 1 && match.Groups[3].Success)
                    return int.Parse(match.Groups[3].Value);

                return major;
            }
            catch
            {
                return null;
            }
        }
        public static string? GetBundledJavaPath(int javaVersion)
        {
            string path = System.IO.Path.Combine(
                RuntimeRoot,
                $"java{javaVersion}",
                "bin",
                "javaw.exe"
            );

            return File.Exists(path) ? path : null;
        }
        public static string? GetExistingRuntime(int javaVersion)
        {
            string dir = Path.Combine(RuntimeRoot, $"java{javaVersion}");
            string exe = Path.Combine(dir, "bin", "javaw.exe");

            if (File.Exists(exe))
                return exe;

            return null;
        }
        public static string? FindSystemJava()
        {
            try
            {
                var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (!string.IsNullOrEmpty(javaHome))
                {
                    var javaExe = System.IO.Path.Combine(javaHome, "bin", "java.exe");
                    if (File.Exists(javaExe))
                        return javaExe;
                }

                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var javaDir = System.IO.Path.Combine(programFiles, "Java");

                if (Directory.Exists(javaDir))
                {
                    var javaExe = Directory.GetFiles(javaDir, "java.exe", SearchOption.AllDirectories)
                                           .FirstOrDefault();
                    if (javaExe != null)
                        return javaExe;
                }
            }
            catch { }

            return null;
        }
        public static async Task<string> EnsureBundledJavaAsync(
     int javaVersion,
     IProgress<double> progress,
     CancellationToken token,
     Action<string>? log = null)
        {
            await _installLock.WaitAsync(token);

            try
            {
                Directory.CreateDirectory(RuntimeRoot);

                string targetDir = Path.Combine(RuntimeRoot, $"java{javaVersion}");
                string javaExe = Path.Combine(targetDir, "bin", "javaw.exe");
                string marker = Path.Combine(targetDir, ".installed");

                if (File.Exists(javaExe) && File.Exists(marker))
                {
                    progress?.Report(100);
                    log?.Invoke("[JAVA] Используется кэшированная версия");
                    return javaExe;
                }

                if (Directory.Exists(targetDir))
                {
                    try { Directory.Delete(targetDir, true); }
                    catch { }
                }

                Directory.CreateDirectory(targetDir);

                string arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                string url =
                    $"https://api.adoptium.net/v3/binary/latest/{javaVersion}/ga/windows/{arch}/jre/hotspot/normal/eclipse";

                string zipPath = Path.Combine(targetDir, "runtime.zip");

                using HttpClient client = new HttpClient
                {
                    Timeout = TimeSpan.FromMinutes(5)
                };
                using var response = await RetryHelper.RetryAsync(
                    () => client.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead,
                        token),
                    3,
                    1500,
                    log);

                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? -1L;
                var canReport = total > 0;

                await ResumableDownloader.DownloadFileAsync(
    client,
    url,
    zipPath,
    progress,
    token
);

                await Task.Delay(150, token);
                if (!File.Exists(zipPath) || new FileInfo(zipPath).Length < 1_000_000)
                    throw new Exception("Архив Java поврежден или скачан не полностью");
                try
                {
                    using var zipStream = new FileStream(
                        zipPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);

                    ZipFile.ExtractToDirectory(zipStream, targetDir, true);
                }
                catch (InvalidDataException)
                {
                    try { File.Delete(zipPath); } catch { }
                    throw new Exception("Архив Java поврежден, будет выполнена повторная загрузка");
                }

                File.Delete(zipPath);

                var innerDir = Directory.GetDirectories(targetDir)
                    .FirstOrDefault(d => File.Exists(Path.Combine(d, "bin", "javaw.exe")));

                if (innerDir != null && !File.Exists(javaExe))
                {
                    foreach (var item in Directory.GetFileSystemEntries(innerDir))
                    {
                        string dest = Path.Combine(targetDir, Path.GetFileName(item));

                        if (Directory.Exists(item))
                            Directory.Move(item, dest);
                        else
                            File.Move(item, dest);
                    }

                    Directory.Delete(innerDir, true);
                }

                await File.WriteAllTextAsync(marker, "ok", token);
                if (!File.Exists(javaExe))
                    throw new Exception("Java установлена, но javaw.exe не найден");
                progress?.Report(100);
                return javaExe;
            }
            finally
            {
                _installLock.Release();
            }
        }
    }
}
    
    
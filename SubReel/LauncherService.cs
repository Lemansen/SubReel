using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using Microsoft.VisualBasic.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;
#nullable enable
namespace SubReel
{
    public class LauncherService
    {
        private readonly string _gamePath;
        private readonly Action<string>? _log;
        private readonly string _minecraftPath;

        public LauncherService(string gamePath, Action<string>? log = null)
        {
            _minecraftPath = gamePath;   // ✅ просто сохраняем путь
            _gamePath = gamePath;
            _log = log;
        }

        public bool IsVersionInstalled(string versionId)
        {
            var versionPath = Path.Combine(_minecraftPath, "versions", versionId);
            return Directory.Exists(versionPath);
        }
        public async Task LaunchAsync(LaunchOptions options)
        {
            var path = new MinecraftPath(options.GamePath);
            var launcher = new MinecraftLauncher(path);

            if (options.OfflineMode)
            {
                if (!IsVersionInstalled(options.Version))
                    throw new Exception("Версия не установлена локально");

                var launchOption = new MLaunchOption
                {
                    Session = MSession.CreateOfflineSession(options.Nickname),
                    MaximumRamMb = options.MaxRamMb,
                    MinimumRamMb = options.MinRamMb
                };

                await launcher.CreateProcessAsync(options.Version, launchOption);
                return;
            }

            // обычный online режим
            await launcher.InstallAsync(options.Version);
        }
        public async Task<Process> PrepareAndCreateProcessAsync(
    string version,
    LaunchOptions options,
    IProgress<InstallerProgressChangedEventArgs>? installerProgress,
    IProgress<ByteProgress>? byteProgress,
    CancellationToken token)
        {
            var launcher = new MinecraftLauncher(_gamePath);

            var ver = await launcher.GetVersionAsync(version);

            await GameIntegrityChecker.EnsureGameInstalledAsync(
                launcher,
                version,
                installerProgress,
                byteProgress,
                token,
                _log
            );

            var nickname = string.IsNullOrWhiteSpace(options.Nickname)
                ? "Player"
                : options.Nickname;

            var session = options.Session ?? MSession.CreateOfflineSession(nickname);

            var launchOption = new MLaunchOption
            {
                Session = session,
                JavaPath = string.IsNullOrWhiteSpace(options.JavaPath) ? "java" : options.JavaPath,
                MaximumRamMb = options.RamMb
            };

            return await launcher.CreateProcessAsync(ver.Id, launchOption);
        }
    }
    }
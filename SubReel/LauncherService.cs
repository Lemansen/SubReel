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

        // ⭐ 1. Добавляем событие прогресса
        public event EventHandler<InstallerProgressChangedEventArgs>? ProgressChanged;

        public LauncherService(string gamePath, Action<string>? log = null)
        {
            _minecraftPath = gamePath;
            _gamePath = gamePath;
            _log = log;
        }

        public bool IsVersionInstalled(string versionId)
        {
            var versionPath = Path.Combine(_minecraftPath, "versions", versionId);
            return Directory.Exists(versionPath);
        }

        public async Task<Process> PrepareAndCreateProcessAsync(
            string version,
            LaunchOptions options,
            IProgress<InstallerProgressChangedEventArgs>? installerProgress,
            IProgress<ByteProgress>? byteProgress,
            CancellationToken token)
        {
            var launcher = new MinecraftLauncher(_gamePath);

            // ⭐ 2. Создаем мост для прогресса
            // Если внешний прогресс не передан, создаем свой, который вызывает наше событие
            var progressBridge = installerProgress ?? new Progress<InstallerProgressChangedEventArgs>(e =>
            {
                ProgressChanged?.Invoke(this, e);
            });

            var ver = await launcher.GetVersionAsync(version);

            // Передаем наш progressBridge в чекер
            await GameIntegrityChecker.EnsureGameInstalledAsync(
                launcher,
                version,
                progressBridge, // используем мост
                byteProgress,
                token,
                _log
            );

            var nickname = string.IsNullOrWhiteSpace(options.Nickname) ? "Player" : options.Nickname;
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
    
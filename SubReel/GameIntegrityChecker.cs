using CmlLib.Core;
using SubReel;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
public static class GameIntegrityChecker
{
    public static async Task EnsureGameInstalledAsync(
    MinecraftLauncher launcher,
    string versionId,
    IProgress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs> installerProgress,
    IProgress<CmlLib.Core.ByteProgress> byteProgress,
    CancellationToken token,
    Action<string>? log = null,
    Action<InstallStage, double, string, string>? stage = null)
    {
        try
        {
            stage?.Invoke(InstallStage.Preparing, 0.05, "Поиск версии...", "Проверка локальных файлов");

            var version = await launcher.GetVersionAsync(versionId);

            bool needInstall = false;

            string basePath = launcher.MinecraftPath.BasePath;

            string versionJson = Path.Combine(basePath, "versions", versionId, versionId + ".json");
            string clientJar = Path.Combine(basePath, "versions", versionId, versionId + ".jar");
            string librariesDir = Path.Combine(basePath, "libraries");
            string assetsDir = Path.Combine(basePath, "assets");

            if (version == null)
            {
                log?.Invoke("[CHECK] Версия отсутствует");
                needInstall = true;
            }
            else
            {
                if (!File.Exists(versionJson))
                {
                    log?.Invoke("[CHECK] Нет JSON");
                    needInstall = true;
                }

                if (!File.Exists(clientJar))
                {
                    log?.Invoke("[CHECK] Нет client.jar");
                    needInstall = true;
                }

                if (!Directory.Exists(librariesDir))
                {
                    log?.Invoke("[CHECK] Нет libraries");
                    needInstall = true;
                }

                if (!Directory.Exists(assetsDir))
                {
                    log?.Invoke("[CHECK] Нет assets");
                    needInstall = true;
                }
            }

            if (!needInstall)
            {
                stage?.Invoke(InstallStage.Verifying, 1, "Файлы на месте", "Проверка целостности");
                log?.Invoke("[CHECK] Игра уже установлена");
                return;
            }

            stage?.Invoke(InstallStage.Downloading, 0, "Подготовка загрузки", "Подключение к серверу");

            await RetryHelper.RetryAsync<object?>(
                async () =>
                {
                    await launcher.InstallAsync(
                        versionId,
                        installerProgress,
                        byteProgress,
                        token);

                    return null;
                },
                3,
                2000,
                log
            );

            stage?.Invoke(InstallStage.Verifying, 0.7, "Проверка файлов", "Проверка целостности");
            stage?.Invoke(InstallStage.Finalizing, 1, "Подготовка запуска", "Инициализация компонентов");

            log?.Invoke("[CHECK] Установка завершена");
        }
        catch (OperationCanceledException)
        {
            log?.Invoke("[CHECK] Установка отменена");
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke("[CHECK] Ошибка: " + ex.Message);
            throw;
        }
    }
}
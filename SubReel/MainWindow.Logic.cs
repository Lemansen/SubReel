using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.VersionMetadata;
using CmlLib.Core.Version;
using System;
using System.Collections.Generic; // Обязательно для List<NewsItem>
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using IO = System.IO;
using IOPath = System.IO.Path;
#nullable enable


namespace SubReel
{
    public partial class MainWindow : Window
    {

        private int GetRequiredJavaMajor(string mcVersion)
        {
            if (Version.TryParse(mcVersion, out var v))
            {
                if (v >= new Version(1, 20, 5)) return 21;
                if (v >= new Version(1, 17)) return 17;
            }

            return 8;
        }
        public enum JavaSourceType
        {
            Bundled,   // скачанная лаунчером
            System,    // из PATH
            Manual     // выбранная пользователем
        }
        // --- НАСТРОЙКИ И ОБНОВЛЕНИЯ ---
        public void SaveSettings()
        {
            if (!IsLoaded) return;

            try
            {
                var s = SettingsManager.Current;

                s.Nickname = string.IsNullOrWhiteSpace(NicknameBox?.Text)
                    ? "Player"
                    : NicknameBox.Text.Trim();

                s.Ram = RamSlider?.Value ?? 4096;
                s.IsLicensed = IsLicensed;
                s.SelectedVersion = _selectedVersion ?? "1.21.1";
                s.IsConsoleShow = ConsoleCheck?.IsChecked == true;
                s.ManualJavaPath = _manualJavaPath;
                s.JavaSource = _javaSource;

                SettingsManager.Save(ConfigPath);

                Debug.WriteLine("Настройки успешно сохранены: " + ConfigPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Ошибка сохранения настроек: " + ex.Message);
            }
        }
        private string? FindSystemJava()
        {
            try
            {
                var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");

                if (!string.IsNullOrWhiteSpace(javaHome))
                {
                    var path = System.IO.Path.Combine(javaHome, "bin", "javaw.exe");
                    if (File.Exists(path))
                        return path;
                }

                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var javaDir = System.IO.Path.Combine(programFiles, "Java");

                if (Directory.Exists(javaDir))
                {
                    foreach (var dir in Directory.GetDirectories(javaDir))
                    {
                        var path = System.IO.Path.Combine(dir, "bin", "javaw.exe");
                        if (File.Exists(path))
                            return path;
                    }
                }
            }
            catch { }

            return null;
        }
        private LaunchOptions BuildLaunchOptions()
        {
            return new LaunchOptions
            {
                Nickname = string.IsNullOrWhiteSpace(NicknameBox?.Text)
                    ? "Player"
                    : NicknameBox.Text.Trim(),

                RamMb = (int)(RamSlider?.Value ?? 4096),
                Version = _selectedVersion ?? "1.21.1",
                ShowConsole = ConsoleCheck?.IsChecked == true,
                IsLicensed = IsLicensed,
                Session = CurrentSession,

                GamePath = AppDataPath,   // 🔥 ОБЯЗАТЕЛЬНО

                JavaPath = null
            };
        }
        private void ApplySettingsToUI()
        {
            var s = SettingsManager.Current;

            if (NicknameBox != null)
                NicknameBox.Text = s.Nickname;

            if (DisplayNick != null)
                DisplayNick.Text = s.Nickname;

            if (ConsoleCheck != null)
                ConsoleCheck.IsChecked = s.IsConsoleShow;

            if (RamSlider != null)
                RamSlider.Value = s.Ram;

            if (RamInput != null)
                RamInput.Text = ((int)s.Ram).ToString();

            if (GbText != null)
                GbText.Text = $"{(s.Ram / 1024.0):F1} GB";

            _selectedVersion = s.SelectedVersion;

            if (BtnVanilla != null)
                BtnVanilla.Content = _selectedVersion;

            if (SelectedVersionBottom != null)
                SelectedVersionBottom.Text = $"Vanilla {_selectedVersion}";
        }

        public class LauncherSettings
        {
            public string Nickname { get; set; } = "Player";
            public double Ram { get; set; } = 4096;
            public bool IsLicensed { get; set; } = false;
            public string SelectedVersion { get; set; } = "1.21.1";
            public bool IsConsoleShow { get; set; } = false;
        }


        private void LoadSettings()
        {
            if (!File.Exists(ConfigPath))
                return;
            try
            {
                var json = File.ReadAllText(ConfigPath);

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    // --- НИК ---
                    string nick = root.TryGetProperty("Nickname", out var nickProp)
                        ? (nickProp.GetString() ?? "Player")
                        : "Player";

                    if (string.IsNullOrWhiteSpace(nick)) nick = "Player";

                    if (NicknameBox != null) NicknameBox.Text = nick;
                    if (DisplayNick != null) DisplayNick.Text = nick;

                    // --- АВАТАР ---
                    if (UserAvatarImg != null)
                    {
                        try
                        {
                            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri($"https://minotar.net/helm/{nick}/45.png", UriKind.Absolute);
                            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            UserAvatarImg.ImageSource = bitmap;
                        }
                        catch { }
                    }

                    // --- КОНСОЛЬ ---
                    if (root.TryGetProperty("IsConsoleShow", out var consoleProp))
                    {
                        if (ConsoleCheck != null)
                            ConsoleCheck.IsChecked = consoleProp.GetBoolean();
                    }
                    if (root.TryGetProperty("ManualJavaPath", out var javaProp))
                    {
                        _manualJavaPath = javaProp.ValueKind == JsonValueKind.String
                            ? javaProp.GetString()
                            : null;
                    }

                    // 👇 ОБНОВЛЕНИЕ ИНДИКАТОРА (после установки CheckBox)
                    if (ConsoleIndicator != null && ConsoleCheck != null)
                    {
                        ConsoleIndicator.Visibility =
                            ConsoleCheck.IsChecked == true
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                    }

                    // --- RAM ---
                    if (root.TryGetProperty("Ram", out var ramProp) && RamSlider != null)
                    {
                        if (ramProp.ValueKind == JsonValueKind.Number)
                        {
                            double ramValue = ramProp.GetDouble();
                            ramValue = Math.Max(RamSlider.Minimum, Math.Min(RamSlider.Maximum, ramValue));
                            RamSlider.Value = ramValue;

                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (RamInput != null)
                                    RamInput.Text = ((int)ramValue).ToString();

                                if (GbText != null)
                                    GbText.Text = $"{(ramValue / 1024.0):F1} GB";
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }

                    // --- ВЕРСИЯ ---
                    if (root.TryGetProperty("SelectedVersion", out var verProp))
                    {
                        _selectedVersion = verProp.GetString() ?? "1.21.1";

                        if (BtnVanilla != null)
                            BtnVanilla.Content = _selectedVersion;

                        if (SelectedVersionBottom != null)
                            SelectedVersionBottom.Text = $"Vanilla {_selectedVersion}";
                    }

                    // --- ЛИЦЕНЗИЯ ---
                    if (root.TryGetProperty("IsLicensed", out var licensedProp))
                    {
                        IsLicensed = licensedProp.GetBoolean();

                        if (AccountTypeStatus != null && AccountTypeBadge != null)
                        {
                            if (IsLicensed)
                            {
                                AccountTypeStatus.Text = "PREMIUM";
                                AccountTypeStatus.Foreground = Brushes.Black;
                                AccountTypeBadge.Background = new SolidColorBrush(Color.FromRgb(255, 170, 0));
                            }
                            else
                            {
                                AccountTypeStatus.Text = "OFFLINE";
                                AccountTypeStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0));
                                AccountTypeBadge.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLog($"LoadSettings error: {ex.Message}", Brushes.Red);
            }
        }

        private void UpdateRamUI()
        {
            // Проверяем, что слайдер существует
            if (RamSlider == null) return;

            double val = RamSlider.Value;

            // Обновляем текстовые поля напрямую
            if (RamInput != null)
                RamInput.Text = ((int)val).ToString();

            if (GbText != null)
                GbText.Text = $"{(val / 1024.0):F1} GB";
        }


        private void AppendLog(string message, SolidColorBrush color)
        {
            Dispatcher.Invoke(() =>
            {
                if (LogText == null) return;

                // Создаем новый "пробег" текста с нужным цветом
                Run run = new Run(message + Environment.NewLine) { Foreground = color };

                // Добавляем его в Inlines нашего TextBlock
                // Твой LogText в XAML должен поддерживать Inlines (это стандарт для TextBlock)
                LogText.Inlines.Add(run);

                // Авто-скролл вниз, который у тебя уже был
                LogScroll?.ScrollToEnd();
            });
        }


        // --- [А] ПРОВЕРКА ОБНОВЛЕНИЙ ---
        private async Task CheckForUpdates()
        {
            try
            {
                // 1. Получаем JSON с защитой от кэширования (добавляем Ticks)
                string jsonString = await _httpClient.GetStringAsync(MasterUrl + "?t=" + DateTime.Now.Ticks);

                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // 2. Десериализуем в MasterConfig
                var master = System.Text.Json.JsonSerializer.Deserialize<MasterConfig>(jsonString, options);

                if (master != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        // --- [ИСПРАВЛЕНО] 1. ОБНОВЛЯЕМ ОНЛАЙН И ЦВЕТ СТАТУСА ---
                        if (master.Status != null && OnlineCount != null)
                        {
                            OnlineCount.Text = $"ОНЛАЙН: {master.Status.OnlineCount}";

                            // Теперь проверяем не только цифру, но и статус сервера из JSON
                            // В твоем JSON это поле "server_status": "online"
                            bool isOnline = master.Status.OnlineCount > 0;

                            // Если в JSON будет добавлено поле ServerStatus (как в моем совете выше), 
                            // можно проверять его. Пока оставляем проверку по количеству игроков:
                            if (OnlineCircle != null)
                            {
                                OnlineCircle.Fill = isOnline
                                    ? (SolidColorBrush)new BrushConverter().ConvertFrom("#20F289")
                                    : Brushes.Gray;
                            }
                        }

                        // --- [БЕЗОПАСНО] 2. ПРОВЕРЯЕМ ВЕРСИЮ ---
                        if (master.Installer != null)
                        {
                            // 1. Сравниваем версии (CurrentVersion должна быть прописана в начале класса, например "1.0.0")
                            bool hasNewVersion = !string.IsNullOrEmpty(master.Installer.Version) &&
                                                 master.Installer.Version != CurrentVersion;

                            if (hasNewVersion)
                            {
                                // 2. Проверяем, что ссылка на скачивание вообще есть в JSON
                                if (!string.IsNullOrEmpty(master.Installer.DownloadUrl))
                                {
                                    UpdateBadge.Visibility = Visibility.Visible;
                                    LauncherVersionText.Text = $"ОБНОВИТЬ ДО {master.Installer.Version}";

                                    // Сохраняем ссылку в Tag, чтобы метод UpdateBadge_Click её подцепил
                                    UpdateBadge.Tag = master.Installer.DownloadUrl;

                                    // Запуск анимации (PulseUpdateAnim должна быть в MainWindow.xaml в <Window.Resources>)
                                    if (this.Resources.Contains("PulseUpdateAnim"))
                                    {
                                        var sb = (Storyboard)this.Resources["PulseUpdateAnim"];
                                        sb?.Begin(UpdateBadge); // Указываем конкретный объект для анимации
                                    }
                                }
                            }
                            else
                            {
                                // Если версия актуальная — прячем кнопку
                                UpdateBadge.Visibility = Visibility.Collapsed;
                            }
                        }

                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => {
                    if (OnlineCount != null) OnlineCount.Text = "OFFLINE";
                    if (OnlineCircle != null) OnlineCircle.Fill = Brushes.Red; // Красный, если нет связи
                });
                SafeLog($"[System] Ошибка синхронизации: {ex.Message}", Brushes.Gray);
            }
        }



        private async void AppVersionText_Click(object sender, MouseButtonEventArgs e)
        {
            // ОСТАНАВЛИВАЕМ прохождение клика к ProfileRegion (чтобы не открывалось окно логина)
            e.Handled = true;

            try
            {
                string oldText = AppVersionText.Text;
                AppVersionText.Text = "ПРОВЕРКА...";

                await CheckForUpdates();

                AppVersionText.Text = oldText;
            }
            catch (Exception ex)
            {
                SafeLog($"[Update] Ошибка: {ex.Message}", Brushes.Red);
            }
        }


        // Отдельный метод для клика по кнопке (пропиши его в XAML: MouseLeftButtonDown="UpdateBadge_Click")
        private void UpdateBadge_Click(object sender, MouseButtonEventArgs e)
        {
            if (UpdateBadge.Tag is string url)
            {
                StartManualUpdate(url);
            }
        }
        private bool _isUpdating = false;
        private CancellationTokenSource _updateCts;
        private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Проверка на null обязательна, так как событие срабатывает при инициализации XAML
            if (RamInput == null || GbText == null) return;

            int val = (int)e.NewValue;

            // Обновляем текст в мегабайтах
            RamInput.Text = val.ToString();

            // Вычисляем гигабайты (используем "N1" или "F1" для единообразия точек/запятых)
            double gb = val / 1024.0;
            GbText.Text = string.Format("{0:0.0} GB", gb);
        }
        private void CancelUpdate_Click(object sender, RoutedEventArgs e)
        {
            _updateCts?.Cancel();
        }

        private async void StartManualUpdate(string downloadUrl)
        {

            if (_isUpdating)
                return;

            _isUpdating = true;
            _updateCts = new CancellationTokenSource();
            var token = _updateCts.Token;

            if (LauncherVersionText != null)
                LauncherVersionText.IsHitTestVisible = false;
           
            string tempFile = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SubReelUpdate.exe");
            string partFile = tempFile + ".part";
            long existingBytes = 0;

            if (File.Exists(partFile))
            {
                existingBytes = new FileInfo(partFile).Length;
                SafeLog($"[Update] Найден незавершенный файл ({existingBytes} байт)", Brushes.Orange);
            }
            try
            {
                SafeLog("[Update] Начинаю загрузку новой версии...", Brushes.Cyan);

                var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);

                if (existingBytes > 0)
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);

                using (var response = await RetryHelper.RetryAsync(
                    () => _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        token),
                    3,
                    2000,
                    msg =>
                    {
                        SafeLog(msg, Brushes.Orange);
                        AppLogger.Log(msg);
                        if (msg.Contains("Попытка"))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                StatusLabel.Text = "ОБНОВЛЕНИЕ: проблемы с сетью...";
                            });
                        }
                    }))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength;
                    bool canReportProgress = totalBytes.HasValue && totalBytes.Value > 0;

                    if (!canReportProgress)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusLabel.Text = "ОБНОВЛЕНИЕ: загрузка...";
                        });
                    }
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(tempFile)!);
                    using (var contentStream = await response.Content.ReadAsStreamAsync(token))
                    using (var fileStream = new FileStream(
    partFile,
    existingBytes > 0 ? FileMode.Append : 
    FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        8192,
                        true))
                    {
                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;
                        long lastUiUpdate = 0;

                        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
                        {
                            token.ThrowIfCancellationRequested();

                            await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                            totalRead += bytesRead;

                            if (canReportProgress && totalRead - lastUiUpdate > 50000)
                            {
                                lastUiUpdate = totalRead;

                                long fullRead = existingBytes + totalRead;
                                int percent = (int)(fullRead * 100 / (existingBytes + totalBytes.Value));
                            }
                        }
                    }
                }
                // 🔥 завершаем докачку → делаем финальный exe
                if (!File.Exists(partFile))
                    throw new Exception("Файл обновления не найден (.part)");

                if (File.Exists(tempFile))
                    File.Delete(tempFile);

                File.Move(partFile, tempFile);

                SafeLog("[Update] Файл обновления готов", Brushes.Lime);

                // 🔥 проверка файла перед установкой
                if (new FileInfo(tempFile).Length < 1_000_000)
                    throw new Exception("Файл обновления поврежден");

                SafeLog("[Update] Загрузка завершена. Установка...", Brushes.Lime);

                await Task.Delay(400, token);

                ApplyUpdate(tempFile);
            }

            finally
            {
                _updateCts?.Dispose();
                _updateCts = null;
                _isUpdating = false;

                if (LauncherVersionText != null)
                    LauncherVersionText.IsHitTestVisible = true;
            }
        }


        // --- [В] УСТАНОВКА (САМОЗАМЕНА) ---
        private void ApplyUpdate(string tempFilePath)
        {
            try
            {
                if (!File.Exists(tempFilePath))
                {
                    SafeLog("[Update] Файл обновления не найден!", Brushes.Red);
                    ShowNotification("Ошибка обновления: файл не найден");
                    return;
                }

                if (new FileInfo(tempFilePath).Length < 1_000_000)
                {
                    SafeLog("[Update] Файл обновления поврежден!", Brushes.Red);
                    ShowNotification("Ошибка обновления: файл поврежден");
                    return;
                }

                try
                {
                    using var fs = File.Open(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                catch
                {
                    SafeLog("[Update] Файл обновления заблокирован!", Brushes.Red);
                    ShowNotification("Файл обновления занят системой");
                    return;
                }

                string currentExe = Process.GetCurrentProcess().MainModule!.FileName!;
                string batchPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SR_Updater.bat");

                string backupExe = currentExe + ".bak";

                string batchContent = $@"
@echo off
timeout /t 2 /nobreak > nul

:retry
del /f /q ""{currentExe}"" > nul 2>&1
if exist ""{currentExe}"" (
    timeout /t 1 /nobreak > nul
    goto retry
)

copy /y ""{currentExe}"" ""{backupExe}"" > nul 2>&1
copy /y ""{tempFilePath}"" ""{currentExe}"" > nul 2>&1

if not exist ""{currentExe}"" (
    copy /y ""{backupExe}"" ""{currentExe}""
    exit
)

start """" ""{currentExe}"" updated
del ""%~f0""
";

                File.WriteAllText(batchPath, batchContent);

                Thread.Sleep(300);

                SafeLog("[Update] Применение обновления...", Brushes.Lime);

                // запускаем встроенный updater
                UpdaterService.Run(tempFilePath);

                // закрываем текущую версию
                Application.Current.Shutdown();

                Task.Delay(600).ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        Application.Current.Shutdown());
                });
            }
            catch (Exception ex)
            {
                SafeLog($"[Update] Ошибка установки: {ex}", Brushes.Red);
                ShowNotification("Ошибка установки обновления");
            }
        }
        private async Task TryAutoUpdateSilent()
        {
            try
            {
                var master = await SyncWithMasterJson();
                if (master?.Installer == null)
                    return;

                if (master.Installer.Version == CurrentVersion)
                    return;

                SafeLog("[Update] Найдена новая версия. Тихое обновление...", Brushes.Orange);

                await DownloadAndApplyUpdate(master.Installer.DownloadUrl);
            }
            catch (Exception ex)
            {
                SafeLog("[Update] Silent update failed: " + ex.Message, Brushes.Gray);
            }
        }
        private void SafeLog(string message, SolidColorBrush color = null)
        {
            // Если мы вызвали метод не из главного потока (например, из процесса игры),
            // Dispatcher.BeginInvoke перенаправит задачу в UI-поток.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (LogText == null) return;

                    // 1. Используем стандартный цвет (AccentBlue), если другой не указан
                    var logColor = color;

                    if (logColor == null)
                    {
                        try
                        {
                            logColor = (SolidColorBrush)FindResource("AccentBlue");
                        }
                        catch
                        {
                            logColor = Brushes.LightBlue;
                        }
                    }
                    if (message.Contains("[CRASH]"))

                    WriteLauncherLogToFile(message);
                    var runMsg = new Run(message + Environment.NewLine) { Foreground = logColor };
                    LogText.Inlines.Add(runMsg);

                    // 5. Авто-скролл вниз
                    LogScroll?.ScrollToEnd();

                    // 6. Защита от переполнения: если логов > 300, удаляем старые
                    if (LogText.Inlines.Count > 600)
                    {
                        // Удаляем по 2 элемента (время + сообщение)
                        LogText.Inlines.Remove(LogText.Inlines.FirstInline);
                        LogText.Inlines.Remove(LogText.Inlines.FirstInline);
                    }
                }
                catch { /* Ошибки логов не должны вешать лаунчер */ }
            }));
        }



        // --- АВТОРИЗАЦИЯ ---
        private async Task MicrosoftLogin()
        {
            try
            {
                var loginHandler = new JELoginHandlerBuilder().Build();

                // Показываем в статусе, что ждем действий от юзера
                AppendLog("[Auth] Ожидание авторизации в браузере...", Brushes.Cyan);

                var session = await loginHandler.AuthenticateInteractively();

                Dispatcher.Invoke(() =>
                {
                    if (session != null)
                    {
                        DisplayNick.Text = session.Username;

                        // Проверка на корректность URL аватара
                        try
                        {
                            UserAvatarImg.ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri($"https://minotar.net/helm/{session.Username}/45.png"));
                        }
                        catch { /* Если сервис аватаров лежит, не падаем */ }

                        if (AccountTypeStatus != null)
                        {
                            AccountTypeStatus.Text = "PREMIUM";
                            AccountTypeStatus.Foreground = Brushes.Black;
                        }
                        if (AccountTypeBadge != null)
                        {
                            AccountTypeBadge.Background = new SolidColorBrush(Color.FromRgb(255, 170, 0));
                        }

                        ShowNotification($"Лицензия: {session.Username}");
                        CloseAuthWithAnimation();
                        IsLicensed = true;
                        CurrentSession = session;

                        AppendLog($"[Auth] Успешный вход: {session.Username}", Brushes.Lime);
                        SaveSettings(); // Сохраняем сессию сразу после успеха
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ShowNotification("Ошибка или отмена входа");
                    // Выводим конкретную ошибку в лог, чтобы понимать — это юзер закрыл окно или нет интернета
                    AppendLog($"[Auth] Сбой: {ex.Message}", Brushes.Yellow);
                });
            }
        }

        private async Task TrySilentLogin()
        {
            if (!IsLicensed) return;
            try
            {
                var loginHandler = new JELoginHandlerBuilder().Build();
                var session = await loginHandler.AuthenticateSilently();
                if (session != null)
                {
                    CurrentSession = session;
                    DisplayNick.Text = session.Username;
                    UserAvatarImg.ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri($"https://minotar.net/helm/{session.Username}/45.png"));
                }
            }
            catch { IsLicensed = false; }
        }
        
        private void OpenLastCrashReport()
        {
            try
            {
                string dir = System.IO.Path.Combine(AppDataPath, "crash_reports");
                if (!Directory.Exists(dir))
                    return;

                var file = new DirectoryInfo(dir)
                    .GetFiles("crash_*.txt")
                    .OrderByDescending(f => f.CreationTime)
                    .FirstOrDefault();

                if (file != null)
                    Process.Start("explorer.exe", $"/select,\"{file.FullName}\"");
                else
                    Process.Start("explorer.exe", dir);
            }
            catch { }
        }
        private void LogJvmArguments(Process gameProcess)
        {
            try
            {
                SafeLog("[JVM] Параметры запуска:", Brushes.Gray);
                SafeLog(gameProcess.StartInfo.Arguments, Brushes.Gray);
            }
            catch { }
        }
        // --- ЗАПУСК ИГРЫ ---
        private async void Play_Click(object sender, RoutedEventArgs e)
        {
            if (!await _playLock.WaitAsync(0)) return;

            // 0. Флаг, который скажет лаунчеру: игра запустилась успешно?
            bool isGameRunning = false;

            try
            {
                MainProgressBar.Visibility = Visibility.Visible;
                MainProgressBar.Value = 0;
                MainProgressBar.IsIndeterminate = true;

                PlayBtn.Visibility = Visibility.Collapsed;
                CancelBtn.Visibility = Visibility.Visible;
                SetVersionSelectionEnabled(false);

                _downloadCts = new CancellationTokenSource();
                SaveSettings();

                var opt = BuildLaunchOptions();
                bool hasInternet = await HasInternetAsync();
                opt.OfflineMode = !hasInternet || !opt.IsLicensed;

                if (string.IsNullOrEmpty(opt.JavaPath))
                    opt.JavaPath = await ResolveJavaPathAsync(opt, _downloadCts.Token);

                string? problem = DiagnoseLaunchEnvironment(opt, AppDataPath);
                if (problem != null) throw new Exception(problem);

                var gameProcess = await InstallGameAsync(opt, _downloadCts.Token);

                if (gameProcess != null)
                {
                    gameProcess.EnableRaisingEvents = true;
                    ConfigureProcess(gameProcess, opt);

                    await StartGameAsync(gameProcess);

                    // ⭐ 1. ПОДТВЕРЖДАЕМ ЗАПУСК
                    isGameRunning = true;

                    SafeLog("[LAUNCH] Игра запущена успешно!", Brushes.Green);
                    this.WindowState = WindowState.Minimized;

                    // ⭐ 2. ЗАПУСКАЕМ "СЛЕДИЛКУ" В ФОНЕ (она развернет окно ПОТОМ)
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            gameProcess.WaitForExit();
                            Dispatcher.Invoke(() =>
                            {
                                // Разворачиваем только когда игра ЗАКРЫЛАСЬ
                                if (this.WindowState == WindowState.Minimized)
                                {
                                    this.WindowState = WindowState.Normal;
                                    this.Activate();
                                    StatusLabel.Text = "Готов к запуску";
                                }
                            });
                        }
                        catch { /* Процесс мог закрыться слишком быстро */ }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                SafeLog("[LAUNCH] Операция отменена пользователем", Brushes.Orange);
            }
            catch (Exception ex)
            {
                SafeLog("[ERROR] " + ex.Message, Brushes.Red);
                ShowCrashDialog(CrashReport.Simple("Ошибка запуска", ex.Message));
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    // Сбрасываем прогресс
                    MainProgressBar.BeginAnimation(ProgressBar.ValueProperty, null);
                    MainProgressBar.Visibility = Visibility.Collapsed;
                    MainProgressBar.Value = 0;

                    // Если игра НЕ запустилась — возвращаем кнопку Play
                    if (!isGameRunning)
                    {
                        SetGameRunningUI(false);

                        CancelBtn.Visibility = Visibility.Collapsed;
                        if (CancelDownloadBtn != null)
                            CancelDownloadBtn.Visibility = Visibility.Collapsed;

                        PlayBtn.Visibility = Visibility.Visible;
                        PlayBtn.IsEnabled = true;

                        StatusLabel.Text = "Готов к запуску";
                    }
                    else
                    {
                        StatusLabel.Text = "Игра запущена";
                    }

                    SetVersionSelectionEnabled(true);

                    // Разворачиваем окно только если запуск сорвался
                    if (!isGameRunning && this.WindowState == WindowState.Minimized)
                    {
                        this.WindowState = WindowState.Normal;
                        this.Activate();
                    }
                });

                _playLock.Release();
            }
        }



        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadCts != null)
            {
                _downloadCts.Cancel();
                CancelBtn.IsEnabled = false;
                CancelDownloadBtn.IsEnabled = false; // Добавь и здесь для визуального отклика
                StatusLabel.Text = "Статус: Отмена...";
            }
        }




        private async void ReinstallJava_Click(object sender, RoutedEventArgs e)
        {
            if (IsGameAlreadyRunning())
            {
                ShowNotification("Закрой игру перед переустановкой Java");
                return;
            }

            try
            {
                SafeLog("[JAVA] Запрошена переустановка", Brushes.Orange);

                int javaVer = GetRequiredJavaMajor(_selectedVersion ?? "1.21.1");

                string runtimeDir = System.IO.Path.Combine(AppDataPath, "runtime", $"java{javaVer}");

                if (Directory.Exists(runtimeDir))
                {
                    SafeLog("[JAVA] Удаление старой версии...", Brushes.Gray);

                    try
                    {
                        Directory.Delete(runtimeDir, true);
                    }
                    catch (Exception ex)
                    {
                        SafeLog("[JAVA] Не удалось удалить runtime: " + ex.Message, Brushes.Red);
                        ShowNotification("Закрой лаунчер и попробуй снова");
                        return;
                    }
                }

                _javaDownloadCts = new CancellationTokenSource();

                SafeLog("[JAVA] Переустановка завершена", Brushes.LightGreen);
                ShowNotification("Java успешно переустановлена");
            }
            catch (OperationCanceledException)
            {
                SafeLog("[JAVA] Переустановка отменена", Brushes.Yellow);
            }
            catch (Exception ex)
            {
                SafeLog("[JAVA] Ошибка переустановки: " + ex.Message, Brushes.Red);
                ShowNotification("Ошибка установки Java");
            }
            finally
            {
                _javaDownloadCts = null;
            }
        }
        private async Task HandleGameCrashAsync(Process gameProcess)
        {
            SafeLog($"[CRASH] Код выхода: {gameProcess.ExitCode}", Brushes.OrangeRed);

            // 👇 ВСТАВИТЬ СЮДА
            string solution = gameProcess.ExitCode switch
            {
                1 => "Проверь Java и моды",
                -1 => "Недостаточно памяти",
                _ => "Посмотри логи"
            };

            var report = new CrashReport
            {
                Title = "Игра завершилась с ошибкой",
                Message = $"Код выхода: {gameProcess.ExitCode}",
                Solution = solution
            };
            // 👆 ДО ЭТОГО МЕСТА

            UI(() => ShowCrashDialog(report));

            if (!string.IsNullOrEmpty(report.Solution))
                SafeLog("[РЕШЕНИЕ] " + report.Solution, Brushes.LightBlue);

            await Task.CompletedTask;
        }
        private void ConfigureProcess(Process gameProcess, LaunchOptions opt)
        {
            bool console = ConsoleCheck.IsChecked == true;

            if (string.IsNullOrWhiteSpace(opt.JavaPath))
                throw new Exception("Java не подготовлена");

            if (!File.Exists(opt.JavaPath))
                throw new Exception("Файл Java не найден");

            if (console)
                opt.JavaPath = opt.JavaPath.Replace("javaw.exe", "java.exe");

            gameProcess.StartInfo.FileName = opt.JavaPath;
            gameProcess.StartInfo.UseShellExecute = false;
            gameProcess.StartInfo.CreateNoWindow = !console;
            gameProcess.StartInfo.RedirectStandardOutput = !console;
            gameProcess.StartInfo.RedirectStandardError = !console;
            gameProcess.StartInfo.RedirectStandardInput = !console;

            // 🔥 ВАЖНО — подписка на вывод
            if (!console)
            {
                gameProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        SafeLog(e.Data, Brushes.LightGray);
                };

                gameProcess.ErrorDataReceived += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;

                    if (e.Data.Contains("Exception") ||
                        e.Data.Contains("ERROR") ||
                        e.Data.Contains("Failed"))
                    {
                        SafeLog("[JVM ERROR] " + e.Data, Brushes.OrangeRed);
                    }
                    else
                    {
                        SafeLog(e.Data, Brushes.LightGray);
                    }
                };
            }

            SafeLog("[JVM] Path: " + opt.JavaPath, Brushes.Gray);
            SafeLog("[JVM] RAM: " + opt.RamMb + " MB", Brushes.Gray);
            SafeLog("[JVM] Версия: " + JavaResolver.GetJavaMajorVersion(opt.JavaPath), Brushes.Gray);
        }
        private async Task StartGameAsync(Process gameProcess)
        {

            LogJvmArguments(gameProcess);

            gameProcess.Start();
           
            // 🔥 запуск чтения stdout/stderr
            bool console = ConsoleCheck.IsChecked == true;

            if (!console)
            {
                gameProcess.BeginOutputReadLine();
                gameProcess.BeginErrorReadLine();
            }

            await Task.Delay(1500);

            if (gameProcess.HasExited)
                throw new Exception($"JVM завершилась сразу после запуска (код {gameProcess.ExitCode})");

            SetGameRunningUI(true);
            SafeLog("[LAUNCH] Игра запущена", Brushes.LightGreen);
        }
        private void MonitorGame(Process gameProcess)
        {
            _gameProcess = gameProcess;
            gameProcess.EnableRaisingEvents = true;

            gameProcess.Exited += async (s, ev) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    SetGameRunningUI(false);
                });

                if (gameProcess.ExitCode != 0)
                {
                    await HandleGameCrashAsync(gameProcess);
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        SafeLog("[LAUNCH] Игра закрыта", Brushes.Gray);
                    });
                }
            };
        }
        private async Task<string> ResolveJavaPathAsync(LaunchOptions opt, CancellationToken token)
        {
            int requiredJava = GetRequiredJavaMajor(opt.Version);

            // MANUAL
            if (_javaSource == JavaSourceType.Manual)
            {
                if (!string.IsNullOrWhiteSpace(_manualJavaPath) && File.Exists(_manualJavaPath))
                    return _manualJavaPath;

                throw new Exception("Java не выбрана вручную");
            }

            // SYSTEM
            if (_javaSource == JavaSourceType.System)
            {
                string? sys = FindSystemJava();

                if (!string.IsNullOrWhiteSpace(sys))
                    return sys;

                throw new Exception("Системная Java не найдена");
            }

            // BUNDLED (по умолчанию)
            // BUNDLED (по умолчанию)
            string? bundled = JavaResolver.GetExistingRuntime(requiredJava);

            if (!string.IsNullOrWhiteSpace(bundled))
            {
                SafeLog("[JAVA] Найдена установленная runtime", Brushes.LightGreen);
                return bundled;
            }

            SafeLog($"[JAVA] Требуется Java {requiredJava}", Brushes.Gray);
            SafeLog("[JAVA] Runtime не найдена, начинаю установку...", Brushes.Orange);

            var progress = new Progress<double>(p =>
            {
                StatusLabel.Text = $"Установка Java {Math.Round(p)}%";

                SafeLog($"[JAVA] Установка {Math.Round(p)}%", Brushes.LightBlue);
            });

            try
            {
                string path = await JavaResolver.EnsureBundledJavaAsync(
                    requiredJava,
                    progress,
                    token,
                    msg =>
                    {
                        SafeLog(msg, Brushes.Orange);
                        AppLogger.Log(msg);
                        if (msg.Contains("Попытка"))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                StatusLabel.Text = "Проблемы с сетью, повтор...";
                            });
                        }
                    }
                );

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    throw new Exception("Установка Java завершилась без результата");

                SafeLog("[JAVA] Установка успешно завершена", Brushes.LightGreen);
                return path;
            }
            catch (Exception ex)
            {
                SafeLog("[JAVA] Ошибка установки: " + ex.Message, Brushes.OrangeRed);
                throw;
            }
        }
        public class LaunchProfile
        {
            public string Name { get; set; }
            public string Version { get; set; }
            public int RamMb { get; set; }
            public string? JavaPath { get; set; }
        }

        private async Task<Process> InstallGameAsync(LaunchOptions opt, CancellationToken token)
        {
            if (_isInstalling) throw new InvalidOperationException("Установка уже выполняется");
            _isInstalling = true;

            try
            {
                SetState(LauncherState.Downloading);
                StatusLabel.Text = "Подготовка игры...";

                var service = new LauncherService(AppDataPath, msg => SafeLog(msg, Brushes.Orange));

                // ⭐ Теперь эта ошибка исчезнет
                service.ProgressChanged += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        MainProgressBar.IsIndeterminate = false;

                        // 1. Считаем процент заполнения сами
                        // Формула: (Выполнено / Всего) * 100
                        double percentage = 0;
                        if (e.TotalTasks > 0)
                        {
                            percentage = (double)e.ProgressedTasks / e.TotalTasks * 100;
                        }

                        // 2. Запускаем плавное заполнение полоски
                        DoubleAnimation smoothProgress = new DoubleAnimation
                        {
                            To = percentage,
                            Duration = TimeSpan.FromMilliseconds(450), // Время "доезда" полоски
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                        };

                        MainProgressBar.BeginAnimation(ProgressBar.ValueProperty, smoothProgress);

                        // 3. Обновляем текст (показываем и цифры, и проценты)
                        StatusLabel.Text = $"Загрузка: {e.ProgressedTasks}/{e.TotalTasks} ({(int)percentage}%)";
                    });
                };



                var process = await service.PrepareAndCreateProcessAsync(
                    opt.Version, opt, null, null, token);

                StatusLabel.Text = "Готово";
                SetState(LauncherState.Installing);

                return process;

            }
            catch (OperationCanceledException)
            {
                StatusLabel.Text = "Отменено";
                SetState(LauncherState.Canceled);
                throw;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Ошибка установки";
                SafeLog(ex.Message, Brushes.Red);
                SetState(LauncherState.Error, ex.Message);
                throw;
            }
            finally
            {
                _isInstalling = false;
                // !!! ОТСЮДА УДАЛИЛИ PlayBtn.IsEnabled = true и SetVersionSelectionEnabled(true)
                // Мы перенесем их в Play_Click, чтобы они сработали ПОСЛЕ закрытия игры
            }
        }

        private async Task<string> PrepareJavaAsync(LaunchOptions opt, CancellationToken token)
        {
            PlayBtn.IsEnabled = false;

            int requiredJava = GetRequiredJavaMajor(opt.Version);

            var javaProgress = new Progress<double>(p =>
            {
                StatusLabel.Text = $"Подготовка Java {Math.Round(p)}%";
            });

            // ==============================
            // 1️⃣ MANUAL JAVA
            // ==============================
            if (!string.IsNullOrWhiteSpace(_manualJavaPath))
            {
                SafeLog("[JAVA] Проверка вручную выбранной Java...", Brushes.Gray);

                if (!File.Exists(_manualJavaPath))
                    throw new Exception("Указанный файл Java не найден");

                var ver = JavaResolver.GetJavaMajorVersion(_manualJavaPath);

                if (ver == null)
                    throw new Exception("Не удалось определить версию выбранной Java");

                if (ver < requiredJava)
                    throw new Exception($"Для версии {opt.Version} требуется Java {requiredJava}");

                SafeLog($"[JAVA] Используется ручная Java {ver}", Brushes.LightGreen);

                opt.JavaPath = _manualJavaPath;
                return _manualJavaPath;
            }

            // ==============================
            // 2️⃣ BUNDLED JAVA
            // ==============================
            SafeLog("[JAVA] Проверка встроенного runtime...", Brushes.Gray);

            string bundledPath = JavaResolver.GetBundledJavaPath(requiredJava);

            if (!string.IsNullOrWhiteSpace(bundledPath) && File.Exists(bundledPath))
            {
                var ver = JavaResolver.GetJavaMajorVersion(bundledPath);

                if (ver >= requiredJava)
                {
                    SafeLog($"[JAVA] Используется runtime Java {ver}", Brushes.LightGreen);
                    opt.JavaPath = bundledPath;
                    return bundledPath;
                }
            }

            // ==============================
            // 3️⃣ СКАЧИВАНИЕ
            // ==============================
            SafeLog($"[JAVA] Установка Java {requiredJava}...", Brushes.Orange);

            string javaPath = await JavaResolver.EnsureBundledJavaAsync(
                requiredJava,
                javaProgress,
                token,
                msg => SafeLog(msg, Brushes.Gray)
            );

            if (!File.Exists(javaPath))
                throw new Exception("Java установлена некорректно");

            var detected = JavaResolver.GetJavaMajorVersion(javaPath);
            if (detected < requiredJava)
                throw new Exception("Ошибка установки Java");

            SafeLog($"[JAVA] Установлена Java {detected}", Brushes.LightGreen);

            opt.JavaPath = javaPath;
            return javaPath;
        }
        public static string GetBundledJavaPath(int version)
        {
            string dir = System.IO.Path.Combine(RuntimeRoot, $"java{version}");
            string path = System.IO.Path.Combine(dir, "bin", "javaw.exe");
            return File.Exists(path) ? path : null;
        }
        public static string RuntimeRoot =>
    System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SubReel",
        "runtime"
    );
        private void SaveCrashReport(CrashReport report)
        {
            try
            {
                string dir = System.IO.Path.Combine(AppDataPath, "crash_reports");

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string file = System.IO.Path.Combine(dir, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                File.WriteAllText(file, report.FullText);

                SafeLog("[CRASH] Отчёт сохранён: " + file, Brushes.Orange);
            }
            catch (Exception ex)
            {
                SafeLog("[CRASH] Ошибка сохранения: " + ex.Message, Brushes.Red);
            }
        }
        private string? DiagnoseLaunchEnvironment(LaunchOptions opt, string gamePath)
        {
            if (string.IsNullOrWhiteSpace(opt.JavaPath))
                return "Не найден путь к Java";

            if (!File.Exists(opt.JavaPath))
                return "Java отсутствует на диске";

            var javaVer = JavaResolver.GetJavaMajorVersion(opt.JavaPath);
            if (javaVer == null)
                return "Не удалось определить версию Java";

            int required = GetRequiredJavaMajor(opt.Version);
            if (javaVer < required)
                return $"Требуется Java {required}, найдена {javaVer}";

            if (opt.RamMb < 1024)
                return "Выделено слишком мало RAM";

            if (!Directory.Exists(gamePath))
                return "Папка игры не существует";

            if (IsGameAlreadyRunning())
                return "Игра уже запущена";

            return null;
        }
        private void LogStep(string message)
        {
            Log("[STEP] " + message);
        }
        private void CancelJavaDownload_Click(object sender, RoutedEventArgs e)
        {
            _javaDownloadCts?.Cancel();
        }
        private bool IsGameAlreadyRunning()
        {
            return _gameProcess != null && !_gameProcess.HasExited;
        }
        private void SetGameRunningUI(bool running)
        {
            if (running)
            {
                StatusLabel.Text = "ИГРА ЗАПУЩЕНА";

                PlayBtn.Visibility = Visibility.Collapsed;
                CancelBtn.Visibility = Visibility.Visible;

                PlayBtn.IsEnabled = false;
                KillGameBtn.IsEnabled = true;
            }
            else
            {
                StatusLabel.Text = "ГОТОВ К ЗАПУСКУ";

                PlayBtn.Visibility = Visibility.Visible;
                CancelBtn.Visibility = Visibility.Collapsed;

                PlayBtn.IsEnabled = true;
                KillGameBtn.IsEnabled = false;
            }
        }


        private string? _manualJavaPath;
        private void SaveLogToFile(string fileName)
        {
            try
            {
                string logFolder = System.IO.Path.Combine(AppDataPath, "logs");
                if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);

                // Чистим старые логи (оставляем последние 5), чтобы не забивать диск
                var files = new DirectoryInfo(logFolder).GetFiles("crash_*.txt")
                .OrderBy(f => f.CreationTime).ToList();

                if (files.Count > 5)
                    files[0].Delete();

                string fullPath = System.IO.Path.Combine(logFolder, fileName);

                var fullLog = new System.Text.StringBuilder();
                foreach (var inline in LogText.Inlines)
                {
                    if (inline is System.Windows.Documents.Run run) fullLog.Append(run.Text);
                    else if (inline is System.Windows.Documents.LineBreak) fullLog.AppendLine();
                }

                File.WriteAllText(fullPath, fullLog.ToString());
            }
            catch (Exception ex)
            {
                SafeLog($"[System] Не удалось сохранить файл лога: {ex.Message}", Brushes.Red);
            }
        }
        private async Task LoadMinecraftVersionsAsync()
        {
            if (_isSyncRunning) return;
            _isSyncRunning = true;

            if (VersionCacheManager.HasCache)
            {
                SafeLog("[CACHE] Мгновенная загрузка списка версий", Brushes.Gray);

                var path = new MinecraftPath(AppDataPath);
                var launcher = new MinecraftLauncher(path);

                var versions = await launcher.GetAllVersionsAsync();

                Dispatcher.Invoke(() => FillVersionList(versions));
            }

            try
            {
                var path = new CmlLib.Core.MinecraftPath(AppDataPath);
                var launcher = new CmlLib.Core.MinecraftLauncher(path);

                if (!VersionCacheManager.HasCache)
                    await VersionCacheManager.GetManifestJsonAsync(msg => SafeLog(msg));

                var collection = await launcher.GetAllVersionsAsync();

                try
                {
                    var versions = await launcher.GetAllVersionsAsync();
                    FillVersionList(versions);
                }
                catch
                {
                    SafeLog("Нет интернета — показываю локальные версии", Brushes.Orange);
                    LoadLocalVersions();
                }

                SafeLog($"[Debug] Версий получено", Brushes.Gray);

                Dispatcher.Invoke(() =>
                {
                    FillVersionList(collection);
                });
            }
            catch (Exception ex)
            {
                SafeLog($"[Versions] Ошибка: {ex.Message}", Brushes.Red);
            }
            finally
            {
                _isSyncRunning = false;
            }
        }
        private void LoadLocalVersions()
        {
            var versionsDir = System.IO.Path.Combine(AppDataPath, "versions");
            if (!Directory.Exists(versionsDir)) return;

            VersionListBox.Items.Clear();

            foreach (var dir in Directory.GetDirectories(versionsDir))
            {
                var name = System.IO.Path.GetFileName(dir);

                VersionListBox.Items.Add(new ListBoxItem
                {
                    Content = name,
                    Foreground = Brushes.White
                });
            }
        }
        private void KillGame_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Проверяем, существует ли процесс и не завершился ли он сам
                if (_gameProcess != null && !_gameProcess.HasExited)
                {
                    _gameProcess.Kill(); // Принудительно закрываем Java

                    // 1. Обновляем UI (Кнопка Kill)
                    Dispatcher.Invoke(() => {
                        KillGameBtn.IsEnabled = false;
                        ToolTipService.SetIsEnabled(KillGameBtn, true);
                    });

                    // 2. ОТКРЫВАЕМ ПАПКУ И ВЫДЕЛЯЕМ ФАЙЛ latest.log
                    string logsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    string latestLog = System.IO.Path.Combine(logsDir, "latest.log");

                    if (System.IO.File.Exists(latestLog))
                    {
                        // Открывает проводник и сразу подсвечивает нужный файл
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{latestLog}\"");
                    }
                    else if (System.IO.Directory.Exists(logsDir))
                    {
                        // Если файла нет, просто открываем папку
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{logsDir}\"");
                    }

                    // 3. Заметаем следы и чистим память
                    CleanMemory();
                    SafeLog("[System] Процесс игры принудительно завершен.", Brushes.Orange);
                    ShowNotification("ИГРА ОСТАНОВЛЕНА. ЛОГИ ОТКРЫТЫ.");

                    // 4. Возвращаем кнопку "Играть" и статус
                    Dispatcher.Invoke(() => {
                        PlayBtn.IsEnabled = true;
                        if (StatusLabel != null) StatusLabel.Text = "ГОТОВ К ЗАПУСКУ";
                    });
                }
                else
                {
                    ShowNotification("Игра сейчас не запущена");
                }
            }
            catch (Exception ex)
            {
                SafeLog($"[Error] Не удалось завершить процесс: {ex.Message}", Brushes.Red);
            }

        }

        private void CleanMemory()
        {
            try
            {
                // 1. Собираем весь неиспользуемый мусор во всех поколениях (0, 1, 2)
                GC.Collect();
                // 2. Ожидаем завершения всех фоновых задач по очистке
                GC.WaitForPendingFinalizers();
                // 3. Еще раз проходим сборщиком для закрепления результата
                GC.Collect();

                SafeLog("[System] Очистка памяти завершена.", Brushes.Gray);
            }
            catch { }
        }

        // Вспомогательный метод для визуального переключения в Оффлайн
        private void SetOfflineStatus()
        {
            Dispatcher.Invoke(() => {
                if (OnlineCount != null) OnlineCount.Text = "OFFLINE";
                if (OnlineCircle != null) OnlineCircle.Fill = Brushes.Gray;
                if (UpdateBadge != null) UpdateBadge.Visibility = Visibility.Collapsed;
            });
        }
        private void WriteLauncherLogToFile(string message)
        {
            try
            {
                string logDir = System.IO.Path.Combine(AppDataPath, "launcher_logs");
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                string file = System.IO.Path.Combine(
                    logDir,
                    $"launcher_{DateTime.Now:yyyyMMdd}.log"
                );

                File.AppendAllText(file,
                    $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
        }
        private void OpenCrashFolder()
        {
            try
            {
                string dir = System.IO.Path.Combine(AppDataPath, "crash_reports");

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var file = new DirectoryInfo(dir)
                    .GetFiles("crash_*.txt")
                    .OrderByDescending(f => f.CreationTime)
                    .FirstOrDefault();

                if (file != null)
                    Process.Start("explorer.exe", $"/select,\"{file.FullName}\"");
                else
                    Process.Start("explorer.exe", dir);
            }
            catch (Exception ex)
            {
                SafeLog("[CRASH] Не удалось открыть папку: " + ex.Message, Brushes.Red);
            }
        }
        private void Log(string text)
        {
            LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {text}\n";
            LogScroll.ScrollToEnd();
        }
        // Класс для чата (вынесен сюда из UI)
        public class ChatMessage
        {
            public string Nickname { get; set; } = "";
            public string Message { get; set; } = "";
            public string AvatarUrl => $"https://minotar.net/helm/{Nickname}/32.png";
        }
    }
}

using CmlLib.Core;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using DiscordRPC;
using DiscordRPC.Logging;
using System;
using System.Collections.Generic; // Обязательно для List<NewsItem>
using System.Diagnostics;
using System.IO;
using IO = System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Shapes;
using System.Windows.Documents;



namespace SubReel
{
    public partial class MainWindow
    {
        public string GetJavaPath()
        {
            // 1. Проверяем папку лаунчера (портативный режим)
            string localJava =IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "java.exe");
            if (File.Exists(localJava)) return localJava;

            // 2. Список системных путей (используем Environment для гибкости)
            var searchPaths = new List<string>
    {
        IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
        IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
        IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Adoptium"),
        // Проверка 32-битной папки на 64-битных системах
        IO.Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)", "Java")
    };

            foreach (var path in searchPaths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        // Ищем javaw.exe (он запускает игру без лишнего черного окна консоли)
                        var files = Directory.GetFiles(path, "javaw.exe", SearchOption.AllDirectories);
                        if (files.Length > 0) return files[0];
                    }
                }
                catch { /* Игнорируем ошибки доступа к папкам */ }
            }

            // 3. Последний шанс: если в системе прописана переменная PATH
            return "javaw";
        }


        private DiscordRpcClient? _rpcClient;

        // --- DISCORD RPC ---
        private void InitializeDiscordRPC()
        {
            _rpcClient = new DiscordRpcClient("1473266633484140574");
            _rpcClient.Logger = new ConsoleLogger() { Level = LogLevel.Warning };
            _rpcClient.Initialize();
            UpdateRPC("В главном меню", "Выбирает сборку", "icon_status");
        }

        public void UpdateRPC(string details, string state, string smallImg = "")
        {
            // 1. Проверяем, существует ли клиент и инициализирован ли он
            if (_rpcClient == null || !_rpcClient.IsInitialized) return;

            // 2. Логика иконки (если пусто — ставим дефолт)
            string finalSmallKey = string.IsNullOrEmpty(smallImg) ? "icon_status" : smallImg;

            try
            {
                // 3. Отправляем обновление в Discord
                _rpcClient.SetPresence(new RichPresence()
                {
                    Details = details,
                    State = state,
                    Assets = new Assets()
                    {
                        LargeImageKey = "logo",
                        LargeImageText = "SubReel Studio",
                        SmallImageKey = finalSmallKey,
                        SmallImageText = "SubReel Client"
                    },
                    // Указываем время начала, чтобы в Discord было "Играет уже 05:20"
                    Timestamps = Timestamps.Now
                });
            }
            catch (Exception ex)
            {
                // Если Discord закрылся или произошла ошибка, просто пишем в лог, не ломая лаунчер
                AppendLog($"[RPC] Ошибка обновления статуса: {ex.Message}", Brushes.Gray);
            }
        }



        // --- НАСТРОЙКИ И ОБНОВЛЕНИЯ ---
        public void SaveSettings()
        {
            try
            {
                if (!Directory.Exists(AppDataPath)) Directory.CreateDirectory(AppDataPath);

                // Адаптация: добавили SelectedVersion и Ram в анонимный объект
                var settings = new
                {
                    Nickname = NicknameBox.Text,
                    Ram = RamSlider.Value,
                    IsLicensed = IsLicensed,
                    SelectedVersion = _selectedVersion // Сохраняем выбранную версию
                };

                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings));
            }
            catch { }
        }

        private void LoadSettings()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(ConfigPath);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;

                        // Твоя логика никнейма
                        string nick = root.TryGetProperty("Nickname", out var nickProp) ? nickProp.GetString() ?? "Player" : "Player";
                        if (NicknameBox != null) NicknameBox.Text = nick;
                        if (DisplayNick != null) DisplayNick.Text = nick;
                        if (UserAvatarImg != null)
                        {
                            try { UserAvatarImg.ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri($"https://minotar.net/helm/{nick}/45.png")); }
                            catch { }
                        }

                        // Адаптация: загрузка RAM
                        if (root.TryGetProperty("Ram", out var ramProp) && RamSlider != null)
                        {
                            RamSlider.Value = ramProp.GetDouble(); // Возвращаем ползунок на место
                        }

                        // Адаптация: загрузка версии
                        if (root.TryGetProperty("SelectedVersion", out var verProp))
                        {
                            _selectedVersion = verProp.GetString() ?? "1.21.1";
                            if (SelectedVersionBottom != null) SelectedVersionBottom.Text = $"Vanilla {_selectedVersion}";
                        }

                        // Твоя логика статуса лицензии (без изменений)
                        if (root.TryGetProperty("IsLicensed", out var licensedProp))
                        {
                            IsLicensed = licensedProp.GetBoolean();
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
                catch { }
            }
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
                // 1. Качаем JSON (анти-кэш гарантирует свежие данные)
                string jsonString = await _httpClient.GetStringAsync(VersionUrl + "?t=" + DateTime.Now.Ticks);

                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var update = System.Text.Json.JsonSerializer.Deserialize<UpdateModel>(jsonString, options);

                if (update != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        // --- 2. ОБНОВЛЯЕМ ОНЛАЙН (всегда, когда скачали JSON) ---
                        if (OnlineCount != null)
                        {
                            // Теперь это берется из поля online_count твоего Gist
                            OnlineCount.Text = $"ОНЛАЙН: {update.OnlineCount}";

                            // Опционально: меняем цвет кружка, если онлайн 0 (сервер выключен)
                            OnlineCircle.Fill = update.OnlineCount > 0
                                ? (Brush)new BrushConverter().ConvertFrom("#20F289")
                                : Brushes.Gray;
                        }

                        // --- 3. ПРОВЕРЯЕМ ВЕРСИЮ ---
                        if (update.Version != CurrentVersion)
                        {
                            // Если кнопка уже видна, не перерисовываем её (чтобы анимация не дергалась)
                            if (UpdateBadge.Visibility != Visibility.Visible)
                            {
                                UpdateBadge.Visibility = Visibility.Visible;
                                LauncherVersionText.Text = $"ОБНОВИТЬ ДО {update.Version}";
                                UpdateBadge.Tag = update.DownloadUrl;

                                var sb = (Storyboard)this.Resources["PulseUpdateAnim"];
                                sb?.Begin();
                            }
                        }
                        else
                        {
                            // Если ты в Gist вернул 1.0.0, кнопка сама спрячется
                            UpdateBadge.Visibility = Visibility.Collapsed;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // В случае ошибки (нет интернета)
                Dispatcher.Invoke(() => {
                    if (OnlineCount != null) OnlineCount.Text = "OFFLINE";
                });
                SafeLog($"[System] Не удалось обновить данные: {ex.Message}", Brushes.Gray);
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


        private async void StartManualUpdate(string downloadUrl)
        {
            // Блокируем кнопку, чтобы не нажать дважды
            if (LauncherVersionText != null) LauncherVersionText.IsHitTestVisible = false;

            try
            {
                SafeLog("[Update] Начинаю загрузку новой версии...", Brushes.Cyan);

                // Путь во временную папку Windows
                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SubReelUpdate.exe");

                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        var totalRead = 0L;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes != -1)
                            {
                                int progress = (int)((double)totalRead / totalBytes * 100);
                                // Обновляем UI асинхронно
                                Dispatcher.BeginInvoke(new Action(() => {
                                    if (LaunchProgress != null) LaunchProgress.Value = progress;
                                    if (StatusLabel != null) StatusLabel.Text = $"ОБНОВЛЕНИЕ: {progress}%";
                                }));
                            }
                        }
                    }
                }

                SafeLog("[Update] Загрузка завершена. Перезапуск...", Brushes.Lime);
                ApplyUpdate(tempFile); // Тот самый метод с батником
            }
            catch (Exception ex)
            {
                SafeLog($"[Update] Ошибка загрузки: {ex.Message}", Brushes.Red);
                if (LauncherVersionText != null) LauncherVersionText.IsHitTestVisible = true;
            }
        }


        // --- [В] УСТАНОВКА (САМОЗАМЕНА) ---
        private void ApplyUpdate(string tempFilePath)
        {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(currentExe)) return;

            string batchPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SR_Updater.bat");

            string batchContent = $@"
@echo off
timeout /t 2 /nobreak > nul
del /f /q ""{currentExe}""
copy /y ""{tempFilePath}"" ""{currentExe}""
start """" ""{currentExe}""
del ""{batchPath}""
";

            try
            {
                // Сначала ПУТЬ (batchPath), потом СОДЕРЖИМОЕ (batchContent)
                File.WriteAllText(batchPath, batchContent, System.Text.Encoding.Default);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batchPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                // Закрываем приложение ПРАВИЛЬНО
                _rpcClient?.Dispose();
                Application.Current.Shutdown();
            }
            catch (Exception) // Добавь хотя бы пустой перехват
            {
            }
        }
        private void SafeLog(string message, SolidColorBrush? color = null)
        {
            // Если мы вызвали метод не из главного потока (например, из процесса игры),
            // Dispatcher.BeginInvoke перенаправит задачу в UI-поток.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (LogText == null) return;

                    // 1. Используем стандартный цвет (AccentBlue), если другой не указан
                    var logColor = color ?? (SolidColorBrush)FindResource("AccentBlue");

                    // 2. Формируем красивую строку с временем
                    string timePrefix = $"[{DateTime.Now:HH:mm:ss}] ";

                    // 3. Создаем "Run" — это кусочек текста в TextBlock
                    var runTime = new Run(timePrefix) { Foreground = Brushes.Gray, FontSize = 10 };
                    var runMsg = new Run(message + Environment.NewLine) { Foreground = logColor };

                    // 4. Добавляем в интерфейс
                    LogText.Inlines.Add(runTime);
                    LogText.Inlines.Add(runMsg);

                    // 5. Авто-скролл вниз
                    LogScroll?.ScrollToEnd();

                    // 6. Защита от переполнения: если логов > 300, удаляем старые
                    if (LogText.Inlines.Count > 300)
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

        // --- ЗАПУСК ИГРЫ ---
        private async void Play_Click(object sender, RoutedEventArgs e)
        {
            var heartbeat = (System.Windows.Media.Animation.Storyboard)this.Resources["HeartbeatAnim"];
            if (PlayBtn.IsEnabled == false) return;

            try
            {
                if (PlayBtn == null || StatusLabel == null) return;
                if (string.IsNullOrEmpty(_selectedVersion))
                {
                    ShowNotification("Сначала выбери версию игры!");
                    return;
                }

                PlayBtn.IsEnabled = false;
                heartbeat?.Begin(PlayBtn, true);

                UpdateRPC($"Подготовка: {_selectedVersion}", "Загрузка ресурсов...");
                StatusLabel.Text = "ПОДГОТОВКА...";
                if (LogText != null) { LogText.Text = ""; LogText.Foreground = Brushes.White; }
                if (LaunchProgress != null) { LaunchProgress.IsIndeterminate = true; LaunchProgress.Value = 0; }

                var launcher = new MinecraftLauncher(AppDataPath);
                var installerProgress = new Progress<InstallerProgressChangedEventArgs>(args =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        int percentage = args.TotalTasks > 0 ? (int)((double)args.ProgressedTasks / args.TotalTasks * 100) : 0;
                        if (FileStatusLabel != null)
                            FileStatusLabel.Text = $"Загрузка: {args.Name}";

                        SetProgressSmooth(percentage);
                        UpdateRPC($"Загрузка ресурсов: {percentage}%", $"Файл: {args.Name}", "icon_loading");

                        if (StatusLabel != null)
                            StatusLabel.Text = $"СКАЧИВАНИЕ... {percentage}%";
                    });
                });

                var sessionToUse = (IsLicensed && CurrentSession != null) ? CurrentSession : CmlLib.Core.Auth.MSession.CreateOfflineSession(NicknameBox?.Text ?? "Player");

                StatusLabel.Text = "ПРОВЕРКА ФАЙЛОВ...";
                UpdateRPC($"Версия: {_selectedVersion}", "Проверка целостности...");
                await launcher.InstallAsync(_selectedVersion, installerProgress, default);

                StatusLabel.Text = "ЗАПУСК ИГРЫ...";
                UpdateRPC("Запуск игры", $"Версия {_selectedVersion}");

                // --- ИСПРАВЛЕНИЕ: УМНЫЙ ПОИСК JAVA ---
                // Если в ui.cs у тебя есть метод GetJavaPath(), используй его здесь.
                // Если нет — используем безопасный дефолт.
                string javaPath = "java.exe";
                if (typeof(MainWindow).GetMethod("GetJavaPath") != null)
                {
                    javaPath = GetJavaPath(); // Вызываем твой метод поиска из UI/Logic
                }

                var launchOption = new MLaunchOption
                {
                    Session = sessionToUse,
                    JavaPath = javaPath, // Используем найденный путь
                    MaximumRamMb = (int)RamSlider.Value
                };

                var gameProcess = await launcher.CreateProcessAsync(_selectedVersion, launchOption);
                gameProcess.StartInfo.UseShellExecute = false;
                gameProcess.StartInfo.RedirectStandardOutput = true;
                gameProcess.StartInfo.RedirectStandardError = true;
                gameProcess.StartInfo.CreateNoWindow = !(ConsoleCheck?.IsChecked ?? false);

                DataReceivedEventHandler logHandler = (s, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            string data = args.Data;
                            SolidColorBrush logColor = Brushes.White;

                            if (data.Contains("ERROR") || data.Contains("Exception") || data.Contains("FATAL"))
                                logColor = Brushes.Red;
                            else if (data.Contains("WARN") || data.Contains("WARNING"))
                                logColor = Brushes.Yellow;
                            else if (data.Contains("INFO"))
                                logColor = (SolidColorBrush)FindResource("AccentBlue");

                            var run = new System.Windows.Documents.Run($"\n[{DateTime.Now:HH:mm:ss}] {data}") { Foreground = logColor };
                            if (LogText != null)
                            {
                                LogText.Inlines.Add(run);
                            }
                            LogScroll?.ScrollToEnd();
                        }));
                    
                    }
                };

                gameProcess.OutputDataReceived += logHandler;
                gameProcess.ErrorDataReceived += logHandler;

                gameProcess.Start();
                gameProcess.BeginOutputReadLine();
                gameProcess.BeginErrorReadLine();

                Dispatcher.Invoke(() => { this.WindowState = WindowState.Minimized; });

                gameProcess.EnableRaisingEvents = true;
                gameProcess.Exited += (s, args) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        this.WindowState = WindowState.Normal;
                        this.Activate();

                        PlayBtn.IsEnabled = true;   // ← ПЕРЕНЕСЛИ СЮДА
                        UpdateRPC("В главном меню", "Выбирает сборку");
                        StatusLabel.Text = "ИГРА ЗАВЕРШЕНА";
                        ShowNotification("С возвращением!");
                    });
                };

                UpdateRPC($"Играет в {_selectedVersion}", "В игре", "icon_play");
                heartbeat?.Stop(PlayBtn);
                if (LaunchProgress != null) { LaunchProgress.IsIndeterminate = false; LaunchProgress.Value = 100; }
                ShowNotification("Удачной игры!");
            }
            catch (Exception ex)
            {
                heartbeat?.Stop(PlayBtn);
                ShowNotification($"Ошибка запуска: {ex.Message}");

                // --- ДОБАВЛЕНО: ЛОГИРОВАНИЕ ОШИБКИ ---
                // Это поможет тебе понять, почему игра не запустилась (например, не хватило RAM)
                var errorRun = new System.Windows.Documents.Run($"\n[КРИТИЧЕСКАЯ ОШИБКА]: {ex.Message}") { Foreground = Brushes.Red };
                LogText?.Inlines.Add(errorRun);

                StatusLabel.Text = "ОШИБКА";
                UpdateRPC("Ошибка запуска", "Технический сбой");
            }
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

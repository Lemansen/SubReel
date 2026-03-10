using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.VersionMetadata;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;



namespace SubReel
{
#nullable enable
    public class ReleasePopup
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("force")]
        public bool Force { get; set; }

        [JsonPropertyName("show_once")]
        public bool ShowOnce { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("sub_message")]
        public string SubMessage { get; set; } = "";
    }
    public class MasterConfig
    {
        [JsonPropertyName("project_info")]
        public ProjectInfo? ProjectInfo { get; set; }

        // ИСПРАВЛЕНО: Привязываем к installer_config из JSON
        [JsonPropertyName("installer_config")]
        public UpdateModel? Installer { get; set; }

        [JsonPropertyName("launcher_status")]
        public LauncherStatus? Status { get; set; }

        [JsonPropertyName("news")]
        public List<NewsItem>? News { get; set; }
    }
    public class ProjectInfo
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    public class UpdateModel
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("download_url")] public string DownloadUrl { get; set; } = "";
        [JsonPropertyName("changelog")] public List<string> Changelog { get; set; } = new();
    }

    public class LauncherStatus
    {
        [JsonPropertyName("online_count")] public int OnlineCount { get; set; } = 0;
        [JsonPropertyName("server_status")] public string ServerStatus { get; set; } = "";
        [JsonPropertyName("recent_changelog")] public List<string> RecentChangelog { get; set; } = new();
        [JsonPropertyName("release_popup")] public ReleasePopup? ReleasePopup { get; set; }
    }
    public class NewsItem
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("category")] public string Category { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("accent_color")] public string AccentColor { get; set; } = "#FFCC00";
        [JsonPropertyName("changes")] public List<string> Changes { get; set; } = new();
        [JsonPropertyName("button_url")] public string ButtonUrl { get; set; } = "";
        [JsonPropertyName("button_text")] public string ButtonText { get; set; } = "";

        // Эти свойства ТЕПЕРЬ ВНУТРИ класса NewsItem
        [JsonIgnore]
        public Visibility ButtonVisibility => string.IsNullOrWhiteSpace(ButtonUrl) ? Visibility.Collapsed : Visibility.Visible;

        [JsonIgnore]

        public SolidColorBrush AccentBrush
        {
            get
            {
                try
                {
                    var converter = new BrushConverter();
                    var brushObj = converter.ConvertFrom(AccentColor ?? "#3374FF");
                    return (brushObj as SolidColorBrush) ?? Brushes.CornflowerBlue;
                }
                catch
                {
                    return Brushes.CornflowerBlue;
                }
            }
        }
    } // Конец класса NewsItem
    public class StarConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? "★" : "☆";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StarColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCC00")) // Золотой
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FFFFFF")); // Полупрозрачный белый
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    // --- ОСНОВНОЙ КЛАСС ОКНА ---
    public partial class MainWindow : Window
    {
        // состояние лаунчера
        // идёт установка файлов
        // защита от двойных кликов
        private JavaSourceType _javaSource = JavaSourceType.Bundled;
        // === контроль обновления UI прогресса ===
        private DateTime _lastUiUpdate = DateTime.MinValue;
        private CancellationTokenSource? _installCts; // токен отмены установки
        public readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".SubReelGame");
        private Process? _gameProcess; // Сюда сохраним запущенную игру
        private readonly string ConfigPath;
        private readonly string CurrentVersion = "0.1.2";
        private readonly Queue<string> _notificationQueue = new();
        private ProgressController _progress;
        private bool _isToast1Showing = false;
        private bool _isToast2Showing = false;
        private int _filesRemaining = 0;
        // Единая ссылка на твой Gist
        private readonly string MasterUrl = "https://gist.githubusercontent.com/Lemansen/e30a53d49f4d29eb89b89d739dbeb12b/raw/master.json";
        private readonly string _minecraftPath;
        private DispatcherTimer? _updateTimer;
        private static readonly HttpClient _httpClient =
            new HttpClient(new HttpClientHandler
            {
                UseProxy = false,
                Proxy = null,
                DefaultProxyCredentials = null
            });

        private LaunchStage _currentStage = LaunchStage.Idle;
        // состояние лаунчера
        // --- установка Minecraft ---
        private bool _isInstalling = false;
        private CancellationTokenSource? _downloadCts;

        // --- установка Java ---
        private CancellationTokenSource? _javaDownloadCts;
        private bool IsLicensed = true;
        private bool _isLaunching = false;
        private bool _isSyncRunning = false;
        private string _currentTab = "home";
        private readonly SemaphoreSlim _playLock = new SemaphoreSlim(1, 1);
        private MSession? CurrentSession;
        private string _selectedVersion = "1.21.1";
        public ObservableCollection<ChatMessage> Messages { get; set; } = new ObservableCollection<ChatMessage>();
        private static System.Threading.Mutex? _appMutex;
        private bool CanUpdateUi(int ms = 120)
        {
            if ((DateTime.Now - _lastUiUpdate).TotalMilliseconds < ms)
                return false;

            _lastUiUpdate = DateTime.Now;
            return true;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        private static async Task<bool> HasInternetAsync()
        {
            try
            {
                using var client = new HttpClient()
                {
                    Timeout = TimeSpan.FromSeconds(3)
                };

                using var resp = await client.GetAsync("https://launchermeta.mojang.com");
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // ⭐ 1. Восстановление аккаунта и аватара
                var account = AccountStorage.Load();

                if (account != null)
                {
                    DisplayNick.Text = account.Username;
                    await LoadUserAvatarAsync(account.Uuid);
                }

                // ⭐ 2. Быстрая загрузка версий из кеша
                if (VersionCacheManager.HasCache)
                {
                    var path = new MinecraftPath(AppDataPath);
                    var launcher = new MinecraftLauncher(path);
                    var versions = await launcher.GetAllVersionsAsync();
                }

                // ⭐ 3. Обновление из сети
                await VersionCacheManager.GetManifestJsonAsync(msg => SafeLog(msg));
            }
            catch (Exception ex)
            {
                SafeLog("[Startup] " + ex.Message, Brushes.Red);
            }
        }
        
        private void FocusExistingInstance()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                var processes = Process.GetProcessesByName(current.ProcessName);

                foreach (var proc in processes)
                {
                    if (proc.Id != current.Id && proc.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(proc.MainWindowHandle);
                        break;
                    }
                }
            }
            catch { }
        }
        private void TryRollbackIfBroken()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule!.FileName!;
                string backup = exe + ".bak";
                string successFlag = exe + ".ok";

                if (!File.Exists(backup))
                    return;

                if (File.Exists(successFlag))
                    return;

                SafeLog("[Update] Обнаружена незавершённая установка. Откат...", Brushes.Orange);

                File.Copy(backup, exe, true);
                File.Delete(backup);

                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true
                });

                Environment.Exit(0);
            }
            catch { }
        }
        private void ConfirmUpdateSuccess()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule!.FileName!;
                string backup = exe + ".bak";

                if (File.Exists(backup))
                {
                    File.Delete(backup);
                    SafeLog("[Update] Обновление успешно подтверждено", Brushes.Lime);
                }
            }
            catch { }
        }

        private string VersionCachePath =>
    Path.Combine(AppDataPath, "version_manifest.json");

        public MainWindow()
        {
            var args = Environment.GetCommandLineArgs();

            if (args.Length > 1 && args[1] == "updated")
            {
                try
                {
                    string exe = Process.GetCurrentProcess().MainModule!.FileName!;
                    File.WriteAllText(exe + ".ok", "ok");
                    ConfirmUpdateSuccess();
                }
                catch { }
            }

            if (args.Length > 1 && args[1] == "apply_update")
            {
                string updateExe = Process.GetCurrentProcess().MainModule!.FileName!;
                UpdaterService.Run(updateExe);
                Application.Current.Shutdown();
                return;
            }

            bool createdNew;

            _appMutex = new System.Threading.Mutex(
                true,
                "SubReelLauncher_SingleInstance",
                out createdNew);

            if (!createdNew)
            {
                // Уже запущен
                FocusExistingInstance();
                ShowNotification("Лаунчер уже запущен");
                Application.Current.Shutdown();
                return;
            }
            // Оставляем ваш WindowChrome
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                ResizeBorderThickness = new Thickness(6),
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SubReelLauncher/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
            TryRollbackIfBroken();
            System.Net.WebRequest.DefaultWebProxy = null;

            InitializeComponent();

            SafeLog("[NET] HttpClient создан (proxy disabled)", Brushes.Gray);

            // ✅ очистка старого файла докачки
            try
            {
                var part = Path.Combine(Path.GetTempPath(), "SubReelUpdate.exe.part");
                if (File.Exists(part))
                {
                    if (DateTime.Now - File.GetLastWriteTime(part) > TimeSpan.FromDays(1))
                        File.Delete(part);
                }
            }
            catch { }
            GlobalExceptionHandler.Initialize(
        msg => SafeLog(msg, Brushes.Red),
        msg => ShowNotification(msg)
    );
            // 🔥 Глобальный перехват ошибок UI
            Application.Current.DispatcherUnhandledException += (s, e) =>
            {
                SafeLog("[CRASH UI] " + e.Exception, Brushes.Red);

                ShowCrashDialog(new CrashReport
                {
                    Title = "Критическая ошибка интерфейса",
                    Solution = e.Exception.Message
                });

                e.Handled = true;
            };

            // 🔥 Ошибки фоновых потоков
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                SafeLog("[CRASH DOMAIN] " + e.ExceptionObject, Brushes.Red);
            };

            // 🔥 Ошибки async задач
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                SafeLog("[CRASH TASK] " + e.Exception, Brushes.Red);
                e.SetObserved();
            };
            SourceInitialized += (s, e) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
            };
            if (Resources["OnlinePulse"] is Storyboard sb)
                sb.Begin();
            ConfigPath = Path.Combine(AppDataPath, "config.json");
            if (AppVersionText != null) AppVersionText.Text = $"v{CurrentVersion}";
            this.StateChanged += (s, e) => UpdateWindowLayout();

            // ИСПРАВЛЕНО: Правильное обращение к системным событиям
            // В конструкторе MainWindow()
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() => {
                    // Запоминаем текущее состояние
                    var currentState = this.WindowState;

                    if (currentState == WindowState.Maximized)
                    {
                        // Сбрасываем в Normal и сразу возвращаем в Maximized.
                        // Это заставляет WPF пересчитать физические пиксели под новый DPI.
                        this.WindowState = WindowState.Normal;
                        this.WindowState = WindowState.Maximized;
                    }

                    // Всегда вызываем обновление геометрии
                    UpdateWindowLayout();

                }), DispatcherPriority.Render);
            };


            SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;

            // Сначала настраиваем размеры
            UpdateWindowLayout();

            // Логику загрузки данных запускаем ТОЛЬКО ОДИН РАЗ при старте
            this.Loaded += OnWindowLoaded;
        }
        private void SystemParameters_StaticPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "WorkArea") UpdateWindowLayout();
        }

        // ЭТОТ МЕТОД ТЕПЕРЬ ОТВЕЧАЕТ ТОЛЬКО ЗА ГЕОМЕТРИЮ
        private void UpdateWindowLayout()
        {
            if (!this.IsInitialized) return;

            // ШАГ 0: Сбрасываем старые лимиты в бесконечность.
            // Без этого окно может отказаться расширяться, если масштаб уменьшился.
            this.MaxWidth = double.PositiveInfinity;
            this.MaxHeight = double.PositiveInfinity;

            var workingArea = SystemParameters.WorkArea;

            // ШАГ 1: Обновляем лимиты под актуальную рабочую область
            this.MaxHeight = workingArea.Height;
            this.MaxWidth = workingArea.Width;

            if (this.WindowState == WindowState.Maximized)
            {
                // ШАГ 2: В полноэкранном режиме привязываемся к краям строго
                this.Top = workingArea.Top;
                this.Left = workingArea.Left;
                this.Width = workingArea.Width;
                this.Height = workingArea.Height;

                if (MainBackground != null)
                {
                    MainBackground.CornerRadius = new CornerRadius(0);
                    MainBackground.BorderThickness = new Thickness(0);
                }
            }
            else
            {
                // ШАГ 3: В обычном режиме возвращаем стандартный размер лаунчера
                this.Width = 1100;
                this.Height = 700;

                // Центрируем окно заново, так как центр при 100% и 125% — это разные координаты
                this.Left = (workingArea.Width - this.Width) / 2 + workingArea.Left;
                this.Top = (workingArea.Height - this.Height) / 2 + workingArea.Top;

                if (MainBackground != null)
                {
                    MainBackground.CornerRadius = new CornerRadius(20);
                    MainBackground.BorderThickness = new Thickness(2);
                }
            }
        }
        private async Task DownloadFileWithResume(
    string url,
    string finalFile,
    CancellationToken token)
        {
            string partFile = finalFile + ".part";

            long existingBytes = 0;
            if (File.Exists(partFile))
                existingBytes = new FileInfo(partFile).Length;

            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (existingBytes > 0)
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                token);

            response.EnsureSuccessStatusCode();

            long totalBytes = existingBytes + (response.Content.Headers.ContentLength ?? 0);

            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var file = new FileStream(
                partFile,
                FileMode.Append,
                FileAccess.Write,
                FileShare.None);

            byte[] buffer = new byte[81920];
            int read;

            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                await file.WriteAsync(buffer, 0, read, token);
                existingBytes += read;
            }
            file.Close();

            File.Move(partFile, finalFile, true);

            if (!File.Exists(finalFile))
                throw new Exception("Файл обновления не найден");

            if (new FileInfo(finalFile).Length < 1_000_000)
                throw new Exception("Файл обновления поврежден");
        }
        private async Task DownloadAndApplyUpdate(string url)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "SubReel_update.exe");

            await RetryHelper.RetryAsync<object?>(
                async () =>
                {
                    await DownloadFileWithResume(url, tempFile, CancellationToken.None);
                    return null;
                },
                3,
                2000,
                msg => SafeLog(msg, Brushes.Orange)
            );

            SafeLog("[Update] Загрузка завершена", Brushes.Lime);

            ApplyUpdate(tempFile);
        }

        // ВСЯ ВАША ЛОГИКА ЗАПУСКА (ОДНОКРАТНО)
        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            await TryAutoUpdateSilent();

            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromMinutes(1);
            _updateTimer.Tick += async (s, args) => await CheckForUpdates();
            _updateTimer.Start();

            var argsCmd = Environment.GetCommandLineArgs();
            if (argsCmd.Length > 1 && argsCmd[1].Contains("update"))
                ShowNotification("ОБНОВЛЕНИЕ УСТАНОВЛЕНО!");

            var master = await SyncWithMasterJson();
            if (master != null) await HandleReleasePopup(master);

            // ✅ СНАЧАЛА загрузка настроек
            SettingsManager.Load(ConfigPath);

            ApplySettingsToUI();
            UpdateConsoleIndicator();

            // ✅ ПОТОМ обновление индикатора
            UpdateConsoleIndicator();

            await TrySilentLogin();
            if (_currentTab == "home")
                SwitchTab(BuildsPanel); 

            if (ChatMessages != null)
                ChatMessages.ItemsSource = Messages;

            LoadNews();

            _newsTimer = new DispatcherTimer();
            _newsTimer.Interval = TimeSpan.FromMinutes(5);
            _newsTimer.Tick += (s, args) => LoadNews();
            _newsTimer.Start();

            await Task.Delay(1000);
            await CheckForUpdates();

            if (DisplayNick != null && DisplayNick.Text != "Player")
                ShowNotification($"РАДЫ ВИДЕТЬ, {DisplayNick.Text}!");
        }


        // Это событие срабатывает само, когда ты нажимаешь на "крестик" или закрываешь окно
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _appMutex?.ReleaseMutex();
                _appMutex?.Dispose();
                SaveSettings();
            }
            catch { }

            base.OnClosing(e);
        }
        private async Task HandleReleasePopup(MasterConfig master)
        {
            if (master?.Status?.ReleasePopup == null)
                return;

            var popup = master.Status.ReleasePopup;

            if (!popup.Enabled)
                return;

            string tagPath = Path.Combine(AppDataPath, "infovnachale.tag");
            bool alreadySeen = File.Exists(tagPath);

            if (popup.Force || (popup.ShowOnce && !alreadySeen))
            {
                // Заполняем текст
                WelcomeTitle.Text = popup.Title;
                WelcomeMessage.Text = popup.Message;
                WelcomeSubMessage.Text = popup.SubMessage;

                // Показываем окно
                WelcomeOverlay.Visibility = Visibility.Visible;
                WelcomeOverlay.Opacity = 1;

                if (popup.ShowOnce && !alreadySeen)
                {
                    try
                    {
                        if (!Directory.Exists(AppDataPath))
                            Directory.CreateDirectory(AppDataPath);

                        File.Create(tagPath).Close();
                    }
                    catch { }
                }
            }
        }
        private void CloseWelcome_Click(object sender, RoutedEventArgs e)
        {
            // Просто скрываем оверлей
            WelcomeOverlay.Visibility = Visibility.Collapsed;
        }

        private async Task<MasterConfig?> SyncWithMasterJson()
        {
            try
            {
                // 1. Проверка физического подключения
                if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                {
                    SafeLog("[System] Сетевой кабель не подключен или нет Wi-Fi.", Brushes.Orange);
                    SetOfflineStatus();
                    return null;
                }

                // 2. Запрос с анти-кэшем и заголовком (чтобы GitHub не отфутболил)
                // Добавь это в конструктор MainWindow, если еще не сделал:
                // _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SubReelLauncher/1.0");

                string urlWithTics = MasterUrl + "?t=" + DateTime.Now.Ticks;
                string json = await _httpClient.GetStringAsync(urlWithTics);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<MasterConfig>(json, options);

                if (data != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Обновляем индикатор сети
                        if (OnlineCircle != null)
                            OnlineCircle.Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#20F289");

                        // Обновляем счетчик онлайна
                        if (data.Status != null && OnlineCount != null)
                            OnlineCount.Text = $"{data.Status.OnlineCount} ONLINE";

                        // Подгружаем новости
                        if (data.News != null && NewsItemsControl != null)
                            NewsItemsControl.ItemsSource = data.News;

                        // Проверяем версию установщика
                        if (data.Installer != null && data.Installer.Version != CurrentVersion)
                        {
                            if (UpdateBadge != null)
                            {
                                UpdateBadge.Visibility = Visibility.Visible;
                                UpdateBadge.Tag = data.Installer.DownloadUrl; // Сохраняем ссылку для клика
                            }
                        }
                    });

                    return data;
                }
            }
            catch (HttpRequestException httpEx)
            {
                SafeLog($"[Network] Ошибка HTTP: {httpEx.StatusCode}. Проверьте доступ к GitHub.", Brushes.Red);
                SetOfflineStatus();
            }
            catch (Exception ex)
            {
                SafeLog($"[System] Ошибка синхронизации: {ex.Message}", Brushes.Gray);
                SetOfflineStatus();
            }
            return null;
        }

        // ... остальной код (CloseBtn_Click и т.д.)


        private DispatcherTimer? _newsTimer;

        // Это главный контейнер для всего JSON

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            Application.Current.Shutdown();
        }

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
            }

            // Принудительно обновляем геометрию, чтобы углы и размеры 
            // сразу подстроились под новое состояние
            UpdateWindowLayout();
        }

        // Переопределяем поведение при изменении состояния для коррекции UI
        // --- Исправление Maximize для WindowStyle.None ---

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (this.WindowState == WindowState.Maximized)
            {
                // 1. Убираем скругления фона (делаем 0)
                MainBackground.CornerRadius = new CornerRadius(0);

                // 2. Убираем обводку (BorderThickness), чтобы не было рамки по краям
                MainBackground.BorderThickness = new Thickness(0);

                // 3. Убираем Margin у корневого элемента (если там была тень или отступы)
                if (MainRoot != null) MainRoot.Margin = new Thickness(0);

                if (MaximizeBtn != null) MaximizeBtn.Content = "❐";
            }
            else
            {
                // 1. Возвращаем скругления (например, 20)
                MainBackground.CornerRadius = new CornerRadius(20);

                // 2. Возвращаем обводку (например, 2)
                MainBackground.BorderThickness = new Thickness(2);

                // 3. Возвращаем Margin, если он нужен для эффекта тени в оконном режиме
                // if (MainRoot != null) MainRoot.Margin = new Thickness(10); 

                if (MaximizeBtn != null) MaximizeBtn.Content = "▢";
            }
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0024) // WM_GETMINMAXINFO
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                GetMonitorInfo(monitor, monitorInfo);
                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;
                mmi.ptMaxPosition.x = Math.Abs(rcWorkArea.left - rcMonitorArea.left);
                mmi.ptMaxPosition.y = Math.Abs(rcWorkArea.top - rcMonitorArea.top);
                mmi.ptMaxSize.x = Math.Abs(rcWorkArea.right - rcWorkArea.left);
                mmi.ptMaxSize.y = Math.Abs(rcWorkArea.bottom - rcWorkArea.top);
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor = new RECT();
            public RECT rcWork = new RECT();
            public int dwFlags = 0;
        }
        private void ShowCrashDialog(CrashReport report)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (CrashReasonText != null)
                    CrashReasonText.Text = report.Title;

                if (CrashHelpText != null)
                    CrashHelpText.Text =
                        string.IsNullOrWhiteSpace(report.Solution)
                        ? "Посмотри crash-лог для подробностей."
                        : report.Solution;

                if (CrashOverlay != null)
                {
                    CrashOverlay.Visibility = Visibility.Visible;
                    CrashOverlay.Opacity = 0;

                    var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
                    CrashOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
                }
            });
        }
        public void UI(Action action)
        {
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.Invoke(action);
        }
        private void CloseCrashOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (CrashOverlay == null)
                return;

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (s, _) =>
            {
                CrashOverlay.Visibility = Visibility.Collapsed;
            };

            CrashOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        
        private async Task CheckForCrashAndShowAsync()
        {
            var report = await AnalyzeCrashAsync();

            if (report != null)
            {
                Dispatcher.Invoke(() => ShowCrashDialog(report));
            }
        }
        private string AvatarCachePath =>
    Path.Combine(AppDataPath, "avatar.png");
        private async Task LoadUserAvatarAsync(string uuid)
        {
            try
            {
                byte[] bytes;

                if (File.Exists(AvatarCachePath))
                {
                    bytes = await File.ReadAllBytesAsync(AvatarCachePath);
                }
                else
                {
                    string url = $"https://crafatar.com/avatars/{uuid}?size=128&overlay";
                    bytes = await _httpClient.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(AvatarCachePath, bytes);
                }

                using var ms = new MemoryStream(bytes);
                var image = new BitmapImage();

                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();

                UserAvatarImg.ImageSource = image;
            }
            catch { }
        }

        private void MainBackground_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            // двойной клик → maximize / normal
            if (e.ClickCount == 2)
            {
                MaximizeBtn_Click(sender, e);
                return;
            }

            // если окно было развернуто — сначала корректно восстанавливаем
            if (WindowState == WindowState.Maximized)
            {
                var mouseX = e.GetPosition(this).X;
                var percent = mouseX / ActualWidth;

                WindowState = WindowState.Normal;

                var cursor = System.Windows.Forms.Cursor.Position;

                Left = cursor.X - (Width * percent);
                Top = cursor.Y - 10;
            }

            try
            {
                DragMove();

                // ⭐ SNAP В ПОЛНЫЙ ЭКРАН ЕСЛИ ДОТАЩИЛИ К ВЕРХУ
                var screen = System.Windows.Forms.Screen.FromHandle(
                    new System.Windows.Interop.WindowInteropHelper(this).Handle);

                var workArea = screen.WorkingArea;

                if (Top <= workArea.Top + 2)
                {
                    WindowState = WindowState.Maximized;
                }
            }
            catch { }
        }
    }
}

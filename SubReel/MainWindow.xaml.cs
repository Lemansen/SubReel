using CmlLib.Core.Auth; // Для MSession
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;


namespace SubReel
{
    public partial class MainWindow : Window
    {
        // Базовые пути и настройки
        public readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SubReelGame");
        private readonly string ConfigPath;
        private readonly string CurrentVersion = "1.0.0";
        private readonly string VersionUrl = "https://gist.githubusercontent.com/Lemansen/d73f89af3312884d0d263a17c576faef/raw/update.json";
        private System.Windows.Threading.DispatcherTimer _updateTimer;
        private readonly string DownloadUrl = "ТВОЯ_ССЫЛКА_НА_EXE";
        private static readonly HttpClient _httpClient = new HttpClient();

        // Глобальные состояния
        private bool IsLicensed = false;
        private MSession? CurrentSession;
        private string _selectedVersion = "1.21.1";

        // Коллекция для чата
        public ObservableCollection<ChatMessage> Messages { get; set; } = new ObservableCollection<ChatMessage>();

        public class UpdateModel
        {
            [System.Text.Json.Serialization.JsonPropertyName("version")]
            public string Version { get; set; } = "";

            [System.Text.Json.Serialization.JsonPropertyName("download_url")]
            public string DownloadUrl { get; set; } = "";

            [System.Text.Json.Serialization.JsonPropertyName("changelog")]
            public List<string> Changelog { get; set; } = new();

            // Добавляем это поле для счетчика игроков
            [System.Text.Json.Serialization.JsonPropertyName("online_count")]
            public int OnlineCount { get; set; } = 0;
        }

        public MainWindow()
        {
            InitializeComponent();
            ConfigPath = System.IO.Path.Combine(AppDataPath, "config.json");
            AppVersionText.Text = $"v{CurrentVersion}";

            // Настраиваем таймер для проверки обновлений раз в минуту
            _updateTimer = new System.Windows.Threading.DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromMinutes(1);
            _updateTimer.Tick += async (s, e) => await CheckForUpdates();
            _updateTimer.Start();

            this.Loaded += async (s, e) =>
            {
                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1 && args[1].Contains("update"))
                {
                    ShowNotification("ОБНОВЛЕНИЕ УСТАНОВЛЕНО!");
                }

                LoadSettings();
                await TrySilentLogin();
                SwitchTab(BuildsPanel);
                ChatMessages.ItemsSource = Messages;
                InitializeDiscordRPC();
                LoadNews();

                _newsTimer = new DispatcherTimer();
                _newsTimer.Interval = TimeSpan.FromMinutes(5);
                _newsTimer.Tick += (s, args) => LoadNews();
                _newsTimer.Start();

                await Task.Delay(1000);
                await CheckForUpdates();

                if (DisplayNick.Text != "Player")
                {
                    ShowNotification($"РАДЫ ВИДЕТЬ, {DisplayNick.Text}!");
                }
            };
        }

        private DispatcherTimer? _newsTimer;

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            _rpcClient?.Dispose();
            Application.Current.Shutdown();
        }

        private void MainBackground_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
    }
}

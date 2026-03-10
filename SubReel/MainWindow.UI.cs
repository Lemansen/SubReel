using CmlLib.Core.VersionMetadata;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Linq;
#nullable enable

namespace SubReel
{

    public partial class MainWindow : Window
    {
        public enum LauncherState
        {
            Idle,          // готов к запуску
            Checking,      // проверка файлов
            Downloading,   // загрузка
            Installing,    // установка
            Launching,     // запуск игры
            Running,       // игра запущена
            Canceled,      // отменено
            Error          // ошибка
        }
        public ObservableCollection<BuildModel> FavoriteBuilds { get; set; } = new();
        public ObservableCollection<BuildModel> CustomBuilds { get; set; } = new();
        private LauncherState _state = LauncherState.Idle;
        private bool _isProcessingNotifications = false;
        private Random _rnd = new Random();
        private async Task<CrashReport?> AnalyzeCrashAsync()
        {
            try
            {
                string logFile = System.IO.Path.Combine(AppDataPath, "launcher_logs", "latest.log");

                if (!File.Exists(logFile))
                    return null;

                string text = await File.ReadAllTextAsync(logFile);

                if (text.Contains("OutOfMemoryError"))
                    return new CrashReport { Title = "Недостаточно памяти", FullText = text };

                if (text.Contains("Unable to locate Java runtime"))
                    return new CrashReport { Title = "Java не найдена", FullText = text };

                if (text.Contains("EXCEPTION_ACCESS_VIOLATION"))
                    return new CrashReport { Title = "Сбой JVM", FullText = text };

                return null;
            }
            catch
            {
                return null;
            }
        }
        private void SetState(LauncherState state, string? message = null)
        {
            _state = state;

            switch (state)
            {
                case LauncherState.Idle:
                    StatusLabel.Text = "ГОТОВ К ЗАПУСКУ";
                    StatusLabel.Foreground = Brushes.White;
                    PlayBtn.IsEnabled = true;
                    PlayBtn.Content = "ИГРАТЬ";
                    break;

                case LauncherState.Downloading:
                    StatusLabel.Text = message ?? "ЗАГРУЗКА...";
                    StatusLabel.Foreground = Brushes.White;
                    PlayBtn.IsEnabled = true;
                    PlayBtn.Content = "ОТМЕНИТЬ";
                    break;

                case LauncherState.Installing:
                    StatusLabel.Text = "УСТАНОВКА...";
                    StatusLabel.Foreground = Brushes.White;
                    PlayBtn.IsEnabled = true;
                    PlayBtn.Content = "ОТМЕНИТЬ";
                    break;

                case LauncherState.Launching:
                    {
                        bool isConsoleEnabled = ConsoleCheck?.IsChecked == true;

                        StatusLabel.Text = message ??
                            (isConsoleEnabled ? "ЗАПУСК С КОНСОЛЬЮ..." : "ЗАПУСК...");

                        StatusLabel.Foreground = isConsoleEnabled
                            ? new SolidColorBrush(Color.FromRgb(120, 200, 255))
                            : Brushes.White;

                        PlayBtn.IsEnabled = false;
                        PlayBtn.Content = "ЗАПУСК...";
                        break;
                    }

                case LauncherState.Running:
                    StatusLabel.Text = "ИГРА ЗАПУЩЕНА";
                    StatusLabel.Foreground = Brushes.White;
                    PlayBtn.IsEnabled = false;
                    PlayBtn.Content = "ИГРАЕТ";
                    break;

                case LauncherState.Canceled:
                    StatusLabel.Text = "ГОТОВ К ЗАПУСКУ";
                    StatusLabel.Foreground = Brushes.White;
                    PlayBtn.IsEnabled = true;
                    PlayBtn.Content = "ИГРАТЬ";
                    break;

                case LauncherState.Error:
                    StatusLabel.Text = message ?? "ОШИБКА";
                    StatusLabel.Foreground = Brushes.White;
                    PlayBtn.IsEnabled = true;
                    PlayBtn.Content = "ИГРАТЬ";
                    break;
            }

            UpdatePlayButtonVisual(state);

            bool shouldAnimate =
                state == LauncherState.Downloading ||
                state == LauncherState.Installing ||
                state == LauncherState.Launching;

            AnimatePlayButtonText(shouldAnimate);
        }
        private void SelectJava_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите java.exe или javaw.exe",
                Filter = "Java (java.exe;javaw.exe)|java.exe;javaw.exe",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                string selectedPath = dialog.FileName;

                // если выбрали java.exe — пробуем найти рядом javaw.exe
                if (selectedPath.EndsWith("java.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string javaw = selectedPath.Replace("java.exe", "javaw.exe");
                    if (File.Exists(javaw))
                        selectedPath = javaw;
                }

                int required = GetRequiredJavaMajor(_selectedVersion ?? "1.21.1");
                int? detected = JavaResolver.GetJavaMajorVersion(selectedPath);

                if (detected == null)
                    throw new Exception("Не удалось определить версию Java");

                if (detected < required)
                    throw new Exception($"Для этой версии Minecraft нужна Java {required}+");

                _manualJavaPath = selectedPath;
                SaveSettings();

                SafeLog($"[JAVA] Выбрана вручную: Java {detected}", Brushes.LightGreen);
                ShowNotification($"Java {detected} выбрана");
            }
            catch (Exception ex)
            {
                SafeLog("[JAVA] " + ex.Message, Brushes.Red);
                ShowNotification(ex.Message);
            }
        }

        private void UpdatePlayButtonVisual(LauncherState state)
        {
            if (PlayBtn == null) return;

            Color targetColor;

            switch (state)
            {
                case LauncherState.Idle:
                case LauncherState.Canceled:
                case LauncherState.Error:
                    targetColor = ((SolidColorBrush)FindResource("AccentBlue")).Color;
                    break;

                case LauncherState.Downloading:
                case LauncherState.Installing:
                    targetColor = Color.FromRgb(220, 60, 60); // режим отмены
                    break;

                case LauncherState.Launching:
                case LauncherState.Running:
                    targetColor = Color.FromRgb(120, 120, 120);
                    break;

                default:
                    targetColor = Color.FromRgb(0, 200, 120);
                    break;
            }

            AnimateButtonColor(targetColor);
        }
        private void AnimateButtonColor(Color targetColor)
        {
            // 1. Пытаемся получить текущую кисть
            if (PlayBtn.Background is not SolidColorBrush brush)
            {
                // Если фона нет или это градиент, создаем новую кисть
                brush = new SolidColorBrush(Colors.Transparent);
                PlayBtn.Background = brush;
            }
            else if (brush.IsFrozen)
            {
                // !!! ВОТ РЕШЕНИЕ ПРОБЛЕМЫ ЗАМОРОЗКИ !!!
                // Если кисть заморожена, создаем ее изменяемую копию (Clone)
                brush = brush.Clone();
                PlayBtn.Background = brush;
            }

            // 2. Теперь мы уверены, что кисть существует и НЕ заморожена.
            // Запускаем анимацию.
            var anim = new ColorAnimation
            {
                To = targetColor,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            // Анимируем свойство Color у кисти
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }


        private void AnimatePlayButtonText(bool enable)
        {
            if (PlayBtn == null) return;

            if (!enable)
            {
                PlayBtn.BeginAnimation(UIElement.OpacityProperty, null);
                PlayBtn.Opacity = 1;
                return;
            }

            DoubleAnimation pulse = new DoubleAnimation
            {
                From = 1.0,
                To = 0.55,
                Duration = TimeSpan.FromMilliseconds(650),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase()
            };

            PlayBtn.BeginAnimation(UIElement.OpacityProperty, pulse);
        }

        private async void LoadNews()
        {
            try
            {
                // 1. Используем НОВЫЙ URL (master.json)
                // Тот же самый, что мы прописали в начале MainWindow
                string url = MasterUrl + "?t=" + DateTime.Now.Ticks;

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                string jsonString = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(jsonString))
                    throw new Exception("Сервер прислал пустой файл");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                };

                // 2. ДЕСЕРИАЛИЗУЕМ В MasterConfig вместо List<NewsItem>
                var master = JsonSerializer.Deserialize<MasterConfig>(jsonString, options);

                // Достаем список новостей из объекта master
                var news = master?.News ?? new List<NewsItem>();

                if (news.Count == 0)
                    throw new Exception("Новости отсутствуют в мастер-файле");

                // ===== Сохраняем кэш (только список новостей, чтобы не ломать старый кэш) =====
                if (!Directory.Exists(AppDataPath))
                    Directory.CreateDirectory(AppDataPath);

                string newsJsonOnly = JsonSerializer.Serialize(news);
                File.WriteAllText(NewsCachePath, newsJsonOnly);

                Dispatcher.Invoke(() =>
                {
                    NewsItemsControl.ItemsSource = news;
                    AppendLog("НОВОСТИ: Синхронизация завершена успешно.", Brushes.Cyan);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    AppendLog($"ОШИБКА ЗАГРУЗКИ НОВОСТЕЙ: {ex.Message}", Brushes.Red);
                });

                // ===== Пытаемся загрузить кэш (здесь логика остается старой) =====
                try
                {
                    if (File.Exists(NewsCachePath))
                    {
                        string cachedJson = File.ReadAllText(NewsCachePath);
                        var cachedNews = JsonSerializer.Deserialize<List<NewsItem>>(cachedJson) ?? new List<NewsItem>();

                        if (cachedNews.Count > 0)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                NewsItemsControl.ItemsSource = cachedNews;
                                AppendLog("Загружены кэшированные новости.", Brushes.Yellow);
                            });
                            return;
                        }
                    }
                }
                catch { /* Игнорируем ошибки кэша */ }

                // Сообщение об ошибке, если кэша нет
                Dispatcher.Invoke(() =>
                {
                    NewsItemsControl.ItemsSource = new List<NewsItem>
            {
                new NewsItem { Title = "Ошибка сервера", Description = "Не удалось получить новости.", Category = "SYSTEM ERROR", AccentColor = "#FF4444" }
            };
                });
            }
        }

        private string NewsCachePath => System.IO.Path.Combine(AppDataPath, "news_cache.json");

        private void SetActiveMenuButton()
        {
            try
            {
                if (BtnBuilds != null) BtnBuilds.IsEnabled = true;
                if (BtnSettings != null) BtnSettings.IsEnabled = true;
                if (BtnNews != null) BtnNews.IsEnabled = true;

                if (_currentTab == "home" && BtnBuilds != null)
                    BtnBuilds.IsEnabled = false;

                if (_currentTab == "settings" && BtnSettings != null)
                    BtnSettings.IsEnabled = false;

                if (_currentTab == "news" && BtnNews != null)
                    BtnNews.IsEnabled = false;
            }
            catch { }
        }

        // --- НАВИГАЦИЯ И ПАНЕЛИ ---
        // 1. Улучшаем твой SwitchTab, чтобы он прятал ВСЕ страницы
        private void SwitchTab(FrameworkElement panel)
        {
            // Определяем текущую вкладку
            if (panel == BuildsPanel) _currentTab = "home";
            else if (panel == SettingsPanel) _currentTab = "settings";
            else if (panel == CreateBuildPage) _currentTab = "create"; // <--- Добавили
            else if (panel == NewsPanel) _currentTab = "news";
            else _currentTab = "other";

            // Скрываем абсолютно все панели (включая контейнер главной страницы)
            if (MainPageContent != null) MainPageContent.Visibility = Visibility.Collapsed;
            if (BuildsPanel != null) BuildsPanel.Visibility = Visibility.Collapsed;
            if (SettingsPanel != null) SettingsPanel.Visibility = Visibility.Collapsed;
            if (NewsPanel != null) NewsPanel.Visibility = Visibility.Collapsed;
            if (CommunityPanel != null) CommunityPanel.Visibility = Visibility.Collapsed;
            if (CreateBuildPage != null) CreateBuildPage.Visibility = Visibility.Collapsed;
            if (BuildSettingsPage != null) BuildSettingsPage.Visibility = Visibility.Collapsed;
            // Показываем нужную
            panel.Visibility = Visibility.Visible;

            // Если это страница создания, убеждаемся, что её родитель (MainPageContent) тоже виден, 
            // ЕСЛИ она находится внутри него. Если она лежит в корне — просто показываем её.

            SetActiveMenuButton();

            if (Resources["FadeIn"] is Storyboard sb)
                sb.Begin(panel);
        }


        private void ShowPanel(FrameworkElement panelToShow)
        {
            SwitchTab(panelToShow);
        }

        private void OpenNewsUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url &&
     !string.IsNullOrWhiteSpace(url))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        }
        private void NewsBtn_Click(object sender, RoutedEventArgs e)
        {
            AdditionalHeaderText.Text = "Новости"; // Меняем текст
            AdditionalHeaderPart.Visibility = Visibility.Visible; // Показываем добавку
            BreadcrumbsText.Visibility = Visibility.Collapsed;   // Прячем крошки (как ты и просил)

            ShowPanel(NewsPanel);
            SetActiveButton(BtnNews);
            LoadNews();
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            AdditionalHeaderText.Text = "Настройки";
            AdditionalHeaderPart.Visibility = Visibility.Visible;
            BreadcrumbsText.Visibility = Visibility.Collapsed;

            ShowPanel(SettingsPanel);
            SetActiveButton(BtnSettings);
            UpdateRamUI();
        }

        private void CommunityBtn_Click(object sender, RoutedEventArgs e)
        {
            //SwitchTab(CommunityPanel);
            //SetActiveButton(BtnCommunity);
            ShowNotification("Раздел «Сообщество» находится в разработке 🚧 ");
        }
        private void RestoreLastTab()
        {
            switch (_currentTab)
            {
                case "settings": SwitchTab(SettingsPanel); break;
                case "news": SwitchTab(NewsPanel); break;
                case "community": SwitchTab(CommunityPanel); break;
                default: SwitchTab(BuildsPanel); break;
            }
        }

        // --- ОКНО АВТОРИЗАЦИИ ---
        private void ProfileRegion_Click(object sender, MouseButtonEventArgs e)
        {
            if (AuthOverlay != null)
            {
                e.Handled = true;
                AuthOverlay.Visibility = Visibility.Visible;
                AuthOverlay.BeginAnimation(UIElement.OpacityProperty, null);
                AuthOverlay.Opacity = 0;

                if (AuthChild.RenderTransform is ScaleTransform st)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    st.ScaleX = 0.8;
                    st.ScaleY = 0.8;
                }

                if (this.Resources["FadeInAuth"] is Storyboard sb) sb.Begin(this);
            }
        }

        private void CloseAuthWithAnimation()
        {
            if (AuthOverlay == null) return;
            if (this.Resources["FadeOutAuth"] is Storyboard sb)
            {
                Storyboard closeSb = sb.Clone();
                closeSb.Completed += (s, e) => { AuthOverlay.Visibility = Visibility.Collapsed; };
                closeSb.Begin(this);
            }
            else { AuthOverlay.Visibility = Visibility.Collapsed; }
        }

        private void AuthOverlay_Close(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == sender) CloseAuthWithAnimation();
        }

        private void SaveAuth_Click(object sender, RoutedEventArgs e)
        {
            string inputNick = NicknameBox?.Text?.Trim() ?? "Player";

            if (string.IsNullOrWhiteSpace(inputNick) || inputNick.Length < 3)
            {
                ShowNotification("НИК СЛИШКОМ КОРОТКИЙ (МИН. 3)");
                return;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(inputNick, @"^[a-zA-Z0-9_]+$"))
            {
                ShowNotification("ТОЛЬКО ЛАТИНИЦА И ЦИФРЫ");
                return;
            }

            // ⭐ создаём UUID офлайн игрока
            string uuid = Guid.NewGuid().ToString("N");

            // ⭐ сохраняем аккаунт
            AccountStorage.Save(new AccountData
            {
                Username = inputNick,
                Uuid = uuid
            });

            CurrentSession = CmlLib.Core.Auth.MSession.CreateOfflineSession(inputNick);

            DisplayNick.Text = inputNick;

            // ⭐ загрузка аватара сразу
            _ = LoadUserAvatarAsync(uuid);

            ShowNotification($"ВЫ ВОШЛИ КАК {inputNick.ToUpper()}");
            CloseAuthWithAnimation();
        }


        private async void MSLogin_Click(object sender, RoutedEventArgs e)
        {
            ShowNotification("Запуск авторизации Microsoft...");
            await MicrosoftLogin();
        }

        private void SetActiveButton(Button activeButton)
        {
            // Список кнопок, которые относятся именно к ЛЕВОЙ панели
            Button[] menuButtons = { BtnBuilds, BtnCommunity, BtnSettings, BtnNews };

            // Проверяем: это кнопка из меню или нет?
            bool isMenuButton = false;
            foreach (var btn in menuButtons)
            {
                if (btn == activeButton)
                {
                    isMenuButton = true;
                    break;
                }
            }

            // Если нажата КАРТОЧКА (Ванилла), а не меню — выходим и ничего не сбрасываем!
            if (!isMenuButton) return;

            // Если это кнопка меню — обновляем теги для XAML
            foreach (var btn in menuButtons)
            {
                if (btn != null)
                    btn.Tag = (btn == activeButton) ? "Active" : null;
            }
        }

        // --- ВЫБОР ВЕРСИЙ ---
        private void SelectVersion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                // Выделяем саму карточку версии в центре (Tag="Selected")
                BtnCreateCustom.Tag = null;
                BtnServer.Tag = null;
                btn.Tag = "Selected";

                if (btn == BtnCreateCustom)
                {
                    if (SelectedVersionBottom != null) SelectedVersionBottom.Text = "Новая сборка";
                }
                else if (btn == BtnServer)
                {
                    if (SelectedVersionBottom != null) SelectedVersionBottom.Text = "Наш сервер";
                }

                SetActiveButtonVisual(btn);
            }
        }


        private void SetActiveButtonVisual(Button activeButton)
        {
            Button[] menuButtons = { BtnBuilds, BtnCommunity, BtnNews, BtnSettings };

            bool isMenuButton = false;
            foreach (var menuBtn in menuButtons)
            {
                if (menuBtn != null && menuBtn == activeButton)
                {
                    isMenuButton = true;
                    break;
                }
            }

            // Если это не кнопка меню (например, Ванилла) — НИЧЕГО НЕ ТРОГАЕМ
            if (!isMenuButton) return;

            foreach (var btn in menuButtons)
            {
                if (btn == null) continue;

                // Обновляем тег для анимаций XAML
                btn.Tag = (btn == activeButton) ? "Active" : null;

                var border = btn.Template.FindName("btnBorder", btn) as Border;
                var indicator = btn.Template.FindName("ActiveIndicator", btn) as Rectangle;

                if (btn == activeButton)
                {
                    if (border != null) border.Background = new SolidColorBrush(Color.FromArgb(40, 51, 116, 255));
                    if (indicator != null) indicator.Height = 20;
                    btn.Foreground = Brushes.White;
                }
                else
                {
                    if (border != null) border.Background = Brushes.Transparent;
                    if (indicator != null) indicator.Height = 0;
                    btn.Foreground = (SolidColorBrush)FindResource("TextGray");
                }
            }
        }

        // Свернуть окно
        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // Копирование логов в буфер обмена
        private void CopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (LogText == null || LogText.Inlines.Count == 0)
                {
                    ShowNotification("Лог пуст");
                    return;
                }

                // Используем StringBuilder для эффективной сборки большого текста
                var fullLog = new System.Text.StringBuilder();

                // Проходим по всем кусочкам текста (Run) в TextBlock
                foreach (var inline in LogText.Inlines)
                {
                    if (inline is System.Windows.Documents.Run run)
                    {
                        fullLog.Append(run.Text);
                    }
                    else if (inline is System.Windows.Documents.LineBreak)
                    {
                        fullLog.AppendLine();
                    }
                }

                string result = fullLog.ToString();

                if (!string.IsNullOrEmpty(result))
                {
                    Clipboard.SetText(result);
                    ShowNotification("Весь лог скопирован в буфер!");
                }
            }
            catch (Exception ex)
            {
                ShowNotification("Ошибка копирования");
                // Записываем ошибку в сам лог, чтобы понять, что пошло не так
                SafeLog($"[System] Ошибка буфера обмена: {ex.Message}", Brushes.Red);
            }
        }

        private void UpdateSidebarSelection(Button selectedButton)
        {
            // Список всех ваших кнопок в боковом меню
            var buttons = new[] { BtnBuilds, BtnCommunity, BtnNews, BtnSettings };

            foreach (var btn in buttons)
            {
                btn.Tag = null; // Сбрасываем выделение у всех
            }

            selectedButton.Tag = "Active"; // Выделяем нажатую
        }
        // 1. Переход с Главной на страницу Создания
        // 1. Вход в меню создания (нажимаем "Создать" в главном меню)
        private void CreateCustomVersion_Click(object sender, RoutedEventArgs e)
        {
            // Показываем первую часть: SubRell Studio | Создание
            AdditionalHeaderPart.Visibility = Visibility.Visible;
            AdditionalHeaderText.Text = "Создание";

            // Скрываем вторую часть (она пока не нужна)
            BreadcrumbsText.Visibility = Visibility.Collapsed;

            // Сброс видимости панелей контента
            CreationTypeGrid.Visibility = Visibility.Visible;
            CreationDetailsArea.Visibility = Visibility.Collapsed;
            CreatePageTitle.Text = "ВЫБЕРИТЕ СПОСОБ СОЗДАНИЯ";

            SwitchTab(CreateBuildPage);
            SafeLog("[UI] Переход в меню создания сборки", Brushes.AliceBlue);
        }

        // 2. Выбор конкретного типа (Custom, Modrinth и т.д.)
        private void SelectCreationType_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string type)
            {
                CreationTypeGrid.Visibility = Visibility.Collapsed;
                CreationDetailsArea.Visibility = Visibility.Visible;

                // Включаем вторую крошку: SubRell Studio | Создание | [Тип]
                BreadcrumbsText.Visibility = Visibility.Visible;

                switch (type)
                {
                    case "Custom":
                        CreatePageTitle.Text = "СОЗДАНИЕ: СВОЯ СБОРКА";
                        SubHeaderText.Text = "Своя сборка"; // Текст в самый верх
                        CreationModeText.Text = "Режим: Своя сборка"; // Текст в карточку превью
                        MiniPreviewIcon.Text = "🛠";
                        break;

                    case "Modrinth":
                        CreatePageTitle.Text = "СОЗДАНИЕ: MODRINTH";
                        SubHeaderText.Text = "Modrinth";
                        CreationModeText.Text = "Режим: Modrinth";
                        MiniPreviewIcon.Text = "M";
                        break;

                    case "Curse":
                        CreatePageTitle.Text = "СОЗДАНИЕ: CURSEFORGE";
                        SubHeaderText.Text = "CurseForge";
                        CreationModeText.Text = "Режим: CurseForge";
                        MiniPreviewIcon.Text = "C";
                        break;

                    case "Secret":
                        CreatePageTitle.Text = "СОЗДАНИЕ: НОВАЯ ФИЧА";
                        SubHeaderText.Text = "В планах";
                        CreationModeText.Text = "Режим: В планах";
                        MiniPreviewIcon.Text = "✨";
                        break;
                }
            }
        }

        // 3. Кнопка "Назад" внутри конструктора (возврат к плиткам)
        private void BackToCreationType_Click(object sender, RoutedEventArgs e)
        {
            CreationDetailsArea.Visibility = Visibility.Collapsed;
            CreationTypeGrid.Visibility = Visibility.Visible;

            CreatePageTitle.Text = "ВЫБЕРИТЕ СПОСОБ СОЗДАНИЯ";

            // Прячем только последнюю часть крошек
            BreadcrumbsText.Visibility = Visibility.Collapsed;
        }

        // 4. Полный выход (Кнопка X или завершение создания)
        private void BackToMain_Click(object sender, RoutedEventArgs e)
        {
            // Прячем всю цепочку крошек
            AdditionalHeaderPart.Visibility = Visibility.Collapsed;
            BreadcrumbsText.Visibility = Visibility.Collapsed;

            SwitchTab(BuildsPanel);
            SetActiveButton(BtnBuilds);

            if (BuildNameInput != null) BuildNameInput.Text = "My New Pack";
        }
        private void BackToBuilds_Click(object sender, RoutedEventArgs e)
        {
            BackToMain_Click(sender, e);
        }
        // Проверь, чтобы название СТРОГО совпадало с тем, что в XAML
        // 1. В методе создания сборки убираем ручное добавление кнопок
        private void ConfirmCreate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(BuildNameInput.Text))
            {
                ShowNotification("УКАЖИТЕ НАЗВАНИЕ СБОРКИ!");
                return;
            }

            string name = BuildNameInput.Text;
            string version = ((ComboBoxItem)VersionSelector.SelectedItem).Content.ToString();
            string loader = ((ComboBoxItem)LoaderSelector.SelectedItem).Content.ToString();

            // Создаем модель данных
            BuildModel build = new BuildModel(name, version, loader);

            // Добавляем ТОЛЬКО в менеджер данных
            BuildManager.AddBuild(build);

            // Обновляем списки (ItemsControl сами перерисуют карточки)
            RefreshBuildsUI();

            ShowNotification($"СБОРКА '{name.ToUpper()}' СОЗДАНА");
            BackToMain_Click(null, null);
        }

        // 2. Исправляем обработчик избранного (берем данные из DataContext)
        private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            // В ItemsControl источником данных (DataContext) для кнопки является сама модель BuildModel
            if (sender is FrameworkElement element && element.DataContext is BuildModel build)
            {
                build.IsFavorite = !build.IsFavorite;

                // Пересобираем списки, чтобы карточка перепрыгнула из "Созданных" в "Избранное"
                RefreshBuildsUI();
                string status = build.IsFavorite ? "ДОБАВЛЕНО В ИЗБРАННОЕ" : "УДАЛЕНО ИЗ ИЗБРАННОГО";
                ShowNotification(status);
                SafeLog($"[UI] Сборка {build.Name} {(build.IsFavorite ? "добавлена в" : "удалена из")} избранного", Brushes.Gray);
            }
        }
        // --- ОБРАБОТЧИКИ ДЛЯ КАРТОЧЕК СБОРОК ---

        // 1. Выбор карточки (Клик по всей области кнопки)
        private void SelectBuild_Click(object sender, RoutedEventArgs e)
        {
            // В ItemsControl DataContext кнопки — это объект BuildModel
            if (sender is FrameworkElement fe && fe.DataContext is BuildModel clickedBuild)
            {
                // Снимаем выделение со всех сборок через менеджер или напрямую в коллекции
                foreach (var build in BuildManager.GetAllBuilds())
                {
                    build.IsSelected = false;
                }

                // Выделяем ту, по которой кликнули
                clickedBuild.IsSelected = true;

                // Обновляем текст в нижней панели (если нужно)
                if (SelectedVersionBottom != null)
                    SelectedVersionBottom.Text = $"{clickedBuild.Name} ({clickedBuild.Version})";

                SafeLog($"[UI] Выбрана сборка: {clickedBuild.Name}", Brushes.AliceBlue);
            }
        }
        // 2. Обработчик нажатия на "Настройки" (Шестеренка)
        private void OpenBuildSettings_Click(object sender, RoutedEventArgs e)
        {
            // 1. Получаем данные сборки, на которую нажали
            var button = sender as Button;
            if (button?.DataContext is BuildModel selectedBuild)
            {
                // 2. Скрываем главный список и панель создания
                BuildsPanel.Visibility = Visibility.Collapsed;
                CreateBuildPage.Visibility = Visibility.Collapsed;

                // 3. Показываем страницу настроек
                BuildSettingsPage.Visibility = Visibility.Visible;
                BuildSettingsPage.Opacity = 1; // Можно добавить анимацию FadeIn

                // 4. Обновляем хлебные крошки (Breadcrumbs)
                AdditionalHeaderPart.Visibility = Visibility.Visible;
                AdditionalHeaderText.Text = "Настройки";

                BreadcrumbsText.Visibility = Visibility.Visible;
                SubHeaderText.Text = selectedBuild.Name; // Имя сборки в заголовке

                // 5. (Опционально) Сохраняем ссылку на текущую сборку, чтобы знать что удалять/менять
                this.Tag = selectedBuild;
            }
        }
        private BuildModel _currentEditingBuild;
        private void OpenBuildFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditingBuild != null)
            {
                // Логика открытия папки через Process.Start
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "builds", _currentEditingBuild.Name);
                if (System.IO.Directory.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", path);
            }
        }
        private void PlaySelectedBuild_Click(object sender, RoutedEventArgs e)
        {
            // Логика запуска сборки
        }
        private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
        {
            // Логика открытия папки mods через Process.Start
        }
        private void DeleteBuild_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditingBuild == null) return;

            // Здесь твоя логика удаления из BuildManager и из коллекции
            // BuildManager.Delete(_currentEditingBuild);
            // CustomBuilds.Remove(_currentEditingBuild);

            BackToMain_Click(null, null); // Возвращаемся в меню
        }
        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            // Проверка на null всех вкладок
            if (TabMain == null || TabSettings == null || TabResourcePacks == null ||
                TabWorlds == null || TabMods == null || TabScrin == null) return;

            var rb = sender as RadioButton;
            if (rb == null || rb.Tag == null) return;

            // Скрываем абсолютно все вкладки перед показом нужной
            TabMain.Visibility = Visibility.Collapsed;
            TabSettings.Visibility = Visibility.Collapsed;
            TabResourcePacks.Visibility = Visibility.Collapsed;
            TabWorlds.Visibility = Visibility.Collapsed;
            TabMods.Visibility = Visibility.Collapsed;
            TabScrin.Visibility = Visibility.Collapsed;

            // Показываем нужную по Tag, который прописан в RadioButton в XAML
            switch (rb.Tag.ToString())
            {
                case "Main": TabMain.Visibility = Visibility.Visible; break;
                case "Settings": TabSettings.Visibility = Visibility.Visible; break;
                case "ResourcePacks": TabResourcePacks.Visibility = Visibility.Visible; break;
                case "Worlds": TabWorlds.Visibility = Visibility.Visible; break;
                case "Mods": TabMods.Visibility = Visibility.Visible; break; // Была ошибка тут
                case "SCrin": TabScrin.Visibility = Visibility.Visible; break; // И тут (регистр важен!)
            }
        }

        private void OpenClientFolder_Click(object sender, RoutedEventArgs e)
        {

        }
        private void CreateBtnServer_Click(object sender, RoutedEventArgs e)
        {
            ShowNotification("Этот раздел находится в разработке!");
        }

        private void RefreshBuildsUI()
        {
            // Проверка на null, чтобы не упало при инициализации окна
            if (CustomBuildsContainer == null || CustomBuilds == null) return;

            // Привязываем коллекцию к UI, если еще не привязана
            if (CustomBuildsContainer.ItemsSource == null)
                CustomBuildsContainer.ItemsSource = CustomBuilds;

            // Берем текст, убираем пробелы по краям
            string search = SearchBuildsBox.Text.ToLower().Trim();

            // Получаем список всех сборок
            var allBuilds = BuildManager.GetAllBuilds();

            // Фильтруем и сортируем:
            // Сначала фильтр по имени/версии, потом ИЗБРАННЫЕ ВВЕРХ, потом по алфавиту
            var sortedList = allBuilds
                .Where(b => string.IsNullOrEmpty(search) ||
                            b.Name.ToLower().Contains(search) ||
                            b.Version.ToLower().Contains(search))
                .OrderByDescending(b => b.IsFavorite) // true (избранное) будет выше false
                .ThenBy(b => b.Name)
                .ToList();

            // Синхронизируем ObservableCollection
            CustomBuilds.Clear();
            foreach (var build in sortedList)
            {
                CustomBuilds.Add(build);
            }
        }
        // Обработчик поиска
        private void SearchBuildsBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshBuildsUI();
        }
        // Для клика по самой карточке (открытие)
        private void OpenBuild_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var build = (BuildModel)btn.Tag;
            MessageBox.Show($"Открываем сборку: {build.Name}");
        }
        // Исправленная анимация (принимает FrameworkElement для доступа к свойствам анимации)
        private void ShowPanelWithAnimation(FrameworkElement panel)
        {
            if (panel == null) return;

            panel.Visibility = Visibility.Visible;
            panel.Opacity = 0; // Сбрасываем перед началом

            var sb = new Storyboard();

            // Анимация прозрачности
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350));
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));

            // Анимация движения
            var move = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            // Важно: убедись, что в XAML у NewsPanel прописан <Grid.RenderTransform><TranslateTransform/></Grid.RenderTransform>
            Storyboard.SetTargetProperty(move,
                new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

            sb.Children.Add(fade);
            sb.Children.Add(move);

            Storyboard.SetTarget(fade, panel);
            Storyboard.SetTarget(move, panel);

            sb.Begin();
        }

        // --- ПОЛЯ ВВОДА И ПОЛЗУНКИ ---
        private void RamInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (RamSlider == null || RamInput == null) return;
            if (int.TryParse(RamInput.Text, out int result))
            {
                if (result >= RamSlider.Minimum && result <= RamSlider.Maximum)
                {
                    RamSlider.Value = result;
                    if (this.Resources["AccentBlue"] is SolidColorBrush accentBrush) RamInput.BorderBrush = accentBrush;
                }
                else { RamInput.BorderBrush = Brushes.Red; }
            }
        }

        private void NicknameBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var regex = new System.Text.RegularExpressions.Regex("[^a-zA-Z0-9_]");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void NicknameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (NicknameBox == null) return;
            if (NicknameBox.Text.Length >= 3) NicknameBox.BorderBrush = (SolidColorBrush)FindResource("AccentBlue");
            else NicknameBox.BorderBrush = (SolidColorBrush)FindResource("BorderSharp");
        }

        // --- УВЕДОМЛЕНИЯ И АНИМАЦИИ ---
        public void ShowNotification(string message)
        {
            if (NotificationToast == null || NotificationToast2 == null)
                return;

            _notificationQueue.Enqueue(message);
            TryShowNotification();
        }
        private void TryShowNotification()
        {
            // если первое пустое — показываем в первом
            if (!_isToast1Showing && _notificationQueue.Count > 0)
            {
                ShowToast(NotificationToast, NotificationText, 30, () =>
                {
                    _isToast1Showing = false;
                    TryShowNotification();
                });

                _isToast1Showing = true;
                return;
            }

            // если первое занято, но второе свободно
            if (_isToast1Showing && !_isToast2Showing && _notificationQueue.Count > 0)
            {
                // поднимаем первое выше
                NotificationToast.BeginAnimation(Canvas.TopProperty,
                    new DoubleAnimation(30, -50, TimeSpan.FromMilliseconds(300)));

                ShowToast(NotificationToast2, NotificationText2, 30, () =>
                {
                    _isToast2Showing = false;

                    // возвращаем первое вниз если осталось одно
                    if (_isToast1Showing)
                    {
                        NotificationToast.BeginAnimation(Canvas.TopProperty,
                            new DoubleAnimation(-50, 30, TimeSpan.FromMilliseconds(300)));
                    }

                    TryShowNotification();
                });

                _isToast2Showing = true;
            }
        }
        private void ShowToast(Border toast, TextBlock text, double top, Action onClose)
        {
            string message = _notificationQueue.Dequeue();
            text.Text = message;

            // 👉 ВАЖНО: заставляем WPF измерить размер тоста
            toast.UpdateLayout();

            double toastWidth = toast.ActualWidth > 0 ? toast.ActualWidth : 320;
            double centerX = (this.ActualWidth - toastWidth) / 2;
            Canvas.SetLeft(toast, centerX);

            // 👉 правильная стартовая позиция (выше экрана полностью)
            double startY = -toast.ActualHeight - 20;

            var slideDown = new DoubleAnimation(startY, top, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new BackEase
                {
                    Amplitude = 0.5,
                    EasingMode = EasingMode.EaseOut
                }
            };

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));

            toast.BeginAnimation(Canvas.TopProperty, slideDown);
            toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };

            timer.Tick += (s, e) =>
            {
                timer.Stop();

                var slideUp = new DoubleAnimation(top, startY, TimeSpan.FromMilliseconds(300));
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));

                slideUp.Completed += (s2, e2) => onClose?.Invoke();

                toast.BeginAnimation(Canvas.TopProperty, slideUp);
                toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };

            timer.Start();
        }

        // --- ЧАТ И ПРОЧЕЕ ---
        private void PerformSendMessage()
        {
            if (ChatMessageInput == null || string.IsNullOrWhiteSpace(ChatMessageInput.Text)) return;
            if (Messages != null)
            {
                Messages.Add(new ChatMessage { Nickname = NicknameBox?.Text ?? "Player", Message = ChatMessageInput.Text });
            }
            ChatMessageInput.Clear();
            ChatScroll?.ScrollToEnd();
        }

        public void SendMessage_Click(object sender, RoutedEventArgs e) { PerformSendMessage(); }

        public void ChatMessageInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) PerformSendMessage();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!System.IO.Directory.Exists(AppDataPath)) System.IO.Directory.CreateDirectory(AppDataPath);
                System.Diagnostics.Process.Start("explorer.exe", AppDataPath);
                ShowNotification("Папка .minecraft успешно открыта");
            }
            catch { ShowNotification("Не удалось открыть папку"); }
        }
        private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Формируем путь к папке logs внутри твоей AppDataPath
                string logDir = System.IO.Path.Combine(AppDataPath, "logs");

                // Если папки еще нет — создаем её
                if (!System.IO.Directory.Exists(logDir))
                    System.IO.Directory.CreateDirectory(logDir);

                // Открываем проводник
                System.Diagnostics.Process.Start("explorer.exe", logDir);
                ShowNotification("Папка с логами открыта");
            }
            catch (Exception ex)
            {
                ShowNotification("Не удалось найти папку");
                SafeLog($"[System] Ошибка открытия папки логов: {ex.Message}", Brushes.Red);
            }
        }
        private void ConsoleCheck_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            UpdateConsoleIndicator();
        }
        private void UpdateConsoleIndicator()
        {
            if (ConsoleIndicator != null && ConsoleCheck != null)
            {
                ConsoleIndicator.Visibility =
                    ConsoleCheck.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }
        
        private string GetCrashHelp(string reason)
        {
            if (reason.Contains("RAM"))
                return "Попробуй увеличить выделенную память в настройках.";

            if (reason.Contains("Java"))
                return "Переустанови Java через настройки лаунчера.";

            if (reason.Contains("видеодрайвера"))
                return "Обнови драйвер видеокарты.";

            if (reason.Contains("Повреждены файлы"))
                return "Перезапусти лаунчер для повторной загрузки файлов.";

            return "Посмотри crash-лог для подробностей.";
        }

        private void ReportProgress(
    double percent,
    string stage,
    string details,
    bool indeterminate = false)
        {

        }
      
        public class DownloadProgressInfo
        {
            public long BytesReceived { get; set; }
            public long TotalBytes { get; set; }
            public double Percent =>
                TotalBytes > 0 ? BytesReceived * 100.0 / TotalBytes : 0;
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            if (LogText != null)
            {
                LogText.Text = "Логи очищены...";
                ShowNotification("Консоль очищена");
            }
        }
    
    }
}

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
using System.Windows.Shapes;
using System.Windows.Threading;
using CmlLib.Core.VersionMetadata;
using SubReel.Core.Models;
using SubReel.Infrastructure.System;
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
            if (PlayBtn.Background is not SolidColorBrush brush)
            {
                brush = new SolidColorBrush(targetColor);
                PlayBtn.Background = brush;
                return;
            }

            var anim = new ColorAnimation
            {
                To = targetColor,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase()
            };

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
        private void SwitchTab(FrameworkElement panel)
        {
            if (panel == BuildsPanel) _currentTab = "home";
            else if (panel == SettingsPanel) _currentTab = "settings";
            else _currentTab = "other";

            if (BuildsPanel != null) BuildsPanel.Visibility = Visibility.Collapsed;
            if (SettingsPanel != null) SettingsPanel.Visibility = Visibility.Collapsed;
            if (NewsPanel != null) NewsPanel.Visibility = Visibility.Collapsed;

            panel.Visibility = Visibility.Visible;

            SetActiveMenuButton(); // ⭐ ВАЖНО

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
            ShowPanel(NewsPanel);
            SetActiveButton(BtnNews);
            LoadNews();
        }

        private void BackToBuilds_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(BuildsPanel);
            SetActiveButton(BtnBuilds);
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
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
                BtnVanilla.Tag = null;
                BtnCreateCustom.Tag = null;
                btn.Tag = "Selected";

                if (btn == BtnVanilla)
                {
                    string version = btn.Content?.ToString() ?? "1.21.1";
                    _selectedVersion = version;
                    if (SelectedVersionBottom != null) SelectedVersionBottom.Text = $"Vanilla {version}";
                }
                else if (btn == BtnCreateCustom)
                {
                    if (SelectedVersionBottom != null) SelectedVersionBottom.Text = "Новая сборка";
                }

                // Вызываем визуализатор. Благодаря защите внутри него, 
                // левое меню НЕ сбросит выделение со "Сборок".
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




        private void BtnVanilla_RightClick(object sender, MouseButtonEventArgs e)
        {
            VersionOverlay.Visibility = Visibility.Visible;
            e.Handled = true;
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


        private void CloseVersionOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            VersionOverlay.Visibility = Visibility.Collapsed;
        }

        private void ApplyVersion_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, что в списке действительно что-то выбрано
            if (VersionListBox?.SelectedItem is ListBoxItem selected && selected.Content != null)
            {
                string newVersion = selected.Content.ToString() ?? "1.21.1";

                // Обновляем переменную выбранной версии
                _selectedVersion = newVersion;

                // 1. Сохраняем в конфиг сразу
                SaveSettings();

                // 2. Обновляем текст на главной кнопке и её состояние (Безопасно)
                if (BtnVanilla != null)
                {
                    BtnVanilla.Content = newVersion;
                    // Переносим установку Tag сюда, чтобы избежать вылета, если кнопка null
                    BtnVanilla.Tag = "Selected";
                }

                // 3. Обновляем текст в информационной панели снизу
                if (SelectedVersionBottom != null)
                {
                    SelectedVersionBottom.Text = $"Vanilla {newVersion}";
                }

                // 4. Закрываем оверлей выбора версий
                if (VersionOverlay != null)
                {
                    VersionOverlay.Visibility = Visibility.Collapsed;
                }

                // Возвращаем фокус на окно (помогает, если нужно сразу управлять с клавиатуры)
                this.Focus();

                // Показываем уведомление пользователю
                ShowNotification($"ВЕРСИЯ {newVersion} ВЫБРАНА");
            }
        }
        private void VersionEditBtn_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // Чтобы клик не ушел на саму карточку
            VersionOverlay.Visibility = Visibility.Visible;
        }


        private void CreateCustomVersion_Click(object sender, RoutedEventArgs e)
        {
            ShowNotification("Этот раздел находится в разработке!");
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
        private async void ShowSnapshotsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            await LoadMinecraftVersionsAsync();
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
        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadCts == null)
                return;

            SafeLog("[LAUNCH] Пользователь отменил установку", Brushes.Orange);

            _downloadCts.Cancel();
            SetState(LauncherState.Canceled);
        }
        public class DownloadProgressInfo
        {
            public long BytesReceived { get; set; }
            public long TotalBytes { get; set; }
            public double Percent =>
                TotalBytes > 0 ? BytesReceived * 100.0 / TotalBytes : 0;
        }
        private void SetVersionSelectionEnabled(bool enabled)
        {
            if (VersionListBox != null)
                VersionListBox.IsEnabled = enabled;

            if (BtnVanilla != null)
                BtnVanilla.IsEnabled = enabled;

            if (ShowSnapshotsCheckBox != null)
                ShowSnapshotsCheckBox.IsEnabled = enabled;
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

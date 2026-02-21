using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SubReel
{
    public class NewsItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("accent_color")]
        public string AccentColor { get; set; } = "#3374FF";

        [JsonPropertyName("changes")]
        public List<string> Changes { get; set; } = new();

        [JsonPropertyName("button_url")]
        public string ButtonUrl { get; set; } = "";

        [JsonPropertyName("button_text")]
        public string ButtonText { get; set; } = "";
        public Visibility ButtonVisibility =>string.IsNullOrWhiteSpace(ButtonUrl)? Visibility.Collapsed: Visibility.Visible;
        public SolidColorBrush AccentBrush
        {
            get
            {
                try
                {
                    var converter = new BrushConverter();
                    var brushObj = converter.ConvertFrom(AccentColor ?? "#3374FF");

                    if (brushObj is SolidColorBrush brush)
                        return brush;

                    return Brushes.CornflowerBlue;
                }
                catch
                {
                    return Brushes.CornflowerBlue;
                }
            }
        }
    }

    public partial class MainWindow
    {
        private bool _isNotificationShowing = false;
        private DispatcherTimer? _onlineTimer;
        private Random _rnd = new Random();

        private async void LoadNews()
        {
            try
            {
                string url =
                    "https://gist.githubusercontent.com/Lemansen/5a1422d11e30226671c55640be3f5083/raw/news.json"
                    + "?t=" + DateTime.Now.Ticks;

                using var response = await _httpClient.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead);

                response.EnsureSuccessStatusCode();

                string jsonString = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(jsonString))
                    throw new Exception("Сервер прислал пустой файл");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                };

                var news = JsonSerializer.Deserialize<List<NewsItem>>(jsonString, options)
           ?? new List<NewsItem>();

                if (news == null || news.Count == 0)
                    throw new Exception("Новости отсутствуют");

                // ===== Сохраняем кэш =====
                if (!Directory.Exists(AppDataPath))
                    Directory.CreateDirectory(AppDataPath);

                File.WriteAllText(NewsCachePath, jsonString);

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

                // ===== Пытаемся загрузить кэш =====
                try
                {
                    if (File.Exists(NewsCachePath))
                    {
                        string cachedJson = File.ReadAllText(NewsCachePath);

                        var cachedNews = JsonSerializer.Deserialize<List<NewsItem>>(cachedJson)
                 ?? new List<NewsItem>();

                        if (cachedNews != null && cachedNews.Count > 0)
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
                catch
                {
                    // Если даже кэш повреждён — игнорируем
                }

                // ===== Если ничего не помогло — показываем ошибку =====
                Dispatcher.Invoke(() =>
                {
                    NewsItemsControl.ItemsSource = new List<NewsItem>
            {
                new NewsItem
                {
                    Title = "Ошибка сервера",
                    Description = "Не удалось получить новости и кэш отсутствует.",
                    Category = "SYSTEM ERROR",
                    AccentColor = "#FF4444"
                }
            };
                });
            }
        }
        private string NewsCachePath => System.IO.Path.Combine(AppDataPath, "news_cache.json");


        // --- НАВИГАЦИЯ И ПАНЕЛИ ---
        private void SwitchTab(FrameworkElement panel)
        {
            // Принудительно скрываем ВСЕ основные панели
            if (BuildsPanel != null) BuildsPanel.Visibility = Visibility.Collapsed;
            if (SettingsPanel != null) SettingsPanel.Visibility = Visibility.Collapsed;
            if (NewsPanel != null) NewsPanel.Visibility = Visibility.Collapsed;
            // Если есть панель сообщества, добавь и её:
            // if (CommunityPanel != null) CommunityPanel.Visibility = Visibility.Collapsed;

            panel.Visibility = Visibility.Visible; // Показываем только нужную

            if (this.Resources["FadeIn"] is Storyboard sb)
            {
                sb.Begin(panel);
            }
        }



        private void ShowPanel(FrameworkElement panelToShow)
        {
            SwitchTab(panelToShow);
        }

        private void SetActiveButton(Button activeButton)
        {
            Button[] menuButtons = { BtnBuilds, BtnCommunity, BtnSettings, BtnNews };

            foreach (var btn in menuButtons)
            {
                if (btn == null) continue;

                // Если это нажатая кнопка — ставим ей метку "Active"
                // Если нет — стираем метку
                btn.Tag = (btn == activeButton) ? "Active" : null;
            }
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
            // 1. Скрываем все лишнее через общую логику
            SwitchTab(NewsPanel);
            // 2. Активируем кнопку (убедись, что BtnNews добавлен в массив в SetActiveButton)
            SetActiveButton(BtnNews);
            LoadNews(); // Загружаем свежие данные
            // 3. Запускаем красивый выезд снизу
            if (NewsPanel != null)
            {
                ShowPanelWithAnimation(NewsPanel);
            }

            // Обновляем статус в Discord (если есть метод)
            UpdateRPC("Читает новости", "В лаунчере");
        }

        private void BackToBuilds_Click(object sender, RoutedEventArgs e)
        {
            // Скрываем новости явно
            if (NewsPanel != null) NewsPanel.Visibility = Visibility.Collapsed;

            ShowPanel(BuildsPanel);
            SetActiveButton(BtnBuilds);
            UpdateRPC("Выбирает сборку", "В главном меню");
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(SettingsPanel);
            SetActiveButton(BtnSettings);
            UpdateRPC("В настройках", "Меняет конфиги");
        }

        private void CommunityBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowNotification("Этот раздел находится в разработке!");
            // Будущая логика:
            // ShowPanel(CommunityPanel);
            // SetActiveButton(BtnCommunity);
            // UpdateRPC("В сообществе", "Общается");
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

            IsLicensed = false;
            // ИСПРАВЛЕНО: Теперь используется актуальный метод библиотеки
            CurrentSession = CmlLib.Core.Auth.MSession.CreateOfflineSession(inputNick);

            if (DisplayNick != null) DisplayNick.Text = inputNick;

            if (AccountTypeStatus != null)
            {
                AccountTypeStatus.Text = "OFFLINE";
                AccountTypeStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0));
            }

            if (AccountTypeBadge != null)
                AccountTypeBadge.Background = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255));

            try
            {
                if (UserAvatarImg != null)
                    UserAvatarImg.ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri($"https://minotar.net/helm/{inputNick}/45.png"));
            }
            catch { }

            SaveSettings();
            ShowNotification($"ВЫ ВОШЛИ КАК {inputNick.ToUpper()}");
            CloseAuthWithAnimation();
        }


        private async void MSLogin_Click(object sender, RoutedEventArgs e)
        {
            ShowNotification("Запуск авторизации Microsoft...");
            await MicrosoftLogin();
        }

        // --- ВЫБОР ВЕРСИЙ ---
        private void SelectVersion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                var defaultBorder = (SolidColorBrush)FindResource("BorderSharp");
                BtnVanilla.BorderBrush = defaultBorder;
                BtnCreateCustom.BorderBrush = defaultBorder;
                BtnVanilla.BorderThickness = new Thickness(2);
                BtnCreateCustom.BorderThickness = new Thickness(2);

                btn.BorderBrush = (SolidColorBrush)FindResource("AccentBlue");
                btn.BorderThickness = new Thickness(2);

                string rawTag = btn.Tag.ToString() ?? "";
                string[] parts = rawTag.Split('_');
                string buildName = parts[0];
                _selectedVersion = parts.Length > 1 ? parts[1] : rawTag;

                if (SelectedVersionBottom != null) SelectedVersionBottom.Text = $"{buildName} {_selectedVersion}";
                UpdateRPC("Выбор версии", $"Выбрано: {buildName}");
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
            if (!string.IsNullOrEmpty(LogText.Text))
            {
                Clipboard.SetText(LogText.Text);
                ShowNotification("Логи скопированы в буфер обмена");
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
            if (VersionListBox?.SelectedItem is ListBoxItem selected &&
     selected.Content is string newVersion)
            {
                _selectedVersion = newVersion;
                SaveSettings();

                if (BtnVanilla != null)
                {
                    BtnVanilla.Content = newVersion;
                    BtnVanilla.Tag = $"Vanilla_{newVersion}";
                }
                if (SelectedVersionBottom != null)
                    SelectedVersionBottom.Text = $"Vanilla {newVersion}";
                if (VersionOverlay != null)
                    VersionOverlay.Visibility = Visibility.Collapsed;

                ShowNotification($"ВЕРСИЯ {newVersion} ВЫБРАНА");
            }
        }

        private void CreateCustomVersion_Click(object sender, RoutedEventArgs e)
        {
            ShowNotification("Этот раздел находится в разработке!");
        }

        // Исправленная анимация (принимает FrameworkElement для доступа к свойствам анимации)
        private void ShowPanelWithAnimation(FrameworkElement? panel)
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

        private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RamInput == null || GbText == null) return;

            int val = (int)e.NewValue;
            RamInput.Text = val.ToString();

            // Вычисляем гигабайты
            double gb = Math.Round(val / 1024.0, 1);
            GbText.Text = $"{gb:F1} GB";
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
            if (_isNotificationShowing || NotificationToast == null) return;

            NotificationText.Text = message;
            _isNotificationShowing = true;

            double toastWidth = NotificationToast.ActualWidth > 0 ? NotificationToast.ActualWidth : 300;
            double centerX = (this.ActualWidth - toastWidth) / 2;
            Canvas.SetLeft(NotificationToast, centerX);

            DoubleAnimation slideDown = new DoubleAnimation { From = -100, To = 30, Duration = TimeSpan.FromSeconds(0.6), EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
            DoubleAnimation fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromSeconds(0.4) };

            NotificationToast.BeginAnimation(Canvas.TopProperty, slideDown);
            NotificationToast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            timer.Tick += (s, e) => {
                timer.Stop();
                DoubleAnimation slideUp = new DoubleAnimation { To = -100, Duration = TimeSpan.FromSeconds(0.5), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                DoubleAnimation fadeOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(0.3) };
                slideUp.Completed += (s2, e2) => _isNotificationShowing = false;

                NotificationToast.BeginAnimation(Canvas.TopProperty, slideUp);
                NotificationToast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
            timer.Start();
        }

        private void UpdateProgress(double newValue)
        {
            DoubleAnimation animation = new DoubleAnimation { To = newValue, Duration = TimeSpan.FromSeconds(0.5), EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            LaunchProgress.BeginAnimation(ProgressBar.ValueProperty, animation);
        }

        private void SetProgressSmooth(double value)
        {
            if (LaunchProgress == null) return;
            DoubleAnimation animation = new DoubleAnimation { To = value, Duration = TimeSpan.FromSeconds(0.6), EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            LaunchProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, animation);
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

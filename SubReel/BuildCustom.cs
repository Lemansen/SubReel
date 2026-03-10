using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SubReel
{
    using System.ComponentModel;
    using System.Linq;

    public class BuildModel : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Loader { get; set; }
        public string Icon { get; set; } = "🧩";

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set { _isFavorite = value; OnPropertyChanged(nameof(IsFavorite)); }
        }
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }


        public BuildModel(string name, string version, string loader, bool isFavorite = false)
        {
            Name = name;
            Version = version;
            Loader = loader;
            IsFavorite = isFavorite;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static class BuildManager
    {
        // Список всех сборок
        public static List<BuildModel> Builds = new();
        public static IEnumerable<BuildModel> GetAllBuilds()
        {
            return Builds;
        }
        // Метод добавления, который просил компилятор
        public static void AddBuild(BuildModel build)
        {
            Builds.Add(build);
        }

        // Метод для фильтрации
        public static IEnumerable<BuildModel> GetFiltered(string query, bool favoritesOnly)
        {
            query = query?.ToLower() ?? "";
            return Builds.Where(b =>
                (string.IsNullOrEmpty(query) || b.Name.ToLower().Contains(query)) &&
                (!favoritesOnly || b.IsFavorite) // Если favoritesOnly = false, условие всегда истинно
            );
        }
    }

    public static class BuildCardFactory
    {
        public static Button Create(BuildModel build, RoutedEventHandler openHandler, RoutedEventHandler favoriteToggleHandler)
        {
            Button card = new Button();
            card.Height = 160;
            card.Margin = new Thickness(10);
            card.Tag = build;
            card.Click += openHandler;

            // Используем стиль из ресурсов (если он есть) или создаем шаблон
            // Для краткости создадим содержимое программно:
            Grid mainGrid = new Grid();

            Border border = new Border
            {
                CornerRadius = new CornerRadius(22),
                Background = (Brush)Application.Current.FindResource("CardBg"),
                BorderBrush = (Brush)Application.Current.FindResource("BorderSharp"),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(20)
            };

            StackPanel stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = build.Icon, FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = build.Name, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
            stack.Children.Add(new TextBlock { Text = $"{build.Loader} {build.Version}", Foreground = Brushes.Gray, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });

            border.Child = stack;
            mainGrid.Children.Add(border);

            // КНОПКА ИЗБРАННОГО (Звездочка)
            Button favBtn = new Button
            {
                Content = build.IsFavorite ? "★" : "☆",
                Foreground = build.IsFavorite ? new SolidColorBrush(Color.FromRgb(255, 204, 0)) : Brushes.Gray,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = build // Привязываем модель к кнопке
            };
            favBtn.Click += favoriteToggleHandler; // Отдельный обработчик для звезды

            mainGrid.Children.Add(favBtn);
            card.Content = mainGrid;

            return card;
        }
    }

}

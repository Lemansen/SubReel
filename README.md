# SubReel

Primary frontend: `SubReel.Avalonia`

Legacy frontend: `SubReel` (WPF, kept as fallback while the migration is finishing)

Current direction:

- `SubReel.Avalonia` is the main cross-platform launcher shell.
- `SubReel.Launcher.Core` contains shared runtime logic.
- `SubReel` remains only for legacy WPF compatibility and migration support.

Черновая основа лаунчера Minecraft на C# и .NET с упором на MVVM.

Сейчас в репозитории уже есть UI и логика запуска, а в этом обновлении добавлен понятный MVVM-слой, от которого удобно строить дальше:

- `MainViewModel` управляет навигацией между `Builds`, `Community`, `News`, `Settings` и `CreateBuild`.
- `BuildsViewModel` хранит поиск сборок, карточку сервера, карточку создания сборки, список сборок, избранное, статус запуска и нижний progress bar.
- `NewsViewModel` разделяет новости лаунчера и сервера.
- `SettingsViewModel` покрывает автопоиск Java, консоль при запуске, язык, режим окна, память, папки игры и встроенный терминал.
- `CommunityViewModel` оставлен как placeholder "в планах".

Рекомендуемая архитектура дальше:

1. `Presentation`
   `View` и `ViewModel`, только UI-состояние и команды.
2. `Application`
   сценарии: запуск игры, создание сборки, авторизация, обновление лаунчера.
3. `Domain`
   сущности: сборка, профиль игрока, новость, настройки, статус запуска.
4. `Infrastructure`
   `CmlLib`, файловая система, Java detector, Microsoft auth, updater, логирование.

Если хотите перейти именно на Avalonia, лучше следующим шагом не переносить giant code-behind как есть, а собрать новое окно на этих viewmodel-ах и постепенно вынести логику из `MainWindow.xaml.cs` в сервисы.

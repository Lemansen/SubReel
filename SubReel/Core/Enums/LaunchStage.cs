using System;
using System.Collections.Generic;
using System.Text;

namespace SubReel.Core.Enums
{
    public enum LaunchStage
    {
        Idle,
        PreparingJava,
        CheckingJava,
        DownloadingGame,
        PreparingProcess,
        Launching,
        Running,
        Error
    }
    public enum InstallStage
    {
        Preparing,        // подготовка лаунчера
        CheckingFiles,    // проверка наличия файлов
        Verifying,        // проверка целостности
        Downloading,      // загрузка файлов
        Finalizing,       // финализация установки
        Initializing,     // подготовка к запуску
        Completed,        // установка завершена
        Canceled,         // отменено
        Error             // ошибка
    }
}

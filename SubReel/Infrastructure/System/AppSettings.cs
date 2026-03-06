using System;
using static SubReel.MainWindow;
#nullable enable

namespace SubReel.Infrastructure.System
{
    public class AppSettings
    {
        public string Nickname { get; set; } = "Player";
        public double Ram { get; set; } = 4096;
        public bool IsLicensed { get; set; } = false;
        public string SelectedVersion { get; set; } = "1.21.1";
        public bool IsConsoleShow { get; set; } = false;
        public string? ManualJavaPath { get; set; } // ⭐ ВОТ ЭТО ДОБАВИТЬ
        public JavaSourceType JavaSource { get; set; } = JavaSourceType.Bundled;
    }
}

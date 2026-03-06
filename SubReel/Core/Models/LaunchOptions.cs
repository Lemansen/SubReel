using CmlLib.Core.Auth;

namespace SubReel.Core.Models
{
#nullable enable
    public class LaunchOptions
    {
        public bool OfflineMode { get; set; }

        public string Nickname { get; set; } = "Player";

        // RAM в MB
        public int RamMb { get; set; } = 4096;

        public bool ShowConsole { get; set; }
        public bool IsLicensed { get; set; }

        public string Version { get; set; } = "1.21.1";

        public string? JavaPath { get; set; }
        public string? ManualJavaPath { get; set; }

        public MSession? Session { get; set; }

        // 🔥 ДОБАВИТЬ ДЛЯ LauncherService
        public string GamePath { get; set; } = "";

        public int MinRamMb => 1024;
        public int MaxRamMb => RamMb;

        public string Username => Nickname;
    }
}
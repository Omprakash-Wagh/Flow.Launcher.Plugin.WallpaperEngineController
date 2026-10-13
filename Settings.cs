namespace Flow.Launcher.Plugin.WallpaperEngineController
{
    public class Settings
    {
        public string? CustomExePath { get; set; }
        public int DefaultMonitor { get; set; } = 0; // 0 = all monitors, 1+ = specific monitor
    }
}

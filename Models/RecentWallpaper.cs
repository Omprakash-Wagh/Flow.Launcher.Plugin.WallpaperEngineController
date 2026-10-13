namespace Flow.Launcher.Plugin.WallpaperEngineController.Models
{
    public record RecentWallpaper(string Path, string Title, string? PreviewImage = null);

    public record RecentWallpaperContext(RecentWallpaper Wallpaper);
}

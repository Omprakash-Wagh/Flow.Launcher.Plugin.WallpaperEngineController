using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Flow.Launcher.Plugin.WallpaperEngineController.Views
{
    public partial class SettingsControl : UserControl
    {
        private readonly Settings _settings;
        private readonly WallpaperEngineService _service;

        public SettingsControl(Settings settings, WallpaperEngineService service)
        {
            InitializeComponent();
            _settings = settings;
            _service = service;

            TxtExePath.Text = _settings.CustomExePath ?? "";
            UpdateStatus();
        }

        private void OnTxtExePathChanged(object sender, TextChangedEventArgs e)
        {
            string path = TxtExePath.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                _service.ClearCustomPath();
            }
            else if (File.Exists(path))
            {
                _service.SetCustomPath(path);
            }
            else
            {
                _settings.CustomExePath = path;
            }
            UpdateStatus();
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Wallpaper Engine Executable",
                Filter = "Wallpaper Engine Executable (wallpaper64.exe;wallpaper32.exe)|wallpaper64.exe;wallpaper32.exe|All Executables (*.exe)|*.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtExePath.Text = dialog.FileName;
                _service.SetCustomPath(dialog.FileName);
                UpdateStatus();
            }
        }

        private void OnDetectClick(object sender, RoutedEventArgs e)
        {
            _service.Refresh();
            TxtExePath.Text = _settings.CustomExePath ?? "";
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            string? exe = _service.GetExecutablePath();
            if (exe != null && File.Exists(exe))
            {
                TxtStatus.Text = $"Status: Detected at {exe}";
            }
            else
            {
                TxtStatus.Text = "Status: Not detected. Start Wallpaper Engine or browse for wallpaper64.exe.";
            }
        }
    }
}

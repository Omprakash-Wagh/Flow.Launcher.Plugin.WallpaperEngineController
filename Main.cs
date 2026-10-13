using System;
using System.Collections.Generic;
using System.Windows.Controls;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.WallpaperEngineController.Models;
using Flow.Launcher.Plugin.WallpaperEngineController.ResultBuilders;
using Flow.Launcher.Plugin.WallpaperEngineController.Views;

namespace Flow.Launcher.Plugin.WallpaperEngineController
{
    public class Main : IPlugin, IContextMenu, ISettingProvider, IReloadable
    {
        private PluginInitContext _context = null!;
        private Settings _settings = null!;
        private WallpaperEngineService _service = null!;
        private ResultFactory _resultFactory = null!;

        public void Init(PluginInitContext context)
        {
            _context = context;
            _settings = _context.API.LoadSettingJsonStorage<Settings>();
            _service = new WallpaperEngineService(_settings, () => _context.API.SaveSettingJsonStorage<Settings>());
            _resultFactory = new ResultFactory(_service, _settings, _context);
        }

        public List<Result> Query(Query query)
        {
            // If Wallpaper Engine executable cannot be found anywhere, show setup prompt
            if (!_service.IsAvailable())
            {
                return _resultFactory.BuildNotDetectedResult();
            }

            string search = query.Search.Trim();
            if (string.IsNullOrEmpty(search))
            {
                return _resultFactory.BuildMainMenu();
            }

            var parts = search.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLowerInvariant();
            string subQuery = parts.Length > 1 ? parts[1].Trim() : "";

            return command switch
            {
                "play" => _resultFactory.BuildPlayResult(),
                "pause" => _resultFactory.BuildPauseResult(),
                "stop" => _resultFactory.BuildStopResult(),
                "mute" => _resultFactory.BuildMuteResult(),
                "unmute" => _resultFactory.BuildUnmuteResult(),
                "next" => _resultFactory.BuildNextResults(subQuery),
                "profile" or "profiles" => _resultFactory.BuildProfileResults(subQuery),
                "playlist" or "playlists" => _resultFactory.BuildPlaylistResults(subQuery),
                "recent" or "recents" => _resultFactory.BuildRecentResults(subQuery),
                "icon" or "icons" => _resultFactory.BuildIconResults(subQuery),
                "settings" or "setting" or "browse" => _resultFactory.BuildSettingsResult(),
                "quit" or "exit" => _resultFactory.BuildQuitResult(),
                _ => _resultFactory.BuildFilteredMainMenu(search)
            };
        }

        public List<Result> LoadContextMenus(Result selectedResult)
        {
            if (selectedResult.ContextData is RecentWallpaperContext ctx)
            {
                return _resultFactory.BuildRecentWallpaperContextMenu(ctx);
            }

            return new List<Result>();
        }

        public Control CreateSettingPanel()
        {
            return new SettingsControl(_settings, _service);
        }

        public void ReloadData()
        {
            // Trigger redetection or refresh
            _service.Refresh();
        }
    }
}

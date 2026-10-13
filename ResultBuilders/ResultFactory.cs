using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.WallpaperEngineController.Models;

namespace Flow.Launcher.Plugin.WallpaperEngineController.ResultBuilders
{
    public class ResultFactory
    {
        private readonly WallpaperEngineService _service;
        private readonly Settings _settings;
        private readonly PluginInitContext _context;

        public ResultFactory(WallpaperEngineService service, Settings settings, PluginInitContext context)
        {
            _service = service;
            _settings = settings;
            _context = context;
        }

        private static bool _desktopIconsHidden = false;

        private string GetIcon(string name)
        {
            string iconPath = Path.Combine("Images", $"{name}.png");
            try
            {
                string pluginDir = _context.CurrentPluginMetadata.PluginDirectory;
                string fullPath = Path.Combine(pluginDir, iconPath);
                if (File.Exists(fullPath)) return iconPath;

                string mainIcon = Path.Combine("Images", "icon.png");
                if (File.Exists(Path.Combine(pluginDir, mainIcon))) return mainIcon;
            }
            catch
            {
                // Ignore metadata lookup issues
            }

            return iconPath;
        }

        public List<Result> BuildNotDetectedResult()
        {
            return new List<Result>
            {
                new Result
                {
                    Title = "Wallpaper Engine is not running or not detected",
                    SubTitle = "Launch Wallpaper Engine once to auto-detect, or click below to browse manually",
                    IcoPath = GetIcon("icon"),
                    Score = 100,
                    Action = _ =>
                    {
                        BrowseForWallpaperEngine();
                        return true;
                    }
                },
                new Result
                {
                    Title = "Browse for wallpaper64.exe manually",
                    SubTitle = "Click to select your wallpaper64.exe executable",
                    IcoPath = GetIcon("settings"),
                    Score = 90,
                    Action = _ =>
                    {
                        BrowseForWallpaperEngine();
                        return true;
                    }
                }
            };
        }

        private void BrowseForWallpaperEngine()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Wallpaper Engine Executable",
                Filter = "Wallpaper Engine Executable (wallpaper64.exe;wallpaper32.exe)|wallpaper64.exe;wallpaper32.exe|All Executables (*.exe)|*.exe",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };

            if (dialog.ShowDialog() == true)
            {
                _service.SetCustomPath(dialog.FileName);
                _context.API.ShowMsg("Wallpaper Engine Configured", $"Linked to: {dialog.FileName}", GetIcon("icon"));
            }
        }

        public List<Result> BuildMainMenu()
        {
            var profiles = _service.GetProfiles();
            var playlists = _service.GetPlaylists();
            var recents = _service.GetRecentWallpapers();
            var activeMonitors = _service.GetActiveMonitors();

            string nextSubTitle = activeMonitors.Count <= 1
                ? "Skip to next wallpaper on playlist"
                : "Skip to next wallpaper (type 'wp next' for displays)";

            string nextExtraArgs = (activeMonitors.Count == 1 && activeMonitors[0] > 1)
                ? $"-monitor {activeMonitors[0] - 1}"
                : "";

            return new List<Result>
            {
                new Result
                {
                    Title = "Play Wallpapers",
                    SubTitle = "Resume all active wallpapers",
                    IcoPath = GetIcon("play"),
                    Action = _ => { _service.ExecuteCommand("play"); return true; },
                    Score = 100
                },
                new Result
                {
                    Title = "Pause Wallpapers",
                    SubTitle = "Freeze wallpaper animations",
                    IcoPath = GetIcon("pause"),
                    Action = _ => { _service.ExecuteCommand("pause"); return true; },
                    Score = 95
                },
                new Result
                {
                    Title = "Stop Wallpapers",
                    SubTitle = "Completely unload and stop wallpapers",
                    IcoPath = GetIcon("stop"),
                    Action = _ => { _service.ExecuteCommand("stop"); return true; },
                    Score = 90
                },
                new Result
                {
                    Title = "Mute Audio",
                    SubTitle = "Mute wallpaper sound output",
                    IcoPath = GetIcon("mute"),
                    Action = _ => { _service.ExecuteCommand("mute"); return true; },
                    Score = 85
                },
                new Result
                {
                    Title = "Unmute Audio",
                    SubTitle = "Restore wallpaper sound output",
                    IcoPath = GetIcon("unmute"),
                    Action = _ => { _service.ExecuteCommand("unmute"); return true; },
                    Score = 80
                },
                new Result
                {
                    Title = "Next Wallpaper",
                    SubTitle = nextSubTitle,
                    IcoPath = GetIcon("next"),
                    Action = _ => { _service.ExecuteCommand("nextWallpaper", nextExtraArgs); return true; },
                    Score = 75
                },
                new Result
                {
                    Title = "Load Profile...",
                    SubTitle = profiles.Count > 0 
                        ? $"Select from {profiles.Count} saved profile(s) (type 'wp profile')" 
                        : "Browse and load profiles (type 'wp profile')",
                    IcoPath = GetIcon("profile"),
                    Action = _ => { _context.API.ChangeQuery("wp profile "); return false; },
                    Score = 70
                },
                new Result
                {
                    Title = "Load Playlist...",
                    SubTitle = playlists.Count > 0 
                        ? $"Select from {playlists.Count} saved playlist(s) (type 'wp playlist')" 
                        : "Browse and load playlists (type 'wp playlist')",
                    IcoPath = GetIcon("playlist"),
                    Action = _ => { _context.API.ChangeQuery("wp playlist "); return false; },
                    Score = 65
                },
                new Result
                {
                    Title = "Recent Wallpapers...",
                    SubTitle = recents.Count > 0 
                        ? $"Choose from {recents.Count} recent wallpaper(s) (type 'wp recent')" 
                        : "Browse recently used wallpapers (type 'wp recent')",
                    IcoPath = GetIcon("recent"),
                    Action = _ => { _context.API.ChangeQuery("wp recent "); return false; },
                    Score = 60
                },
                new Result
                {
                    Title = "Toggle Desktop Icons",
                    SubTitle = "Show or hide Windows desktop icons",
                    IcoPath = GetIcon("icons"),
                    Action = _ => 
                    { 
                        _desktopIconsHidden = !_desktopIconsHidden;
                        _service.ExecuteCommand(_desktopIconsHidden ? "hideIcons" : "showIcons"); 
                        return true; 
                    },
                    Score = 55
                },
                new Result
                {
                    Title = "Open Settings",
                    SubTitle = "Open Wallpaper Engine (click gear icon in top right for settings)",
                    IcoPath = GetIcon("settings"),
                    Action = _ => 
                    { 
                        _service.OpenWindow("settings"); 
                        _context.API.ShowMsg("Wallpaper Engine", "Opened Wallpaper Engine — click the gear icon in the top right to access Settings.", GetIcon("settings"));
                        return true; 
                    },
                    Score = 50
                },
                new Result
                {
                    Title = "Quit Wallpaper Engine",
                    SubTitle = "Close Wallpaper Engine completely",
                    IcoPath = GetIcon("quit"),
                    Action = _ => { _service.Quit(); return true; },
                    Score = 40
                }
            };
        }

        public List<Result> BuildFilteredMainMenu(string query)
        {
            var menu = BuildMainMenu();
            if (string.IsNullOrWhiteSpace(query)) return menu;

            return menu
                .Select(item =>
                {
                    var match = _context.API.FuzzySearch(query, item.Title);
                    return new { item, match };
                })
                .Where(x => x.match.IsSearchPrecisionScoreMet())
                .OrderByDescending(x => x.match.Score)
                .Select(x =>
                {
                    x.item.Score = x.match.Score;
                    return x.item;
                })
                .ToList();
        }

        public List<Result> BuildPlayResult() => new()
        {
            new Result
            {
                Title = "Play Wallpapers",
                SubTitle = "Resume all wallpapers",
                IcoPath = GetIcon("play"),
                Action = _ => { _service.ExecuteCommand("play"); return true; }
            }
        };

        public List<Result> BuildPauseResult() => new()
        {
            new Result
            {
                Title = "Pause Wallpapers",
                SubTitle = "Pause wallpaper animation",
                IcoPath = GetIcon("pause"),
                Action = _ => { _service.ExecuteCommand("pause"); return true; }
            }
        };

        public List<Result> BuildStopResult() => new()
        {
            new Result
            {
                Title = "Stop Wallpapers",
                SubTitle = "Unload and clear wallpapers",
                IcoPath = GetIcon("stop"),
                Action = _ => { _service.ExecuteCommand("stop"); return true; }
            }
        };

        public List<Result> BuildMuteResult() => new()
        {
            new Result
            {
                Title = "Mute Audio",
                SubTitle = "Mute audio on all wallpapers",
                IcoPath = GetIcon("mute"),
                Action = _ => { _service.ExecuteCommand("mute"); return true; }
            }
        };

        public List<Result> BuildUnmuteResult() => new()
        {
            new Result
            {
                Title = "Unmute Audio",
                SubTitle = "Unmute wallpaper audio",
                IcoPath = GetIcon("unmute"),
                Action = _ => { _service.ExecuteCommand("unmute"); return true; }
            }
        };

        public List<Result> BuildNextResults(string monitorArg)
        {
            var activeMonitors = _service.GetActiveMonitors();
            var results = new List<Result>();

            if (int.TryParse(monitorArg.Trim(), out int monitorIndex))
            {
                if (activeMonitors.Contains(monitorIndex))
                {
                    int zeroBasedIndex = monitorIndex - 1;
                    results.Add(new Result
                    {
                        Title = $"Next Wallpaper on Monitor {monitorIndex}",
                        SubTitle = $"Skip wallpaper on display {monitorIndex}",
                        IcoPath = GetIcon("next"),
                        Action = _ =>
                        {
                            _service.ExecuteCommand("nextWallpaper", $"-monitor {zeroBasedIndex}");
                            return true;
                        }
                    });
                }
                else
                {
                    string activeDisplayList = activeMonitors.Count == 1
                        ? $"Monitor {activeMonitors[0]}"
                        : string.Join(", ", activeMonitors.Select(m => $"Monitor {m}"));

                    results.Add(new Result
                    {
                        Title = $"Monitor {monitorIndex} is not active",
                        SubTitle = $"Wallpaper Engine is active on: {activeDisplayList}",
                        IcoPath = GetIcon("next"),
                        Action = _ => false
                    });
                }
                return results;
            }

            // Case A: Single active monitor
            if (activeMonitors.Count <= 1)
            {
                string extraArgs = (activeMonitors.Count == 1 && activeMonitors[0] > 1)
                    ? $"-monitor {activeMonitors[0] - 1}"
                    : "";

                results.Add(new Result
                {
                    Title = "Next Wallpaper",
                    SubTitle = "Skip to next wallpaper on playlist",
                    IcoPath = GetIcon("next"),
                    Score = 100,
                    Action = _ =>
                    {
                        _service.ExecuteCommand("nextWallpaper", extraArgs);
                        return true;
                    }
                });
                return results;
            }

            // Case B: Multiple active monitors
            results.Add(new Result
            {
                Title = "Next Wallpaper (All Monitors)",
                SubTitle = "Cycle to next wallpaper across all playlists",
                IcoPath = GetIcon("next"),
                Score = 100,
                Action = _ =>
                {
                    _service.ExecuteCommand("nextWallpaper");
                    return true;
                }
            });

            foreach (int monitor in activeMonitors)
            {
                int zeroBasedIndex = monitor - 1;
                results.Add(new Result
                {
                    Title = $"Next Wallpaper on Monitor {monitor}",
                    SubTitle = $"Cycle to next wallpaper on Monitor {monitor} only",
                    IcoPath = GetIcon("next"),
                    Score = 90 - zeroBasedIndex,
                    Action = _ =>
                    {
                        _service.ExecuteCommand("nextWallpaper", $"-monitor {zeroBasedIndex}");
                        return true;
                    }
                });
            }

            return results;
        }

        public List<Result> BuildProfileResults(string filter)
        {
            var profiles = _service.GetProfiles();
            if (profiles.Count == 0)
            {
                return new List<Result>
                {
                    new Result
                    {
                        Title = "No Profiles Found",
                        SubTitle = "Create profiles inside Wallpaper Engine to switch them here",
                        IcoPath = GetIcon("profile")
                    }
                };
            }

            var results = profiles.Select(p => new Result
            {
                Title = p.Name,
                SubTitle = "Press Enter to load this profile",
                IcoPath = GetIcon("profile"),
                Action = _ =>
                {
                    _service.ExecuteCommand("openProfile", $"-profile \"{p.Name}\"");
                    return true;
                }
            }).ToList();

            if (string.IsNullOrWhiteSpace(filter)) return results;

            return results
                .Select(item =>
                {
                    var match = _context.API.FuzzySearch(filter, item.Title);
                    return new { item, match };
                })
                .Where(x => x.match.IsSearchPrecisionScoreMet())
                .OrderByDescending(x => x.match.Score)
                .Select(x =>
                {
                    x.item.Score = x.match.Score;
                    return x.item;
                })
                .ToList();
        }

        public List<Result> BuildPlaylistResults(string filter)
        {
            var playlists = _service.GetPlaylists();
            if (playlists.Count == 0)
            {
                return new List<Result>
                {
                    new Result
                    {
                        Title = "No Playlists Found",
                        SubTitle = "Create playlists inside Wallpaper Engine to load them here",
                        IcoPath = GetIcon("playlist")
                    }
                };
            }

            var results = playlists.Select(p => new Result
            {
                Title = p.Name,
                SubTitle = "Press Enter to load this playlist",
                IcoPath = GetIcon("playlist"),
                Action = _ =>
                {
                    _service.ExecuteCommand("openPlaylist", $"-playlist \"{p.Name}\"");
                    return true;
                }
            }).ToList();

            if (string.IsNullOrWhiteSpace(filter)) return results;

            return results
                .Select(item =>
                {
                    var match = _context.API.FuzzySearch(filter, item.Title);
                    return new { item, match };
                })
                .Where(x => x.match.IsSearchPrecisionScoreMet())
                .OrderByDescending(x => x.match.Score)
                .Select(x =>
                {
                    x.item.Score = x.match.Score;
                    return x.item;
                })
                .ToList();
        }

        public List<Result> BuildRecentResults(string filter)
        {
            var recents = _service.GetRecentWallpapers();
            if (recents.Count == 0)
            {
                return new List<Result>
                {
                    new Result
                    {
                        Title = "No Recent Wallpapers Found",
                        SubTitle = "Wallpapers you open in Wallpaper Engine will appear here",
                        IcoPath = GetIcon("recent")
                    }
                };
            }

            var results = recents.Select(w => new Result
            {
                Title = w.Title,
                SubTitle = w.Path,
                IcoPath = !string.IsNullOrEmpty(w.PreviewImage) && File.Exists(w.PreviewImage) 
                    ? w.PreviewImage 
                    : GetIcon("recent"),
                ContextData = new RecentWallpaperContext(w),
                Action = _ =>
                {
                    string safePath = w.Path.Replace('/', '\\');
                    _service.ExecuteCommand("openWallpaper", $"-file \"{safePath}\"");
                    return true;
                }
            }).ToList();

            if (string.IsNullOrWhiteSpace(filter)) return results;

            return results
                .Select(item =>
                {
                    var match = _context.API.FuzzySearch(filter, item.Title);
                    return new { item, match };
                })
                .Where(x => x.match.IsSearchPrecisionScoreMet())
                .OrderByDescending(x => x.match.Score)
                .Select(x =>
                {
                    x.item.Score = x.match.Score;
                    return x.item;
                })
                .ToList();
        }

        public List<Result> BuildRecentWallpaperContextMenu(RecentWallpaperContext ctx)
        {
            string safePath = ctx.Wallpaper.Path.Replace('/', '\\');
            var activeMonitors = _service.GetActiveMonitors();
            var results = new List<Result>();

            if (activeMonitors.Count <= 1)
            {
                string monitorArg = (activeMonitors.Count == 1 && activeMonitors[0] > 1)
                    ? $" -monitor {activeMonitors[0] - 1}"
                    : "";

                results.Add(new Result
                {
                    Title = "Apply Wallpaper",
                    SubTitle = $"Apply '{ctx.Wallpaper.Title}'",
                    IcoPath = GetIcon("recent"),
                    Action = _ =>
                    {
                        _service.ExecuteCommand("openWallpaper", $"-file \"{safePath}\"{monitorArg}");
                        return true;
                    }
                });
            }
            else
            {
                results.Add(new Result
                {
                    Title = "Apply to All Monitors",
                    SubTitle = $"Apply '{ctx.Wallpaper.Title}' across all displays",
                    IcoPath = GetIcon("recent"),
                    Action = _ =>
                    {
                        _service.ExecuteCommand("openWallpaper", $"-file \"{safePath}\"");
                        return true;
                    }
                });

                foreach (int monitor in activeMonitors)
                {
                    int zeroBasedIndex = monitor - 1;
                    results.Add(new Result
                    {
                        Title = $"Apply to Monitor {monitor}",
                        SubTitle = $"Set '{ctx.Wallpaper.Title}' on display {monitor} only",
                        IcoPath = GetIcon("recent"),
                        Action = _ =>
                        {
                            _service.ExecuteCommand("openWallpaper", $"-file \"{safePath}\" -monitor {zeroBasedIndex}");
                            return true;
                        }
                    });
                }
            }

            results.Add(new Result
            {
                Title = "Open Folder in File Explorer",
                SubTitle = ctx.Wallpaper.Path,
                IcoPath = GetIcon("browse"),
                Action = _ =>
                {
                    try
                    {
                        string normalPath = ctx.Wallpaper.Path.Replace('/', '\\');
                        if (File.Exists(normalPath))
                        {
                            Process.Start("explorer.exe", $"/select,\"{normalPath}\"");
                        }
                        else if (Directory.Exists(normalPath))
                        {
                            Process.Start("explorer.exe", $"\"{normalPath}\"");
                        }
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            });

            return results;
        }

        public List<Result> BuildIconResults(string subQuery)
        {
            return new List<Result>
            {
                new Result
                {
                    Title = "Toggle Desktop Icons",
                    SubTitle = "Toggle show/hide desktop icons",
                    IcoPath = GetIcon("icons"),
                    Action = _ =>
                    {
                        _desktopIconsHidden = !_desktopIconsHidden;
                        _service.ExecuteCommand(_desktopIconsHidden ? "hideIcons" : "showIcons");
                        return true;
                    }
                },
                new Result
                {
                    Title = "Hide Desktop Icons",
                    SubTitle = "Hide all desktop icons",
                    IcoPath = GetIcon("icons"),
                    Action = _ =>
                    {
                        _desktopIconsHidden = true;
                        _service.ExecuteCommand("hideIcons");
                        return true;
                    }
                },
                new Result
                {
                    Title = "Show Desktop Icons",
                    SubTitle = "Restore all desktop icons",
                    IcoPath = GetIcon("icons"),
                    Action = _ =>
                    {
                        _desktopIconsHidden = false;
                        _service.ExecuteCommand("showIcons");
                        return true;
                    }
                }
            };
        }

        public List<Result> BuildSettingsResult() => new()
        {
            new Result
            {
                Title = "Wallpaper Engine Settings",
                SubTitle = "Open Wallpaper Engine (click gear icon in top right for settings)",
                IcoPath = GetIcon("settings"),
                Action = _ => 
                { 
                    _service.OpenWindow("settings"); 
                    _context.API.ShowMsg("Wallpaper Engine", "Opened Wallpaper Engine — click the gear icon in the top right to access Settings.", GetIcon("settings"));
                    return true; 
                }
            }
        };

        public List<Result> BuildQuitResult() => new()
        {
            new Result
            {
                Title = "Quit Wallpaper Engine",
                SubTitle = "Exit Wallpaper Engine completely",
                IcoPath = GetIcon("quit"),
                Action = _ => { _service.Quit(); return true; }
            }
        };
    }
}

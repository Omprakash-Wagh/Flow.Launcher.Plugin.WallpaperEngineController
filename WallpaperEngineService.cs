using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Linq;
using Microsoft.Win32;
using Flow.Launcher.Plugin.WallpaperEngineController.Models;

namespace Flow.Launcher.Plugin.WallpaperEngineController
{
    public class WallpaperEngineService
    {
        private const string WE_APP_ID = "431960";
        private readonly Settings _settings;
        private readonly Action _saveSettings;
        private string? _cachedInstallDir;
        private List<int>? _cachedActiveMonitors;
        private DateTime _lastConfigWriteTimeUtc;
        private int _lastPhysicalMonitors;

        public WallpaperEngineService(Settings settings, Action saveSettings)
        {
            _settings = settings;
            _saveSettings = saveSettings;
        }

        public string? GetInstallDirectory()
        {
            if (!string.IsNullOrEmpty(_cachedInstallDir) && Directory.Exists(_cachedInstallDir))
            {
                return _cachedInstallDir;
            }

            _cachedInstallDir = FindInstallPath();
            return _cachedInstallDir;
        }

        public string? GetExecutablePath()
        {
            var dir = GetInstallDirectory();
            if (dir == null) return null;

            string exe64 = Path.Combine(dir, "wallpaper64.exe");
            string exe32 = Path.Combine(dir, "wallpaper32.exe");

            // If a specific wallpaper process is actively running, match its architecture
            try
            {
                if (Process.GetProcessesByName("wallpaper32").Length > 0 && File.Exists(exe32))
                    return exe32;
                if (Process.GetProcessesByName("wallpaper64").Length > 0 && File.Exists(exe64))
                    return exe64;
            }
            catch
            {
                // Ignore process query permission errors
            }

            // Fallback: prefer 64-bit on 64-bit OS if present
            if (Environment.Is64BitOperatingSystem && File.Exists(exe64))
                return exe64;

            if (File.Exists(exe32))
                return exe32;

            if (File.Exists(exe64))
                return exe64;

            return null;
        }

        public string? GetConfigPath()
        {
            var dir = GetInstallDirectory();
            if (dir == null) return null;

            string config = Path.Combine(dir, "config.json");
            if (File.Exists(config)) return config;

            // Alternative location in appdata
            string appDataConfig = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WallpaperEngine",
                "config.json");
            if (File.Exists(appDataConfig)) return appDataConfig;

            return null;
        }

        public bool IsAvailable()
        {
            return GetExecutablePath() != null;
        }

        public void SetCustomPath(string exePath)
        {
            if (File.Exists(exePath))
            {
                _settings.CustomExePath = exePath;
                _cachedInstallDir = Path.GetDirectoryName(exePath);
                _saveSettings();
            }
        }

        public void ClearCustomPath()
        {
            _settings.CustomExePath = null;
            _cachedInstallDir = null;
            _saveSettings();
        }

        public void Refresh()
        {
            _cachedInstallDir = null;
            _cachedActiveMonitors = null;
            GetInstallDirectory();
        }

        // ================= WIN32 INTEROP =================

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        private const int SM_CMONITORS = 80;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        public void CleanupOrphanedHelperProcesses()
        {
            try
            {
                var activeMainPids = new HashSet<uint>();
                foreach (var p in Process.GetProcessesByName("wallpaper64")) activeMainPids.Add((uint)p.Id);
                foreach (var p in Process.GetProcessesByName("wallpaper32")) activeMainPids.Add((uint)p.Id);

                // If no main wallpaper engine process is running, all UI helper processes are orphans
                if (activeMainPids.Count == 0)
                {
                    foreach (var ui in Process.GetProcessesByName("wallpaperui"))
                    {
                        try { ui.Kill(); } catch { }
                    }
                    ClearStaleLockFiles();
                    return;
                }

                // Otherwise, inspect process tree to find orphaned UI helpers whose parents have died
                IntPtr snap = CreateToolhelp32Snapshot(0x00000002, 0); // TH32CS_SNAPPROCESS = 2
                if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return;

                try
                {
                    var allRunningPids = new HashSet<uint>();
                    var uiParents = new Dictionary<uint, uint>();

                    var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                    if (Process32First(snap, ref entry))
                    {
                        do
                        {
                            allRunningPids.Add(entry.th32ProcessID);
                            if (entry.szExeFile.Equals("wallpaperui.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                uiParents[entry.th32ProcessID] = entry.th32ParentProcessID;
                            }
                        } while (Process32Next(snap, ref entry));
                    }

                    foreach (var kvp in uiParents)
                    {
                        uint uiPid = kvp.Key;
                        uint parentPid = kvp.Value;

                        // If parent process no longer exists in running processes, it's an orphan
                        if (!allRunningPids.Contains(parentPid))
                        {
                            try
                            {
                                var orphan = Process.GetProcessById((int)uiPid);
                                orphan.Kill();
                            }
                            catch { }
                        }
                    }
                }
                finally
                {
                    CloseHandle(snap);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        public void ClearStaleLockFiles()
        {
            try
            {
                var dir = GetInstallDirectory();
                if (dir == null) return;

                string uicache = Path.Combine(dir, "ui", "uicache");
                if (!Directory.Exists(uicache)) return;

                foreach (var file in Directory.GetFiles(uicache, "*LOCK*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        public bool ExecuteCommand(string command, string args = "")
        {
            string? exe = GetExecutablePath();
            if (exe == null || !File.Exists(exe)) return false;

            try
            {
                string arguments = string.IsNullOrWhiteSpace(args) 
                    ? $"-control {command}" 
                    : $"-control {command} {args}";

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                    UseShellExecute = true
                };

                using var proc = Process.Start(psi);

                // Run background cleanup to terminate any orphaned UI processes left from previous instances
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(1000);
                    CleanupOrphanedHelperProcesses();
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool OpenWindow(string windowType)
        {
            var dir = GetInstallDirectory();
            if (dir == null) return false;

            try
            {
                // Clean any stale locks or orphaned processes before launching UI
                CleanupOrphanedHelperProcesses();

                string rootLauncher = Path.Combine(dir, "launcher.exe");
                string exe64 = Path.Combine(dir, "wallpaper64.exe");
                string exe32 = Path.Combine(dir, "wallpaper32.exe");

                // Priority 1: launcher.exe in root directory (safely handles UI for both Steam and cracked versions)
                if (File.Exists(rootLauncher))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = rootLauncher,
                        WorkingDirectory = dir,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    return true;
                }

                // Priority 2: main wallpaper executable
                string? mainExe = GetExecutablePath();
                if (mainExe != null && File.Exists(mainExe))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = mainExe,
                        WorkingDirectory = dir,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool Quit()
        {
            // Execute quit command via CLI
            bool result = ExecuteCommand("quit");
            if (!result)
            {
                // Fallback: kill running wallpaper processes
                try
                {
                    foreach (var name in new[] { "wallpaper64", "wallpaper32", "wallpaperui" })
                    {
                        foreach (var proc in Process.GetProcessesByName(name))
                        {
                            proc.Kill();
                        }
                    }
                    result = true;
                }
                catch
                {
                    // Ignore
                }
            }

            // Clean up stale lock files
            ClearStaleLockFiles();
            return result;
        }

        // ================= CONFIG DATA PARSING =================

        public List<int> GetActiveMonitors()
        {
            int physicalMonitors = 1;
            try
            {
                physicalMonitors = Math.Max(1, GetSystemMetrics(SM_CMONITORS));
            }
            catch
            {
                // Default to 1 if API fails
            }

            string? configPath = GetConfigPath();
            if (configPath != null && File.Exists(configPath))
            {
                try
                {
                    DateTime writeTime = File.GetLastWriteTimeUtc(configPath);
                    if (_cachedActiveMonitors != null &&
                        writeTime == _lastConfigWriteTimeUtc &&
                        physicalMonitors == _lastPhysicalMonitors)
                    {
                        return _cachedActiveMonitors;
                    }
                    _lastConfigWriteTimeUtc = writeTime;
                    _lastPhysicalMonitors = physicalMonitors;
                }
                catch
                {
                    // Ignore file timestamp check errors
                }
            }

            var monitors = new SortedSet<int>();
            var doc = LoadConfigDocument();
            if (doc != null)
            {
                using (doc)
                {
                    try
                    {
                        FindConfiguredMonitors(doc.RootElement, monitors);
                    }
                    catch
                    {
                        // Ignore parsing errors
                    }
                }
            }

            // Filter out any monitor numbers exceeding physical monitors or invalid (< 1)
            monitors.RemoveWhere(m => m > physicalMonitors || m < 1);

            // If no active monitors were detected in config, fall back to physical monitors
            if (monitors.Count == 0)
            {
                for (int i = 1; i <= physicalMonitors; i++)
                {
                    monitors.Add(i);
                }
            }

            _cachedActiveMonitors = monitors.ToList();
            return _cachedActiveMonitors;
        }

        private void FindConfiguredMonitors(JsonElement root, SortedSet<int> monitors)
        {
            JsonElement userNode = default;
            bool foundUserNode = false;

            // Check exact match for Windows username
            if (root.TryGetProperty(Environment.UserName, out var exactUser))
            {
                userNode = exactUser;
                foundUserNode = true;
            }
            else
            {
                // Check case-insensitive match for Windows username
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase))
                    {
                        userNode = prop.Value;
                        foundUserNode = true;
                        break;
                    }
                }
            }

            var monitorMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (foundUserNode)
            {
                BuildMonitorMap(userNode, monitorMap);
                ExtractActiveMonitors(userNode, monitors, monitorMap);
            }
            else
            {
                BuildMonitorMap(root, monitorMap);
                ExtractActiveMonitors(root, monitors, monitorMap);
            }
        }

        private void BuildMonitorMap(JsonElement element, Dictionary<string, int> monitorMap)
        {
            if (element.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.NameEquals("monitormap") && prop.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var mapEntry in prop.Value.EnumerateObject())
                    {
                        if (mapEntry.Value.ValueKind == JsonValueKind.Object &&
                            mapEntry.Value.TryGetProperty("location", out var locProp) &&
                            locProp.TryGetInt32(out int loc))
                        {
                            monitorMap[mapEntry.Name] = loc;
                        }
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    BuildMonitorMap(prop.Value, monitorMap);
                }
            }
        }

        private void ExtractActiveMonitors(JsonElement element, SortedSet<int> monitors, Dictionary<string, int> monitorMap)
        {
            if (element.ValueKind != JsonValueKind.Object) return;

            int ordinalIndex = 0;
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.NameEquals("selectedwallpapers") && prop.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var monitorProp in prop.Value.EnumerateObject())
                    {
                        var val = monitorProp.Value;
                        if (val.ValueKind != JsonValueKind.Object) continue;

                        // Validate that this monitor actually has an active wallpaper or playlist
                        bool hasFile = val.TryGetProperty("file", out var fileProp) &&
                                       fileProp.ValueKind == JsonValueKind.String &&
                                       !string.IsNullOrWhiteSpace(fileProp.GetString());

                        bool hasPlaylist = val.TryGetProperty("playlist", out var plProp) &&
                                           plProp.ValueKind == JsonValueKind.Object &&
                                           plProp.TryGetProperty("items", out var itemsProp) &&
                                           itemsProp.ValueKind == JsonValueKind.Array &&
                                           itemsProp.GetArrayLength() > 0;

                        if (!hasFile && !hasPlaylist) continue;

                        string key = monitorProp.Name;
                        int zeroBasedLocation = -1;

                        // Mode 1: GDI / Index name e.g. "Monitor0", "Monitor1"
                        if (key.StartsWith("Monitor", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(key.AsSpan(7), out int parsedIdx))
                        {
                            zeroBasedLocation = parsedIdx;
                        }
                        // Mode 2 & 3: Device Path or Layout mode (look up in monitormap)
                        else if (monitorMap.TryGetValue(key, out int mappedLoc))
                        {
                            zeroBasedLocation = mappedLoc;
                        }
                        else
                        {
                            zeroBasedLocation = ordinalIndex;
                        }

                        if (zeroBasedLocation >= 0)
                        {
                            monitors.Add(zeroBasedLocation + 1); // 1-based UI index
                        }

                        ordinalIndex++;
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    ExtractActiveMonitors(prop.Value, monitors, monitorMap);
                }
            }
        }

        public List<WallpaperProfile> GetProfiles()
        {
            var list = new List<WallpaperProfile>();
            var doc = LoadConfigDocument();
            if (doc == null) return list;

            using (doc)
            {
                try
                {
                    // Search recursively or at known locations for "profiles"
                    JsonElement root = doc.RootElement;
                    FindProfileNames(root, list);
                }
                catch
                {
                    // Ignore JSON parsing errors
                }
            }
            return list;
        }

        public List<WallpaperPlaylist> GetPlaylists()
        {
            var list = new List<WallpaperPlaylist>();
            var doc = LoadConfigDocument();
            if (doc == null) return list;

            using (doc)
            {
                try
                {
                    JsonElement root = doc.RootElement;
                    FindPlaylistNames(root, list);
                }
                catch
                {
                    // Ignore JSON parsing errors
                }
            }
            return list;
        }

        public List<RecentWallpaper> GetRecentWallpapers()
        {
            var list = new List<RecentWallpaper>();
            var doc = LoadConfigDocument();
            if (doc == null) return list;

            using (doc)
            {
                try
                {
                    JsonElement root = doc.RootElement;
                    FindRecentWallpaperPaths(root, list);
                }
                catch
                {
                    // Ignore JSON parsing errors
                }
            }
            return list;
        }

        private JsonDocument? LoadConfigDocument()
        {
            string? configPath = GetConfigPath();
            if (configPath == null || !File.Exists(configPath)) return null;

            try
            {
                using var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var options = new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                };
                return JsonDocument.Parse(stream, options);
            }
            catch
            {
                return null;
            }
        }

        private void FindProfileNames(JsonElement element, List<WallpaperProfile> results)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.NameEquals("profiles") || prop.NameEquals("profilelist"))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                    AddUniqueProfile(results, item.GetString());
                                else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var n))
                                    AddUniqueProfile(results, n.GetString());
                            }
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var p in prop.Value.EnumerateObject())
                            {
                                AddUniqueProfile(results, p.Name);
                            }
                        }
                    }
                    else
                    {
                        FindProfileNames(prop.Value, results);
                    }
                }
            }
        }

        private void FindPlaylistNames(JsonElement element, List<WallpaperPlaylist> results)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.NameEquals("playlists") || prop.NameEquals("playlistlist"))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                    AddUniquePlaylist(results, item.GetString());
                                else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var n))
                                    AddUniquePlaylist(results, n.GetString());
                            }
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var p in prop.Value.EnumerateObject())
                            {
                                AddUniquePlaylist(results, p.Name);
                            }
                        }
                    }
                    else
                    {
                        FindPlaylistNames(prop.Value, results);
                    }
                }
            }
        }

        private void FindRecentWallpaperPaths(JsonElement element, List<RecentWallpaper> results)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.NameEquals("wallpaperconfigrecent") || 
                        prop.NameEquals("recent") || 
                        prop.NameEquals("recentwallpapers") || 
                        prop.NameEquals("history"))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                string? path = null;
                                string? title = null;

                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    path = item.GetString();
                                }
                                else if (item.ValueKind == JsonValueKind.Object)
                                {
                                    if (item.TryGetProperty("title", out var t)) title = t.GetString();
                                    if (item.TryGetProperty("file", out var f)) path = f.GetString();
                                    else if (item.TryGetProperty("path", out var p)) path = p.GetString();

                                    // Check nested: item.config.selectedwallpapers.Monitor*.file
                                    if (string.IsNullOrEmpty(path) && item.TryGetProperty("config", out var config))
                                    {
                                        if (config.TryGetProperty("selectedwallpapers", out var selected))
                                        {
                                            foreach (var monitorProp in selected.EnumerateObject())
                                            {
                                                if (monitorProp.Value.TryGetProperty("file", out var mf))
                                                {
                                                    path = mf.GetString();
                                                    if (!string.IsNullOrEmpty(path)) break;
                                                }
                                            }
                                        }
                                    }
                                }

                                if (!string.IsNullOrEmpty(path))
                                {
                                    AddRecentWallpaper(results, path, title);
                                }
                            }
                        }
                    }
                    else
                    {
                        FindRecentWallpaperPaths(prop.Value, results);
                    }
                }
            }
        }

        private void AddRecentWallpaper(List<RecentWallpaper> results, string path, string? title)
        {
            if (results.Exists(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
                return;

            string displayTitle = title ?? "";
            string? previewImage = null;

            if (File.Exists(path) || Directory.Exists(path))
            {
                string dir = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
                string projectJsonPath = Path.Combine(dir, "project.json");
                
                if (File.Exists(projectJsonPath))
                {
                    try
                    {
                        string pjson = File.ReadAllText(projectJsonPath);
                        using var pdoc = JsonDocument.Parse(pjson);
                        if (string.IsNullOrEmpty(displayTitle) && pdoc.RootElement.TryGetProperty("title", out var t))
                        {
                            displayTitle = t.GetString() ?? "";
                        }
                        if (pdoc.RootElement.TryGetProperty("preview", out var prev))
                        {
                            string prevFile = Path.Combine(dir, prev.GetString() ?? "");
                            if (File.Exists(prevFile)) previewImage = prevFile;
                        }
                    }
                    catch
                    {
                        // Ignore project.json read errors
                    }
                }

                if (string.IsNullOrEmpty(displayTitle))
                {
                    displayTitle = Path.GetFileNameWithoutExtension(path);
                }
            }
            else if (string.IsNullOrEmpty(displayTitle))
            {
                displayTitle = Path.GetFileNameWithoutExtension(path);
            }

            results.Add(new RecentWallpaper(path, displayTitle, previewImage));
        }

        private static void AddUniqueProfile(List<WallpaperProfile> list, string? name)
        {
            if (!string.IsNullOrWhiteSpace(name) && !list.Exists(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new WallpaperProfile(name));
            }
        }

        private static void AddUniquePlaylist(List<WallpaperPlaylist> list, string? name)
        {
            if (!string.IsNullOrWhiteSpace(name) && !list.Exists(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new WallpaperPlaylist(name));
            }
        }

        // ================= DISCOVERY PIPELINE =================

        private string? FindInstallPath()
        {
            // Tier 1: User-configured custom path from Settings
            if (!string.IsNullOrEmpty(_settings.CustomExePath) && File.Exists(_settings.CustomExePath))
            {
                return Path.GetDirectoryName(_settings.CustomExePath);
            }

            // Tier 2: Check currently running process (wallpaper64, wallpaper32, ui32)
            string? runningPath = GetPathFromRunningProcess();
            if (runningPath != null)
            {
                _settings.CustomExePath = runningPath;
                _saveSettings();
                return Path.GetDirectoryName(runningPath);
            }

            // Tier 3: Check Windows Startup Registry (HKCU / HKLM Run keys)
            string? startupPath = GetPathFromAutostartRegistry();
            if (startupPath != null)
            {
                return Path.GetDirectoryName(startupPath);
            }

            // Tier 4: Steam Detection Chain (Registry -> libraryfolders.vdf -> acf)
            string? steamPath = GetSteamPathFromRegistry();
            if (steamPath != null)
            {
                var libraries = GetSteamLibraryFolders(steamPath);
                foreach (var lib in libraries)
                {
                    string steamapps = Path.Combine(lib, "steamapps");
                    string manifest = Path.Combine(steamapps, $"appmanifest_{WE_APP_ID}.acf");
                    if (File.Exists(manifest))
                    {
                        string? installDir = ParseInstallDirFromManifest(manifest);
                        if (installDir != null)
                        {
                            string fullPath = Path.Combine(steamapps, "common", installDir);
                            if (Directory.Exists(fullPath) && File.Exists(Path.Combine(fullPath, "wallpaper64.exe")))
                            {
                                return fullPath;
                            }
                        }
                    }
                }
            }

            // Tier 5: Common Standalone Paths across all drives
            var commonPaths = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Wallpaper Engine"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Wallpaper Engine")
            };

            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        commonPaths.Add(Path.Combine(drive.RootDirectory.FullName, "Wallpaper Engine"));
                        commonPaths.Add(Path.Combine(drive.RootDirectory.FullName, "Games", "Wallpaper Engine"));
                    }
                }
            }
            catch
            {
                // Ignore drive inspection issues
            }

            foreach (var path in commonPaths)
            {
                if (Directory.Exists(path) && (File.Exists(Path.Combine(path, "wallpaper64.exe")) || File.Exists(Path.Combine(path, "wallpaper32.exe"))))
                {
                    return path;
                }
            }

            return null;
        }

        private static string? GetPathFromRunningProcess()
        {
            string[] processNames = { "wallpaper64", "wallpaper32", "ui32" };
            foreach (var name in processNames)
            {
                try
                {
                    var procs = Process.GetProcessesByName(name);
                    if (procs.Length > 0)
                    {
                        string? filePath = procs[0].MainModule?.FileName;
                        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                        {
                            string dir = Path.GetDirectoryName(filePath)!;
                            string wp64 = Path.Combine(dir, "wallpaper64.exe");
                            if (File.Exists(wp64)) return wp64;

                            string wp32 = Path.Combine(dir, "wallpaper32.exe");
                            if (File.Exists(wp32)) return wp32;

                            return filePath;
                        }
                    }
                }
                catch
                {
                    // Ignore permission errors
                }
            }
            return null;
        }

        private static string? GetPathFromAutostartRegistry()
        {
            string[] subkeys = {
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run"
            };

            var hives = new[] { Registry.CurrentUser, Registry.LocalMachine };

            foreach (var hive in hives)
            {
                foreach (var subkey in subkeys)
                {
                    try
                    {
                        using var key = hive.OpenSubKey(subkey);
                        if (key == null) continue;

                        foreach (var valueName in key.GetValueNames())
                        {
                            if (key.GetValue(valueName) is string val)
                            {
                                string? exe = ExtractExeFromCommandString(val);
                                if (exe != null && (exe.Contains("wallpaper64.exe", StringComparison.OrdinalIgnoreCase) ||
                                                    exe.Contains("wallpaper32.exe", StringComparison.OrdinalIgnoreCase)))
                                {
                                    if (File.Exists(exe)) return exe;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }

            return null;
        }

        private static string? ExtractExeFromCommandString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            raw = raw.Trim();
            if (raw.StartsWith("\""))
            {
                int endQuote = raw.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    return raw.Substring(1, endQuote - 1);
                }
            }

            int spaceIndex = raw.IndexOf(' ');
            return spaceIndex > 0 ? raw.Substring(0, spaceIndex) : raw;
        }

        private static string? GetSteamPathFromRegistry()
        {
            string[] keys = {
                @"SOFTWARE\Wow6432Node\Valve\Steam",
                @"SOFTWARE\Valve\Steam"
            };
            foreach (var keyPath in keys)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                    if (key?.GetValue("InstallPath") is string path && Directory.Exists(path))
                        return path;
                }
                catch
                {
                    // Ignore
                }
            }
            return null;
        }

        private static List<string> GetSteamLibraryFolders(string steamRoot)
        {
            var folders = new List<string> { steamRoot };
            string vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");

            if (!File.Exists(vdfPath)) return folders;

            try
            {
                foreach (var line in File.ReadAllLines(vdfPath))
                {
                    if (line.Contains("\"path\""))
                    {
                        int start = line.IndexOf('"', line.IndexOf("\"path\"") + 6) + 1;
                        int end = line.IndexOf('"', start);
                        if (start > 0 && end > start)
                        {
                            string dir = line[start..end].Replace("\\\\", "\\");
                            if (Directory.Exists(dir) && !folders.Contains(dir))
                                folders.Add(dir);
                        }
                    }
                }
            }
            catch
            {
                // Ignore file read error
            }

            return folders;
        }

        private static string? ParseInstallDirFromManifest(string manifestPath)
        {
            try
            {
                foreach (var line in File.ReadAllLines(manifestPath))
                {
                    if (line.Contains("\"installdir\""))
                    {
                        int start = line.IndexOf('"', line.IndexOf("\"installdir\"") + 12) + 1;
                        int end = line.IndexOf('"', start);
                        if (start > 0 && end > start)
                            return line[start..end];
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return null;
        }
    }
}

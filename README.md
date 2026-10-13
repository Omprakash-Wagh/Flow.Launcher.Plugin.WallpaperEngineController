# Flow Launcher Plugin - Wallpaper Engine Controller

<p align="center">
  <img src="Images/icon.png" alt="Wallpaper Engine Controller Logo" width="96" height="96" />
</p>

<p align="center">
  <b>Control Wallpaper Engine directly from Flow Launcher with right-click tray actions, dynamic multi-monitor management, and instant wallpaper switching.</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-purple?style=flat-square" alt=".NET 9.0" />
  <img src="https://img.shields.io/badge/Flow%20Launcher-Plugin-blue?style=flat-square" alt="Flow Launcher" />
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square" alt="Windows" />
</p>

---

## 🌟 Features

- **Full Playback Control:** Play, pause, stop, mute, and unmute all wallpapers with instant CLI commands.
- **Dynamic Display Detection:** Automatically detects physical displays (`SM_CMONITORS`) and monitors genuinely running Wallpaper Engine from `config.json`.
  - **Single Monitor:** Shows a clean, uncluttered `Next Wallpaper` action without redundant `(All Monitors)` or ghost monitor entries.
  - **Multiple Monitors:** Displays `Next Wallpaper (All Monitors)` alongside individual actions for each active display.
- **Profiles & Playlists:** Search and switch between your saved Wallpaper Engine profiles and playlists directly from the search bar.
- **Recent Wallpapers Browser:** Browse recent wallpapers with titles and preview thumbnails.
  - **Context Menu (`Ctrl + O` / Right-Click):** Apply to all monitors, target a specific display, or open the folder directly in Windows File Explorer.
- **Desktop Icons Toggle:** Quickly show or hide desktop icons with `wp icons`.
- **5-Tier Auto-Discovery Pipeline:** Works out of the box with Steam editions, standalone/portable versions, and custom installation paths without configuration.
- **Zero CEF Deadlocks:** Safely launches Wallpaper Engine UI using root launchers and automated lockfile cleanup, avoiding Chromium multi-instance freezes.

---

## 🚀 Commands

| Command | Action |
| :--- | :--- |
| `wp` | Open the main action menu |
| `wp play` | Resume wallpaper playback |
| `wp pause` | Pause / freeze wallpapers |
| `wp stop` | Unload and clear all wallpapers |
| `wp mute` | Mute wallpaper audio output |
| `wp unmute` | Restore wallpaper audio output |
| `wp next` | Cycle to next wallpaper on playlist (auto-detects single vs. multi-monitor) |
| `wp next <monitor>` | Cycle to next wallpaper on a specific display (e.g. `wp next 1`) |
| `wp profile <filter>` | Search and load saved Wallpaper Engine profiles |
| `wp playlist <filter>` | Search and load saved Wallpaper Engine playlists |
| `wp recent <filter>` | Search and apply recently used wallpapers |
| `wp icons` | Toggle Windows desktop icons visibility (`show` / `hide`) |
| `wp settings` | Open Wallpaper Engine (gear icon in top right opens Settings) |
| `wp quit` | Completely exit Wallpaper Engine |

---

## 🖥️ Multi-Monitor Support

The plugin dynamically adapts to your display configuration:
- **Single Monitor Setup:** Typing `wp next` yields exactly **one** result to advance your wallpaper.
- **Multi-Monitor Setup:** Shows options to advance all playlists or target individual displays (`Monitor 1`, `Monitor 2`, etc.).
- **Context Menu:** Press `Ctrl + O` or right-click any recent wallpaper in `wp recent` to apply it across all screens, or exclusively to a specific monitor.

---

## ⚙️ Configuration & Auto-Detection

The plugin automatically searches for Wallpaper Engine using a 5-tier discovery chain:
1. **User Custom Path:** Configured in Flow Launcher Plugin Settings.
2. **Running Process:** Inspects live `wallpaper64.exe` or `wallpaper32.exe` process modules.
3. **Startup Registry:** Checks `HKCU` and `HKLM` Windows autostart keys.
4. **Steam Libraries:** Parses `libraryfolders.vdf` across all mounted drives and libraries.
5. **Standalone Drive Scanner:** Scans all ready system drives (`Program Files`, drive roots) for portable installations.

If your executable is in a non-standard directory, open **Flow Launcher Settings > Plugins > Wallpaper Engine Controller** and click **Browse** to select `wallpaper64.exe`.

---

## 🛠️ Building from Source

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows 10 / 11

### Build Command
```powershell
dotnet build --configuration Release
```

The output assembly and plugin assets will be compiled directly to `bin/Release/`.

---

## 📦 Packaging & Release

To package the plugin for Flow Launcher:
```powershell
dotnet publish -c Release -o ./publish
Compress-Archive -Path ./publish/* -DestinationPath Flow.Launcher.Plugin.WallpaperEngineController.zip
```

---

## 👤 Author

**Omprakash Wagh**  
- GitHub: [@Omprakash-Wagh](https://github.com/Omprakash-Wagh)

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).

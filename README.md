# ADB Studio

A modern, lightweight Windows desktop application for managing Android devices through **ADB** and **scrcpy** — no command line required.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-WPF-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

## ✨ Features

- 📱 **Device Management** — auto-detects connected devices, shows battery, storage, RAM, and Android version at a glance
- 🖥️ **Screen Mirroring** — launch scrcpy directly from the app with one click
- 🖲️ **Hardware Controls** — send Home, Back, Menu, Volume Up/Down key events instantly
- 📋 **Live Logcat Viewer** — filterable, pausable log stream with search
- ⚙️ **Process Monitor** — real-time CPU/RAM usage per running process, with auto-refresh
- 🔌 **Power Menu** — reboot to system, recovery, or bootloader, plus safe shutdown
- 🧰 **Quick Commands & Custom Hot Commands** — save your own frequently used ADB commands as one-click buttons
- 🗂️ **Custom Tool Paths** — point the app to your own ADB / scrcpy installation folders, or let it fall back to your system PATH
- 🎨 **Clean, modern UI** — built from scratch in WPF with a custom Material-inspired theme

## 🖼️ Preview

*(add screenshots or a GIF here)*

## 🚀 Getting Started

### Requirements
- Windows 10/11
- [.NET Desktop Runtime](https://dotnet.microsoft.com/download/dotnet)
- [ADB (Android Platform Tools)](https://developer.android.com/tools/releases/platform-tools)
- [scrcpy](https://github.com/Genymobile/scrcpy) (optional, required for screen mirroring)

### Installation
1. Download the latest release from the [Releases](../../releases) page
2. Extract and run `ADBStudio.exe`
3. On first launch, either place `adb.exe`/`scrcpy.exe` next to the app, add them to your system PATH, or set custom folders in **Settings**

### Usage
1. Connect your Android device via USB with USB debugging enabled
2. The device will appear automatically in the top device selector
3. Use the sidebar to mirror the screen, send hardware keys, or manage power state
4. Switch between **Devices**, **Logcat**, and **Processes** tabs from the left navigation

## ⚙️ Settings

- Enable a custom ADB/scrcpy directory (instead of the app folder or system PATH)
- Adjust the device auto-refresh interval
- Toggle confirmation prompts before reboot/shutdown
- Add your own **Hot Commands** — custom ADB shell commands that appear as quick-access buttons

## 🛠️ Built With
- WPF (.NET)
- ADB (Android Debug Bridge)
- [scrcpy](https://github.com/Genymobile/scrcpy)

## 🤝 Contributing
Pull requests are welcome! Feel free to open an issue for bugs or feature requests.

## 📄 License
This project is licensed under the MIT License — see [LICENSE](LICENSE) for details.

## 🙌 Credits
Created by **YaTeam**

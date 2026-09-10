# MacroBMR

**BMR Macro Recorder & Studio** — A visual-based macro recorder and automation player for Windows, featuring ADB integration for Android emulators.

[Download](https://github.com/SaKaSe2/MacroBMR/releases) · [Report Bug](https://github.com/SaKaSe2/MacroBMR/issues) · [Source Code](https://github.com/SaKaSe2/MacroBMR) · [License](LICENSE)

---

## What is MacroBMR?

MacroBMR is a Windows desktop application designed to record and automate mouse and keyboard action sequences. Unlike standard macro recorders that rely purely on rigid screen coordinates, MacroBMR captures **thumbnail screenshots** at every click point. During playback, it dynamically locates target UI elements via template matching.

Common use cases:
- Mobile game automation via emulators (BlueStacks, LDPlayer, etc.)
- Repetitive UI testing
- Routine desktop task automation

---

## Features

| Feature | Description |
|---|---|
| **Visual Playback** | Stores click thumbnails and matches screen elements dynamically during replay |
| **Background Mode** | Operates without bringing the target window to the foreground |
| **ADB Support** | Directly injects touch events into Android emulators via ADB without mouse hijacking |
| **Smart Fallback** | Automatically attempts alternative recorded coordinates if primary visual match fails |
| **Loop & Repeat** | Play recordings once, N times, or on an infinite loop |
| **Floating Overlay** | Floating toolbar during record and playback for quick controls |
| **Target Picker** | Interactive crosshair tool to select target application windows |
| **NexusClick** | Lightweight standalone auto-clicker sub-project |
| **.bmr File Format** | Clean JSON-based macro files for easy manual editing and external parsing |

---

## Getting Started

### Prerequisites

- Windows 10/11 (64-bit)
- [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
- Run as **Administrator** (required for global low-level mouse & keyboard hooks)

### Installation & Usage

1. Download `MacroBMR.exe` from [Releases](https://github.com/SaKaSe2/MacroBMR/releases)
2. Run `MacroBMR.exe` as Administrator
3. Click **Record** to start capturing actions
4. Click **Stop** when finished
5. Click **Play** to run playback

> Press **F12** at any time to emergency stop playback and restore the main window (especially helpful in background mode).

---

## `.bmr` File Format

Recordings are saved as JSON files in the `Recordings/` directory. Each action corresponds to a `MacroAction` object:

```json
{
  "action": "click",
  "x": 540.0,
  "y": 960.0,
  "button": "left",
  "seconds": 1.5,
  "click_thumbnail": "<base64 JPEG>",
  "click_thumbnail_bg": "<base64 JPEG>",
  "recorded_window": "BlueStacks App Player",
  "match_threshold": 90.0,
  "search_radius": 20,
  "fallbacks": []
}
```

Supported action types: `click`, `double_click`, `mousedown`, `mouseup`, `move`, `scroll`, `press`, `type`, `hotkey`, `wait`, `open_app`, `switch_window`.

---

## Project Structure

```
MacroBMR/
├── LICENSE
├── App.xaml / App.xaml.cs          Application entry point and design tokens
├── MacroBMR.csproj                 Main project file (.NET 6 WPF)
├── Core/
│   ├── MacroRecorder.cs            Global mouse/keyboard hooks -> MacroAction
│   ├── MacroExecutor.cs            Playback engine with template matching
│   ├── ScreenCapture.cs            Screen capture utils (CopyFromScreen & PrintWindow)
│   └── AdbHelper.cs                ADB bridge for Android emulator control
├── Models/
│   ├── MacroAction.cs              Single action data model (JSON schema)
│   └── MacroProject.cs             Action collection and project metadata
├── Views/
│   ├── MainWindow.xaml(.cs)        Primary dashboard interface
│   └── Overlays/
│       ├── OverlayWindow           Floating control toolbar
│       ├── TargetPickerOverlay     Interactive target window picker
│       └── VirtualCursorOverlay    Cursor visualizer for background mode
├── Properties/
│   ├── app.manifest                UAC Administrator execution level and DPI awareness
│   └── AssemblyInfo.cs
├── Assets/                         Logo assets (BMR.svg)
├── Recordings/                     Sample .bmr macro recordings
├── docs/                           Architecture and technical specifications
└── NexusClick/                     Standalone auto-clicker sub-project
```

---

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| [MouseKeyHook](https://github.com/gmamaladze/globalmousekeyhook) | 5.7.1 | Low-level global mouse & keyboard input hooks |
| [InputSimulatorPlus](https://github.com/TChatzigiannakis/InputSimulatorPlus) | 1.0.7 | Windows API keyboard & mouse simulation |
| [AdvancedSharpAdbClient](https://github.com/yungd1plomat/AdvancedSharpAdbClient) | 3.6.16 | Android Debug Bridge client |
| [Newtonsoft.Json](https://www.newtonsoft.com/json) | 13.0.4 | Serialization for `.bmr` recording files |
| [System.Drawing.Common](https://learn.microsoft.com/en-us/dotnet/api/system.drawing) | 6.0.0 | Screen capture and pixel-based template matching |
| [MaterialDesignThemes](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) | 4.9.0 | Material Design theme and WPF UI styling |

---

## Notes

- Built natively for 64-bit Windows.
- Requires .NET 6 Desktop Runtime (SDK not required for running pre-built binaries).
- ADB mode requires `adb.exe` accessible via system `PATH` or located in the application directory.
- Visual matching utilizes deterministic Sum of Squared Differences (SSD) pixel comparisons via `System.Drawing`, avoiding heavy external ML dependencies.

---

## License

Distributed under the [MIT License](LICENSE). Feel free to use, study, and modify with proper attribution to the original author.

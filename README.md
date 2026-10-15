# TomatoMix Audio Mixer

**TomatoMix** is a Windows desktop multi-channel application audio router, real-time mixer, ChatMix controller, and 10-band parametric/graphic equalizer built with **.NET 10**, **Avalonia UI**, and **WASAPI (NAudio)**.

---

## Features

- **Multi-Channel Virtual Mixing**
  - Built-in preset channels (**GAME**, **CHAT**, **MEDIA**, **AUX**) plus support for creating custom channels.
  - Per-channel volume faders, real-time peak level meters, **Mute (M)**, and **Solo (S)** controls.
  - Automatic and drag-and-drop/click-based per-application audio session routing.
- **Game / Chat Balance (ChatMix)**
  - Dedicated slider to balance volume dynamically between **GAME** and **CHAT** channels without leaving your game or adjusting individual apps.
- **Per-Channel & Microphone 10-Band Equalizer Studio**
  - 10-band EQ (`31 Hz`, `62 Hz`, `125 Hz`, `250 Hz`, `500 Hz`, `1 kHz`, `2 kHz`, `4 kHz`, `8 kHz`, `16 kHz`) with adjustable preamp gain (`-12 dB` to `+12 dB`) and interactive frequency response curve visualization.
  - Built-in presets including **Flat**, **FPS Footsteps (CS2 / Valorant)**, **Immersion / RPG Cinematic**, **Bass Boost**, **Punchy V-Shape (Music)**, **Voice Clarity (Discord / Podcast)**, **Warm Broadcast**, and **Night Mode**.
  - Save, delete, import, and export custom `.eqprofile.json` profiles.
  - **Built-in Test Loop Auditioning** & **Live Microphone Monitoring** via a real-time 10-band biquad DSP audio engine.
  - **Equalizer APO Integration**: Automatically syncs active EQ curves to `tomatomix_eq.txt` when Equalizer APO is installed for system-wide hardware output filtering.
- **Hardware Input & Output Control**
  - Select and control master output and microphone capture devices with live level metering.
- **Background Process & System Tray Integration**
  - Runs seamlessly in the Windows System Tray when closed or minimized (`Close to System Tray`, `Minimize to System Tray`), continuously routing newly launched applications and enforcing per-channel volumes, ChatMix, and Equalizer APO curves with low-CPU background polling.
  - System Tray icon with quick-access context menu (`Open TomatoMix Mixer`, `Hide to Background Tray`, `Mute Master Output`, `Mute Microphone`, `Keep Running in Background`, `Start with Windows`, and `Quit TomatoMix`).
  - Single-instance enforcement: launching TomatoMix while it is already running in the background automatically brings the existing mixer window to the foreground.
  - Optional **Start with Windows** auto-launch and **Start Minimized in Tray** (`--background` / `--minimized` / `--tray` CLI flags).
- **Dark & Light Themes**
  - Instant theme switching with persistent settings stored in `%AppData%\TomatoMix`.

---

## Installation

### Option 1: Windows MSI Installer (`TomatoMix.msi`)
1. Download `TomatoMix.msi` from the [Releases](../../releases) page.
2. Double-click `TomatoMix.msi` to install TomatoMix into `%LocalAppData%\Programs\TomatoMix` with Start Menu and Desktop shortcuts (no administrator privileges or certificates required).

### Option 2: Portable Executable (`TomatoMix-Portable.exe`)
1. Download `TomatoMix-Portable.exe` from the [Releases](../../releases) page.
2. Run `TomatoMix-Portable.exe` directly — no installation or external `.NET` runtime is required.

---

## Building from Source

### Prerequisites
- Windows 10 (version 1809+) or Windows 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Run in Development
```powershell
dotnet run
```

### Publish Portable Single-File EXE
```powershell
dotnet publish .\TomatoMix.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o .\dist\portable
```

---

## CI/CD & MSI Packaging

Windows `.msi` packaging is generated exclusively in GitHub Actions (`.github/workflows/release.yml`) so no installer projects or `.wxs` files clutter the repository:
- Triggered automatically by pushing a commit whose message contains `[build]`, or manually via **Actions -> Build and Release**.
- Reads the numeric `Major.Minor.Patch` version from `TomatoMix.csproj`; if that release tag already exists, it adds a build suffix (for example, `v1.0.0-2`).
- Dynamically generates the WiX v5 definition (`TomatoMix.wxs`) and publishes `TomatoMix.msi` alongside `TomatoMix-Portable.exe` as workflow artifacts and GitHub release assets.

---

## Configuration & Profiles

All user configuration and custom EQ profiles are stored in:
- **Config file**: `%AppData%\TomatoMix\mixer_config.json`
- **Custom EQ profiles**: `%AppData%\TomatoMix\EqProfiles\*.eqprofile.json`
- **Equalizer APO include file**: `C:\Program Files\EqualizerAPO\config\tomatomix_eq.txt` (falls back to `%AppData%\TomatoMix\tomatomix_eq.txt` if Equalizer APO is not installed)

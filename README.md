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
- **Dark & Light Themes**
  - Instant theme switching with persistent settings stored in `%AppData%\TomatoMix`.

---

## Installation

### Option 1: Portable Executable (`TomatoMix-Portable.exe`)
1. Download `TomatoMix-Portable.exe` from the [Releases](../../releases) page.
2. Run `TomatoMix-Portable.exe` directly — no installation or external `.NET` runtime is required.

### Option 2: Windows MSIX Package (`TomatoMix.msix`)
1. Download `TomatoMix.msix` (and `TomatoMix.cer` if signed with the CI self-signed certificate) from the [Releases](../../releases) page.
2. If using the CI self-signed build, trust the certificate once before installing:
   - Double-click `TomatoMix.cer` -> **Install Certificate...** -> **Local Machine** -> **Place all certificates in the following store** -> **Trusted People** (or **Trusted Root Certification Authorities**).
3. Double-click `TomatoMix.msix` and click **Install**.

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

## CI/CD & MSIX Packaging

MSIX packaging is generated exclusively in GitHub Actions (`.github/workflows/release.yml`) so no packaging projects or manifest files clutter the repository:
- Triggered automatically when pushing a `v*` tag (e.g., `v1.0.1`) or manually via **Actions -> Build & Release Windows MSIX & EXE**.
- Dynamically generates visual assets from `Assets/app.ico` and `AppxManifest.xml`, packs `TomatoMix.msix` using the Windows SDK (`makeappx.exe`), and signs it with `signtool.exe`.
- **Custom Code Signing Certificate (Optional)**:
  - Add `WINDOWS_PFX_BASE64` (base64-encoded `.pfx` file) and `WINDOWS_PFX_PASSWORD` to your repository's GitHub Actions secrets to sign the MSIX with your own certificate.
  - When those secrets are not set, the workflow automatically generates a self-signed certificate and attaches `TomatoMix.cer` alongside `TomatoMix.msix`.

---

## Configuration & Profiles

All user configuration and custom EQ profiles are stored in:
- **Config file**: `%AppData%\TomatoMix\mixer_config.json`
- **Custom EQ profiles**: `%AppData%\TomatoMix\EqProfiles\*.eqprofile.json`
- **Equalizer APO include file**: `C:\Program Files\EqualizerAPO\config\tomatomix_eq.txt` (falls back to `%AppData%\TomatoMix\tomatomix_eq.txt` if Equalizer APO is not installed)

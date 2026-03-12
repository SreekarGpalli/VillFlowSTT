# VillFlow

Voice dictation for Windows. Hold a hotkey, speak, text shows up at your cursor. Uses [Groq](https://groq.com)'s free API so there's no cost to run.

Hold `Ctrl+Space`, talk, release — it transcribes, optionally polishes spelling/grammar, and pastes wherever you were typing. Works in any Windows app. System tray icon, floating overlay so you know when it's recording. You can add fallback STT providers or bring your own OpenAI-compatible endpoint.

## Getting started

**Download:** Grab the latest `VillFlow.msi` or `VillFlow.App.exe` from [Releases](https://github.com/SreekarGpalli/VillFlowSTT/releases) and run it. No installer needed for the exe — just extract and go.

**Build from source:**
```powershell
git clone https://github.com/SreekarGpalli/VillFlowSTT.git
cd VillFlowSTT
dotnet publish VillFlow.App -c Release -r win-x64 --self-contained
```
Requires .NET 8 SDK on Windows 10/11 x64. Or run `.\Build-Release.ps1` to produce both the exe and MSI installer.

## Setup

On first run a setup wizard walks you through it: grab a free Groq API key from [console.groq.com/keys](https://console.groq.com/keys), pick your mic and hotkey (default is Ctrl+Space), choose your STT provider. Groq works out of the box. You can also turn on text polish if you want — same key.

## How it works

| Action | Result |
|--------|--------|
| Hold Ctrl+Space | Recording starts, overlay shows "Listening..." |
| Release | Transcribes → polishes (if enabled) → pastes at cursor |
| Right-click tray icon | Settings, setup wizard, quit |

## Providers

**STT:** Groq's Whisper (free tier) or any OpenAI-compatible `/audio/transcriptions` endpoint.

**Text polish:** Groq (Llama/Mixtral, free tier) or any OpenAI-compatible `/chat/completions` endpoint.

## Project layout

```
VillFlow.sln
VillFlow.Core/       # Business logic, no UI
  Services/          # Audio capture, STT, polish, text injection
  Orchestration/     # Pipeline controller
  Settings/         # Config persistence, API key handling
VillFlow.App/       # WPF UI
  Views/            # Overlay, settings, setup wizard
VillFlow.Installer/ # WiX MSI
```

## Security

API keys live in `%LOCALAPPDATA%\VillFlow\settings.json` and are encrypted on disk. Nothing is logged or sent except to the APIs you configure. All HTTPS, no telemetry.

## License

MIT

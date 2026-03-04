# VillFlow

Voice dictation for Windows. Hold a hotkey, speak, text appears at your cursor.

Uses [Groq](https://groq.com)'s free API — no cost to run.

## Features

- **Hold-to-dictate** — Hold `Ctrl+Space`, speak, release. Done.
- **Text polish** — Fixes spelling, grammar, punctuation. Strips filler words.
- **Works everywhere** — Pastes text at your cursor in any Windows app.
- **Fallback STT** — Primary + up to 2 fallback providers.
- **System tray app** — Floating overlay shows recording state.
- **Custom APIs** — Bring your own OpenAI-compatible endpoint.

## Getting Started

### Download
Grab `VillFlowSetup.exe` from [Releases](../../releases) and run the installer.

### Build from Source
```powershell
git clone https://github.com/SreekarGpalli/VillFlowSTT.git
cd VillFlowSTT
dotnet publish VillFlow.App -c Release -r win-x64 --self-contained
```
Requires .NET 8 SDK and Windows 10/11 x64.

## Setup

1. Setup wizard opens on first run
2. Get a free Groq API key at [console.groq.com/keys](https://console.groq.com/keys)
3. Pick your mic and hotkey (default: `Ctrl+Space`)
4. Configure STT provider — Groq works out of the box
5. Optionally enable text polish (same Groq key works)

## How It Works

| Action | Result |
|---|---|
| Hold `Ctrl+Space` | Starts recording, overlay shows "Listening..." |
| Release | Transcribes → polishes → pastes at cursor |
| Right-click tray icon | Settings, setup wizard, quit |

## Providers

**STT:** Groq (Whisper, free tier) or any OpenAI-compatible `/audio/transcriptions` endpoint.

**Text Polish:** Groq (Llama/Mixtral, free tier) or any OpenAI-compatible `/chat/completions` endpoint.

## Project Layout

```
VillFlow.sln
VillFlow.Core/           # Business logic — no UI dependency
  Services/              # Audio, hotkey, STT, polish, text injection
  Orchestration/         # Pipeline controller
  Settings/              # JSON config, persistence
VillFlow.App/            # WPF shell
  Views/                 # Overlay, settings, setup wizard
Installer/               # Inno Setup script
```

## Security

- API keys stay on your machine in `%LOCALAPPDATA%\VillFlow\settings.json`
- Nothing is logged or sent anywhere except the APIs you configure
- All calls over HTTPS
- No telemetry

## License

[MIT](LICENSE)

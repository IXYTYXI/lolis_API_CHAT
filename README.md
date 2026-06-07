# VPet LLM Chat Mod

Code plugin prototype for VPet / Virtual Desktop Pet Simulator.

The first milestone is an OpenAI-compatible chat plugin. It can also synthesize
voice for LLM replies through an OpenAI-compatible `POST /audio/speech`
endpoint or MiniMax's HTTP T2A endpoint.

## Features

- Registers a `LLM Chat` Talk API.
- Calls an OpenAI-compatible `POST /chat/completions` endpoint.
- Adds pet mode and likability to each user message for better context.
- Keeps short conversation history in memory.
- Provides an in-game WPF settings window.
- Supports optional TTS playback for LLM replies.
- Supports `OpenAI-compatible` and `MiniMax` TTS providers.
- Uses VPet's `PlayVoice` path for audio playback.
- Caches generated voice files under the plugin directory.

## Important Files

- `VPet.Plugin.LLMChat/LLMChatPlugin.cs`: plugin entry point.
- `VPet.Plugin.LLMChat/LLMChatTalkAPI.cs`: chat integration.
- `VPet.Plugin.LLMChat/LLMChatSettingWindow.cs`: settings window.
- `VPet.Plugin.LLMChat/OpenAICompatibleChatClient.cs`: chat client.
- `VPet.Plugin.LLMChat/OpenAICompatibleTextToSpeechClient.cs`: TTS client.
- `VPet.Plugin.LLMChat/MiniMaxTextToSpeechClient.cs`: MiniMax TTS client.
- `VPet.Plugin.LLMChat/LLMChatSettings.cs`: JSON settings model.
- `VPet.Plugin.LLMChat/1110_LLMChat/info.lps`: VPet mod metadata.

## Local SDK

This workspace uses a local .NET SDK install:

- SDK directory: `.dotnet`
- NuGet package cache: `.nuget-packages`
- CLI home: `.dotnet-home`

Build with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Or run manually:

```powershell
$root = Get-Location
$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_PACKAGES = Join-Path $root ".nuget-packages"
$env:APPDATA = Join-Path $root ".appdata"
$env:LOCALAPPDATA = Join-Path $root ".localappdata"
.\.dotnet\dotnet.exe restore .\VPet.Plugin.LLMChat\VPet.Plugin.LLMChat.csproj --configfile .\NuGet.Config
.\.dotnet\dotnet.exe build .\VPet.Plugin.LLMChat\VPet.Plugin.LLMChat.csproj -c Release --no-restore
```

## Settings

The plugin creates a `LLMChatSetting*.json` file in its plugin directory.
Most settings can also be edited from the VPet mod settings menu.
If the in-game menu is hard to find, edit this file directly:

```text
VPet/mod/1110_LLMChat/LLMChatSetting.json
```

Chat settings:

- `baseUrl`: base API URL, for example `https://api.openai.com/v1`.
- `model`: chat model name.
- `apiKey`: chat API key. If blank, the environment variable below is used.
- `apiKeyEnvironmentVariable`: defaults to `VPET_LLM_API_KEY`.
- `systemPrompt`: pet persona prompt.
- `temperature`, `maxTokens`, `keepHistoryTurns`, `timeoutSeconds`.

TTS settings:

- `enableTextToSpeech`: enables voice playback for model replies.
- `ttsProvider`: `OpenAI-compatible` or `MiniMax`.
- `ttsBaseUrl`: optional TTS API base URL. If blank, `baseUrl` is reused.
- `ttsEndpointPath`: optional custom TTS endpoint path or full URL.
- `ttsModel`: defaults to `gpt-4o-mini-tts`.
- `ttsVoice`: defaults to `alloy`.
- `ttsResponseFormat`: defaults to `mp3`.
- `ttsInstructions`: optional voice style prompt.
- `ttsApiKey`: optional TTS API key. If blank, chat API key is reused.
- `ttsAuthorizationScheme`: defaults to `Bearer`; set it to an empty value for providers that expect `Authorization: sk-...`.

MiniMax quick test settings:

- `ttsProvider`: `MiniMax`
- `ttsBaseUrl`: `https://api.minimax.io/v1`
- `ttsModel`: `speech-2.8-turbo` for speed, or `speech-2.8-hd` for quality.
- `ttsVoice`: `Chinese (Mandarin)_Cute_Spirit` is a good first pet voice.
- `ttsResponseFormat`: `mp3`
- `ttsApiKeyEnvironmentVariable`: `MINIMAX_API_KEY`

MiniMax HTTP T2A returns hex-encoded audio from `POST /v1/t2a_v2`; the plugin
decodes it and writes a cached audio file before asking VPet to play it.

## Output

Release DLL:

```text
VPet.Plugin.LLMChat/bin/Release/net8.0-windows/VPet.Plugin.LLMChat.dll
```

Ready-to-copy mod directory:

```text
dist/1110_LLMChat
```

Copy `dist/1110_LLMChat` directly into VPet's `mod` directory.

Expected runtime layout:

```text
VPet/mod/1110_LLMChat/info.lps
VPet/mod/1110_LLMChat/LLMChatSetting.json
VPet/mod/1110_LLMChat/plugin/VPet.Plugin.LLMChat.dll
```

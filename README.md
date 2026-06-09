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
- Supports safe model-selected actions through a whitelist.
- Tracks local short memory, long memory, preferences, and daily diary context.
- Lets the pet suggest a work/study/play activity and wait for player confirmation.
- Speaks short local lines with TTS when the pet is touched.
- Adds several LLM-themed money-earning jobs to reduce pure consumption loops.
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
- `VPet.Plugin.LLMChat/LLMChatMemoryStore.cs`: local memory, preferences, and diary storage.
- `VPet.Plugin.LLMChat/1110_LLMChat/info.lps`: VPet mod metadata.
- `VPet.Plugin.LLMChat/1110_LLMChat/LolisPersonality.md`: editable local pet personality prompt.
- `VPet.Plugin.LLMChat/1110_LLMChat/LolisShortMemory.json`: rolling short-term event memory.
- `VPet.Plugin.LLMChat/1110_LLMChat/LolisLongMemory.md`: editable long-term memory.
- `VPet.Plugin.LLMChat/1110_LLMChat/LolisPreferences.json`: automatic preference counters.
- `VPet.Plugin.LLMChat/1110_LLMChat/LolisDiary.md`: automatic daily diary.

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

Local personality prompt:

- `LolisPersonality.md` lives next to `LLMChatSetting.json` in the mod directory.
- The plugin reads it on each chat request and adds it to the model prompt as the
  pet's long-term personality and behavior rules.
- Edit this file to change the pet's name, tone, personality, boundaries, and
  shopping/work preferences without recompiling the mod.

Memory files:

- `LolisShortMemory.json` stores recent conversation/action events. It is updated
  automatically and trimmed to a small rolling window.
- `LolisLongMemory.md` stores durable facts and preferences. The model may append
  to it only through the whitelisted `remember_long_term` action, intended for
  explicit user requests like `记住我喜欢可乐`.
- `LolisPreferences.json` stores automatic counters for bought items, item
  categories, activity names, and activity types.
- `LolisDiary.md` stores one short local diary entry per day.
- These local context files are read into the prompt on each chat request.

Chat settings:

- `baseUrl`: base API URL, for example `https://api.openai.com/v1`.
- `model`: chat model name.
- `apiKey`: chat API key. If blank, the environment variable below is used.
- `apiKeyEnvironmentVariable`: defaults to `VPET_LLM_API_KEY`.
- `systemPrompt`: pet persona prompt.
- `temperature`, `maxTokens`, `keepHistoryTurns`, `timeoutSeconds`.
- `enableModelActions`: lets the model request whitelisted game actions.
- `llmWorkMoneyMultiplier`: multiplier applied to the added LLM money-earning
  jobs. `1.0` is base speed, `2.0` is double income, and values are clamped from
  `0.1` through `10.0`.

Model actions:

When model actions are enabled, the model may return a JSON object with a
`reply` and optional `actions`. The plugin only executes known safe actions:

- `open_chat`
- `open_llm_settings`
- `open_game_settings`
- `open_gallery`
- `show_panel`
- `reset_position`
- `move_pet` with `args.direction` (`left`, `right`, `up`, `down`) and optional `args.distance`, clamped to safe movement.
- `start_work`, `start_study`, and `start_play` with `args.name`, resolving the requested activity from VPet's current work/study/play lists and starting it after level, illness, and function-mode checks.
- `pick_activity`, selecting a suitable work/study/play activity from the current VPet list and asking the player for confirmation.
- `clear_pending_activity`, clearing the pending activity after the player refuses.
- `stop_work`, stopping the current work/study/play item with VPet's manual-stop reason.
- `open_better_buy` with `args.name` or `args.type`, opening VPet's Better Buy page for the matching item category.
- `pick_wanted_item`, randomly selecting one item from the full current VPet food/item list and asking the player whether to buy it.
- `clear_wanted_item`, clearing the pending randomly selected item after the player refuses.
- `buy_and_use` with `args.name` and optional `args.count`, finding the matching item in the full Better Buy item list, checking money, charging `Price`, triggering Better Buy's take-item event, and using the item without opening the Better Buy window. `count` is clamped to `1` through `10`.
- `feed_by_name` with `args.name`, limited to known food, meal, snack, drink, or functional item names in the current VPet food list.
- `read_status`
- `set_zoom` with `args.level` clamped to `0.5` through `2.0`
- `play_tts` with short `args.text`

Shopping context is kept in memory. After buying one item, follow-up messages like
`再买咖啡`, `还有蛋糕`, or `再来一瓶` continue the Better Buy flow; repeated
items reuse the last bought item when the user does not name a new one.
Generic shop words such as `商店` or `更好买` open the Better Buy page instead of
being treated as product names.
Wanted-item context is also kept in memory. If the user asks the pet what it
wants, the plugin randomly picks from the full item list and asks for player
confirmation. Follow-up confirmation buys and uses that item directly; refusal
clears the pending item; asking to switch picks a new random item.

Activity context works similarly. If the user asks what the pet wants to do, the
plugin chooses a suitable work/study/play entry from the actual VPet activity
list, asks for confirmation, and only starts it after the player agrees.

Touch speech:

- The plugin listens to VPet head/body touch events.
- Touch responses are local short lines, displayed through `SayRnd` and spoken
  through the configured TTS path when TTS is enabled.
- Touch speech is throttled to avoid voice spam.

LLM jobs:

- The plugin appends several `LLM` money-earning work entries to VPet's current
  work list at runtime.
- Their money gain is multiplied by `llmWorkMoneyMultiplier`, configurable in
  the in-game settings window as `工作收益系数`.
- Indoor jobs use the current pet's work/coding-style animation when available.
- Outdoor jobs use the current pet's sleep animation as requested.
- Earnings still use VPet's normal work formula through `MoneyBase`, duration,
  and state consumption; the plugin does not directly grant money.

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
VPet/mod/1110_LLMChat/LolisPersonality.md
VPet/mod/1110_LLMChat/LolisShortMemory.json
VPet/mod/1110_LLMChat/LolisLongMemory.md
VPet/mod/1110_LLMChat/LolisPreferences.json
VPet/mod/1110_LLMChat/LolisDiary.md
VPet/mod/1110_LLMChat/plugin/VPet.Plugin.LLMChat.dll
```

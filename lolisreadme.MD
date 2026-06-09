# lolis_API_CHAT project context

This file is the long-form memory for the VPet LLM Chat mod. Keep it updated
when behavior changes so future work does not depend on chat history.

## Goal

Build a code-plugin mod for Virtual Desktop Pet Simulator / VPet that lets the
desktop pet talk through an OpenAI-compatible LLM API, optionally speak replies
with TTS, and safely control selected game actions.

The desired UX is an in-game pet companion:

- Chat from the toolbar or VPet talk API.
- Configure API/TTS from an in-game settings window.
- Let the model request whitelisted game actions only.
- Let normal pet touches produce local speech bubbles and TTS when configured.
- Add reasonable money-earning jobs so shopping is balanced by income.
- Preserve player agency for spending money: the model may suggest or ask, but
  direct purchases should only happen after the player asks or confirms.

## Current Build And Layout

Use the local SDK and build script:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The current distributable mod is:

```text
dist/1110_LLMChat
```

Runtime layout in VPet should be:

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

Do not test the old `dist/LLMChat` folder unless it is deliberately refreshed;
that directory has held stale DLLs during development.

## Important Files

- `VPet.Plugin.LLMChat/LLMChatPlugin.cs`: plugin entry, settings lifecycle,
  toolbar/settings menu entries, TTS playback, model action execution, game API
  calls, touch speech, extra work definitions, Better Buy purchase logic.
- `VPet.Plugin.LLMChat/LLMChatTalkAPI.cs`: VPet `TalkBox` integration and the
  action prompt sent to the model.
- `VPet.Plugin.LLMChat/LLMChatActionParser.cs`: extracts JSON action plans from
  model output, including mixed natural text plus embedded JSON.
- `VPet.Plugin.LLMChat/LLMChatInputWindow.cs`: compact persistent chat input
  window.
- `VPet.Plugin.LLMChat/LLMChatSettingWindow.cs`: in-game settings UI.
- `VPet.Plugin.LLMChat/LLMChatSettings.cs`: JSON settings model and defaults.
- `VPet.Plugin.LLMChat/OpenAICompatibleChatClient.cs`: chat completions client.
- `VPet.Plugin.LLMChat/OpenAICompatibleTextToSpeechClient.cs`: OpenAI-style TTS.
- `VPet.Plugin.LLMChat/MiniMaxTextToSpeechClient.cs`: MiniMax HTTP T2A client.
- `VPet.Plugin.LLMChat/1110_LLMChat/LolisPersonality.md`: local editable pet
  personality prompt shipped with the mod.
- `VPet.Plugin.LLMChat/1110_LLMChat/LolisShortMemory.json`: rolling short-term
  memory template.
- `VPet.Plugin.LLMChat/1110_LLMChat/LolisLongMemory.md`: editable long-term
  memory template.
- `VPet.Plugin.LLMChat/1110_LLMChat/LolisPreferences.json`: preference counter
  template for bought items and activity history.
- `VPet.Plugin.LLMChat/1110_LLMChat/LolisDiary.md`: automatic daily diary
  template.
- `VPet.Plugin.LLMChat/1110_LLMChat/info.lps`: mod metadata.
- `dist/1110_LLMChat/plugin/VPet.Plugin.LLMChat.dll`: built plugin DLL used for
  manual testing in VPet.

## Settings Notes

Chat API settings are OpenAI-compatible:

- Base URL example for OpenCode-style gateway: `https://xcode.best/v1` or the
  provider's documented `/v1` endpoint.
- Model can be any compatible chat model, for example `minimax-m3-free`.
- API key can be stored directly or read from `VPET_LLM_API_KEY`.
- `llmWorkMoneyMultiplier` controls added LLM job income. Default is `2.0`, and
  the settings window label is `工作收益系数`.

## Local Pet Personality

This is the file the user originally wanted when asking for a local README-like
context for the pet:

```text
VPet/mod/1110_LLMChat/LolisPersonality.md
```

Source template:

```text
VPet.Plugin.LLMChat/1110_LLMChat/LolisPersonality.md
```

Behavior:

- `LLMChatPlugin.LoadPlugin()` sets `PersonalityPath` to this file under the
  runtime mod directory.
- If the file is missing, `EnsurePersonalityFile()` writes a default personality
  template.
- `LLMChatTalkAPI.BuildPrompt()` calls `BuildLocalPersonalityPrompt()` on every
  chat request, so edits apply without recompiling.
- The personality text is prepended before game state and action instructions as
  `[本地桌宠人设]`.
- The current read length is capped to 6000 characters to avoid runaway token
  usage.

The template currently defines 萝莉斯 as a cute, natural, slightly playful
desktop pet that calls the user 主人, asks before spending money, and avoids
inventing unavailable goods, jobs, or features.

## Memory, Preferences, And Diary

Runtime files:

```text
VPet/mod/1110_LLMChat/LolisShortMemory.json
VPet/mod/1110_LLMChat/LolisLongMemory.md
VPet/mod/1110_LLMChat/LolisPreferences.json
VPet/mod/1110_LLMChat/LolisDiary.md
```

Behavior:

- `LLMChatMemoryStore` owns all four files.
- Short memory records recent events automatically:
  - user messages
  - assistant replies
  - requested model actions
  - state-driven proactive prompts
  - long-memory writes
  - purchases and started work/study/play activities
- Short memory keeps at most 40 events and includes the latest 18 in the prompt.
- Long memory is Markdown and is only appended through `remember_long_term`.
- The action prompt tells the model to use `remember_long_term` only when the
  user explicitly says to remember something.
- Preferences are automatic counters, currently written for:
  - `常买商品`
  - `常买分类`
  - `常做活动`
  - `活动类型`
- Diary writes one short local entry per day with current pet status. It is
  deliberately local and does not call the LLM.
- `LLMChatTalkAPI.BuildPrompt()` prepends personality, memory, preferences, and
  diary before game state and action instructions.

Current proactive state behavior:

- The plugin hooks `MW.Main.TimeUIHandle`.
- If function mode is enabled, it checks health, thirst, hunger, strength, and
  feeling.
- It speaks a short local prompt when one is low.
- It throttles prompts globally to 20 minutes and same-need prompts to 60
  minutes.
- These proactive prompts do not call the LLM; they are local and cheap.

Current touch speech behavior:

- The plugin subscribes to `MW.Main.Event_TouchHead` and
  `MW.Main.Event_TouchBody`.
- A local short line is shown with `MW.Main.SayRnd(...)`.
- The same line is passed to `QueueSpeak(...)`, so it uses the configured TTS
  provider when TTS is enabled.
- Touch speech is throttled to once per 8 seconds.
- It does not call the LLM, so touch interactions do not spend API tokens.

## Extra Money Jobs

The plugin appends several runtime work entries to
`MW.Core.Graph.GraphConfig.Works`:

- `LLM写代码接单`
- `LLM修Bug外包`
- `LLM整理需求文档`
- `LLM户外传单兼职`
- `LLM户外灵感采风`

All are `GraphHelper.Work.WorkType.Work`, so they earn money rather than EXP.
They use VPet's normal `Main.FunctionSpend` formula through `MoneyBase`,
duration, hunger/thirst consumption, and mood consumption; the plugin does not
directly add money.

Income multiplier:

- `LLMChatSettings.LlmWorkMoneyMultiplier` defaults to `2.0`.
- The settings window exposes it as `工作收益系数`, clamped to `0.1..10.0`.
- Saving settings calls `AddLLMWorkDefinitions()` again, which updates existing
  added work entries in place, so changing the coefficient does not require a
  game restart.

Animation behavior:

- Indoor jobs resolve `MW.Core.Graph.FindName(GraphInfo.GraphType.Work)` first,
  so they use the current pet's available work/coding-style animation where the
  skin provides it.
- Outdoor jobs resolve `MW.Core.Graph.FindName(GraphInfo.GraphType.Sleep)` and
  intentionally use the sleep animation as requested.
- If a graph name is missing, the plugin falls back to the first existing work
  graph or `"work"`.

Duplicate prevention:

- Extra work entries are added once per plugin instance.
- Existing work names are checked before inserting so repeated lifecycle hooks
  do not duplicate the list.

MiniMax / compatible TTS notes from testing:

- Native MiniMax provider uses `POST /v1/t2a_v2` and decodes hex audio.
- Some gateways expose MiniMax speech through OpenAI-compatible
  `POST /v1/audio/speech`.
- For DMXAPI-style speech, `output_format` may only accept `hex` or `url`; this
  project has needed compatible handling rather than assuming normal OpenAI mp3
  bytes.
- Use a MiniMax-capable key for MiniMax speech. A chat gateway key may not have
  access to speech models.

## Model Action Protocol

When `enableModelActions` is on, the model may return:

```json
{
  "reply": "natural text shown to the user",
  "actions": [
    { "name": "action_name", "args": { "key": "value" } }
  ]
}
```

Only whitelisted actions execute. Current actions:

- `open_chat`
- `open_llm_settings`
- `open_game_settings`
- `open_gallery`
- `show_panel`
- `reset_position`
- `move_pet`
- `start_work`
- `start_study`
- `start_play`
- `pick_activity`
- `clear_pending_activity`
- `stop_work`
- `open_better_buy`
- `pick_wanted_item`
- `clear_wanted_item`
- `buy_and_use`
- `buy_food` alias for `buy_and_use`
- `feed_by_name`
- `read_status`
- `set_zoom`
- `play_tts`

The model prompt must keep purchase actions separate from shop browsing:

- `open_better_buy` is only for explicit browsing/opening the shop UI.
- `buy_and_use` must purchase in the background without opening Better Buy.
- `pick_wanted_item` lets the plugin randomly choose from the actual item list;
  the model must not invent the random item itself.

## Better Buy Behavior

The plugin uses VPet's actual `MW.Foods` collection as the source of truth.

Prompt state includes the full current item list, grouped by type:

- `Food`
- `Meal`
- `Snack`
- `Drink`
- `Functional`
- `Drug`
- `Gift`
- `Star`

`FindFoodByName(..., allowAllBetterBuyTypes: true)` searches all Better Buy item
types by translated name, internal name, and description. This is why purchases
can include more than ordinary food/drinks.

`buy_and_use` does not open Better Buy. It:

1. Resolves the requested item from the full item list.
2. Checks function mode and overload restrictions.
3. Checks money for expensive/high-exp items.
4. Charges `food.Price * count`.
5. Calls `MW.TakeItem(food)` for each item.
6. Calls `MW.TakeItemHandle(food, count, "betterbuy")`.
7. Plays `MW.DisplayFoodAnimation(food.GetGraph(), food.ImageSource)`.

`count` is clamped to `1..10`.

## Wanted Item Flow

The pet can be asked what it wants:

- User: "你想要什么？"
- Model action: `pick_wanted_item`
- Plugin behavior: randomly select one item from all `MW.Foods`, then show a
  bubble like `我想要「商品名」（分类，价格 金钱）！主人要给我买吗？`

Pending wanted item is kept in memory:

- User confirms: `buy_and_use` buys the pending item directly.
- User refuses: `clear_wanted_item` clears it without spending.
- User asks to switch: `pick_wanted_item` picks another item.

This is intentionally local-random inside the plugin so the model cannot pretend
to know inventory or pick unavailable goods.

## Activity Suggestion Flow

The pet can be asked what it wants to do:

- User: "你想做什么？"
- Model action: `pick_activity`
- Plugin behavior: select a real activity from `MW.Main.WorkList(...)`, filtered
  by level and current state, then ask the player for confirmation.

Pending activity is kept in memory:

- User confirms: model should call the matching `start_work`, `start_study`, or
  `start_play`; the plugin can use the pending activity even if `args.name` is
  omitted.
- User refuses: `clear_pending_activity` clears it without starting anything.
- User asks to switch: `pick_activity` picks another suitable activity.

The plugin may also occasionally suggest an activity locally when status is good
and no work/study/play is currently running. This suggestion is throttled to once
per 60 minutes and never starts automatically.

## Shopping Context

The last bought item is remembered separately from the pending wanted item.

Examples:

- `再买咖啡`: buy coffee.
- `还有蛋糕`: buy cake after a previous purchase without resetting context.
- `再来一瓶`: if no new item is named, repeat the last bought item.
- One user message with multiple goods should produce multiple `buy_and_use`
  actions, one per good.

Generic shop words such as `商店`, `商城`, `更好买`, `shop`, and `store` are not
treated as product names.

## Work Interface Research

Checked upstream source plus the local NuGet package used by this project:

- `.nuget-packages/vpet-simulator.core/1.1.0.66/lib/net8.0/VPet-Simulator.Core.xml`
- `.nuget-packages/vpet-simulator.windows.interface/1.1.0.66/lib/net8.0/VPet-Simulator.Windows.Interface.xml`
- `https://github.com/LorisYounger/VPet/blob/main/VPet-Simulator.Core/Display/MainLogic.cs`
- `https://github.com/LorisYounger/VPet/blob/main/VPet-Simulator.Core/Graph/GraphHelper.cs`
- `https://github.com/LorisYounger/VPet/blob/main/VPet-Simulator.Windows.Interface/ScheduleTask.cs`
- `https://github.com/LorisYounger/VPet/blob/main/VPet-Simulator.Core/Display/WorkTimer.xaml.cs`

Confirmed direct work APIs:

- `VPet_Simulator.Core.Main.WorkList(out List<GraphHelper.Work> ws, out List<GraphHelper.Work> ss, out List<GraphHelper.Work> ps)`
  - Iterates `Core.Graph.GraphConfig.Works` and splits entries by
    `GraphHelper.Work.WorkType.Work`, `Study`, and `Play`.
- `VPet_Simulator.Core.Main.StartWork(GraphHelper.Work work)`
  - Starts a work/study item.
  - Returns `true` only when it actually starts a new item.
  - If function mode is disabled, upstream skips sick/level checks; this plugin
    intentionally blocks model-started work when `Core.Controller.EnableFunction`
    is false.
  - If the pet is ill, upstream shows a MessageBox and returns `false`; this
    plugin pre-checks and uses `SayRnd` instead.
  - If the pet level is below `work.LevelLimit`, upstream shows a MessageBox and
    returns `false`; this plugin pre-checks and uses `SayRnd` instead.
  - If `State == Main.WorkingState.Work` and `NowWork.Name == work.Name`,
    upstream treats another `StartWork` call as a manual stop via
    `WorkTimer.Stop(... MenualStop)` and returns `false`; this plugin avoids
    accidental toggles by saying it is already running.
  - Otherwise upstream checks optional `WorkCheck`, calls `WorkTimer.Start(work)`,
    and returns `true`.
- `VPet_Simulator.Core.GraphCore.Config.Works`
  - Stores all work/study/play definitions loaded with the pet graph.
- `GraphHelper.Work`
  - Public fields/properties include `Type`, `Name`, `NameTrans`, `Graph`,
    `MoneyBase`, `StrengthFood`, `StrengthDrink`, `Feeling`, `LevelLimit`,
    `Time`, and `FinishBonus`.
  - `WorkType` enum values are exactly `Work`, `Study`, and `Play`.
- `WorkTimer.Start(Work work)`
  - Sets `Main.State = WorkingState.Work`, `Main.NowWork = work`, resets timer
    counters, applies style, displays the work animation, invokes work-start
    event, and updates the work UI.
- `WorkTimer.Stop(Action then = null, StopReason reason = MenualStop)`
  - Emits `E_FinishWork` if a work is active, hides the timer, sets state back to
    normal, and plays the work end animation.

Current plugin implementation:

1. Prompt state lists available work/study/play names from `MW.Main.WorkList(...)`
   with level limits and durations.
2. `start_work`, `start_study`, and `start_play` resolve by `Name` or
   `NameTrans`.
3. The plugin pre-checks function mode, illness, and level before calling
   `MW.Main.StartWork(work)`.
4. If another item is running, the plugin calls `WorkTimer.Stop(... MenualStop)`
   and starts the requested item in the stop callback.
5. `stop_work` calls `WorkTimer.Stop(... MenualStop)` when a work/study/play item
   is active.

Schedule APIs are real but should be treated as a separate feature:

- `IMainWindow.ScheduleTask`
- `ScheduleTask.Start()`, `StartWork()`, `Stop()`
- `ScheduleTask.AddWork/AddStudy/AddPlay/AddRest`

`ScheduleTask.StartWork()` is not the same as direct `Main.StartWork(work)`: it
walks the saved schedule, auto-renews packages, requires active work/study
packages for work and study entries, calls `mw.Main.StartWork(wsi.Work.Double(...))`,
and advances/rests based on `WorkTimer.E_FinishWork`. Do not mutate schedules or
buy/renew packages from the model without a separate confirmation flow, because
that can spend money and change long-running behavior.

## Safety Principles

- Keep all game mutations on the WPF dispatcher.
- Never execute model-provided arbitrary code or arbitrary method names.
- Clamp numeric model arguments.
- Avoid spending money unless the user explicitly asks or confirms.
- Keep UI-opening actions separate from background actions.
- Avoid relying on model knowledge for item/work availability; use game-loaded
  collections as the source of truth.

## Known Follow-Ups

- Add a visible history/log panel for executed actions.
- Add optional per-action confirmation settings for purchases and future work.
- Consider trimming the full item prompt if token cost gets too high, while
  still keeping runtime item resolution full.

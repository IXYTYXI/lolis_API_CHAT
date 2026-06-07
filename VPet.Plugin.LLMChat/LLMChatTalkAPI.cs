using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.LLMChat;

public sealed class LLMChatTalkAPI : TalkBox
{
    private static readonly string[] LikabilityLabels = { "陌生", "普通", "喜欢", "爱" };
    private const string ActionPrompt = """
你可以选择让游戏执行最多 5 个安全动作。普通聊天不需要动作时，直接自然回复。
如果需要动作，请只返回一个 JSON 对象，不要使用 Markdown：
{
  "reply": "给用户看的自然回复",
  "actions": [
    { "name": "open_chat" },
    { "name": "open_llm_settings" },
    { "name": "open_game_settings" },
    { "name": "open_gallery" },
    { "name": "show_panel" },
    { "name": "reset_position" },
    { "name": "move_pet", "args": { "direction": "left", "distance": "120" } },
    { "name": "start_work", "args": { "name": "工作名称" } },
    { "name": "start_study", "args": { "name": "学习名称" } },
    { "name": "start_play", "args": { "name": "娱乐名称" } },
    { "name": "stop_work" },
    { "name": "open_better_buy", "args": { "name": "要购买的物品名称" } },
    { "name": "pick_wanted_item" },
    { "name": "clear_wanted_item" },
    { "name": "buy_and_use", "args": { "name": "要购买并使用的物品名称", "count": "1" } },
    { "name": "feed_by_name", "args": { "name": "食物或饮料名称" } },
    { "name": "read_status" },
    { "name": "set_zoom", "args": { "level": "1.0" } },
    { "name": "play_tts", "args": { "text": "额外播放的短语音" } }
  ]
}
只允许使用上面列出的动作；用户要求开始工作/学习/娱乐时，从[可执行工作/学习/娱乐]选择名称并分别使用 start_work/start_study/start_play；用户要求停止当前工作/学习/娱乐时使用 stop_work；如果[当前工作上下文]显示正在进行且用户要求换新项目，直接输出对应 start_*，插件会先停止当前项再切换；用户说“打开商店/打开更好买/逛商店”时，使用 open_better_buy 且不要把“商店/更好买”当商品名；用户问“你想要什么/想买什么/想吃什么/想喝什么/挑一个/选一个”时，使用 pick_wanted_item，让游戏从全量更好买商品里随机挑选，模型不要自己挑商品；有[想要商品上下文]时，用户说“买/可以/好/给你买/就这个”只用 buy_and_use 购买上下文里的商品，不要先打开商店，用户说“不买/算了/不要”用 clear_wanted_item，用户说“换一个/重新选/再想一个”用 pick_wanted_item；用户说“买/购买/买来喝/买来吃”时优先使用 buy_and_use，不要用 feed_by_name，也不要额外调用 open_better_buy；商品名优先从[更好买商品名称]里选择，如果用户明确说出商品名但你不确定分类，仍用 buy_and_use 并传用户原文；用户连续说“再买/还有/然后买/继续买”时，保持更好买购物上下文并继续使用 buy_and_use；用户说多瓶/多个/连续买同一商品时，在 buy_and_use.args.count 写购买数量；用户一次说多个不同商品时，为每个商品分别输出一个 buy_and_use 动作；只想浏览商店时才用 open_better_buy；不要猜不存在的食物名称或工作名称；需要动作时，回复正文必须写在 reply 里，不要在 JSON 前后输出任何字符；不需要动作时不要输出 JSON。
""";

    private readonly LLMChatPlugin _plugin;

    public LLMChatTalkAPI(LLMChatPlugin plugin) : base(plugin)
    {
        _plugin = plugin;
    }

    public override string APIName => "LLM Chat";

    public override void Responded(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        DisplayThink();
        Dispatcher.Invoke(() => IsEnabled = false);

        _ = Task.Run(async () =>
        {
            try
            {
                var prompt = BuildPrompt(content);
                var reply = await _plugin.ChatClient.AskAsync(prompt).ConfigureAwait(false);
                var actionPlan = _plugin.Settings.EnableModelActions
                    ? LLMChatActionParser.Parse(reply.Content)
                    : new LLMChatActionPlan(reply.Content, Array.Empty<LLMChatAction>());
                var desc = _plugin.Settings.ShowTokenUsage && reply.TotalTokens is > 0
                    ? $"当前 Token 使用: {reply.TotalTokens}"
                    : null;

                Dispatcher.Invoke(() => DisplayThinkToSayRnd(actionPlan.Reply, desc: desc));
                _plugin.QueueSpeak(actionPlan.Reply);
                _plugin.ExecuteModelActions(actionPlan.Actions);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    DisplayThinkToSayRnd("API调用失败, 请检查设置和网络连接\n" + CreateExceptionSummary(ex)));
            }
            finally
            {
                Dispatcher.Invoke(() => IsEnabled = true);
            }
        });
    }

    public override void Setting() => _plugin.Setting();

    private string BuildPrompt(string userText)
    {
        var state = _plugin.BuildModelStateSummary(GetLikabilityLabel);

        return _plugin.Settings.EnableModelActions
            ? $"{state}\n{ActionPrompt}\n用户输入：{userText}"
            : $"{state}\n{userText}";
    }

    private static string GetLikabilityLabel(int likability)
    {
        return LikabilityLabels[GetLikabilityLevel(likability)];
    }

    private static int GetLikabilityLevel(int likability)
    {
        if (likability <= 50)
        {
            return 0;
        }

        if (likability < 100)
        {
            return 1;
        }

        return likability < 200 ? 2 : 3;
    }

    private static string CreateExceptionSummary(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current != null; current = current.InnerException)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
        }

        var text = string.Join("\n", parts);
        return text.Length > 900 ? text[..900] + "..." : text;
    }
}

using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.LLMChat;

public sealed class LLMChatTalkAPI : TalkBox
{
    private static readonly string[] LikabilityLabels = { "陌生", "普通", "喜欢", "爱" };
    private const string ActionPrompt = """
你可以选择让游戏执行一个安全动作。普通聊天不需要动作时，直接自然回复。
如果需要动作，请只返回一个 JSON 对象，不要使用 Markdown：
{
  "reply": "给用户看的自然回复",
  "actions": [
    { "name": "open_chat" },
    { "name": "open_llm_settings" },
    { "name": "open_game_settings" },
    { "name": "open_gallery" },
    { "name": "set_zoom", "args": { "level": "1.0" } },
    { "name": "play_tts", "args": { "text": "额外播放的短语音" } }
  ]
}
只允许使用上面列出的动作；不需要动作时不要输出 JSON。
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
        var save = _plugin.MW.Core.Save;
        var likability = (int)save.Likability;
        var likeText = LikabilityLabels[GetLikabilityLevel(likability)];
        var state = $"[当前状态: {save.Mode}, 好感度: {likeText}({likability})]";

        return _plugin.Settings.EnableModelActions
            ? $"{state}\n{ActionPrompt}\n用户输入：{userText}"
            : $"{state}\n{userText}";
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

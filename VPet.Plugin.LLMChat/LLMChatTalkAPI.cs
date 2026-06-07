using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.LLMChat;

public sealed class LLMChatTalkAPI : TalkBox
{
    private static readonly string[] LikabilityLabels = { "陌生", "普通", "喜欢", "爱" };
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
                var desc = _plugin.Settings.ShowTokenUsage && reply.TotalTokens is > 0
                    ? $"当前Token使用: {reply.TotalTokens}"
                    : null;

                Dispatcher.Invoke(() => DisplayThinkToSayRnd(reply.Content, desc: desc));
                _plugin.QueueSpeak(reply.Content);
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

        return $"[当前状态: {save.Mode}, 好感度: {likeText}({likability})]\n{userText}";
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

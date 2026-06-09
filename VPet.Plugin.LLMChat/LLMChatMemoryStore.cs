using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VPet.Plugin.LLMChat;

public sealed class LLMChatMemoryStore
{
    private const int MaxShortEvents = 40;
    private const int PromptShortEvents = 18;
    private const int MaxLongMemoryPromptLength = 6000;
    private readonly object _sync = new();
    private readonly string _shortMemoryPath;
    private readonly string _longMemoryPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LLMChatMemoryStore(string shortMemoryPath, string longMemoryPath)
    {
        _shortMemoryPath = shortMemoryPath;
        _longMemoryPath = longMemoryPath;
    }

    public void EnsureFiles()
    {
        lock (_sync)
        {
            EnsureDirectory(_shortMemoryPath);
            EnsureDirectory(_longMemoryPath);

            if (!File.Exists(_shortMemoryPath))
            {
                SaveShortMemory(new LolisShortMemory());
            }

            if (!File.Exists(_longMemoryPath))
            {
                File.WriteAllText(_longMemoryPath, DefaultLongMemory);
            }
        }
    }

    public void RecordShortEvent(string kind, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                EnsureFiles();
                var memory = LoadShortMemory();
                memory.RecentEvents.Add(new LolisMemoryEvent
                {
                    Time = DateTime.Now,
                    Kind = string.IsNullOrWhiteSpace(kind) ? "event" : kind.Trim(),
                    Text = LimitSingleLine(text, 300)
                });

                while (memory.RecentEvents.Count > MaxShortEvents)
                {
                    memory.RecentEvents.RemoveAt(0);
                }

                SaveShortMemory(memory);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to record short memory: {ex}");
            }
        }
    }

    public void AppendLongMemory(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                EnsureFiles();
                var line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"- {DateTime.Now:yyyy-MM-dd HH:mm}: {LimitSingleLine(text, 500)}");
                File.AppendAllText(_longMemoryPath, Environment.NewLine + line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to append long memory: {ex}");
            }
        }
    }

    public string BuildPrompt()
    {
        lock (_sync)
        {
            try
            {
                EnsureFiles();
                var builder = new StringBuilder();
                var longMemory = File.ReadAllText(_longMemoryPath).Trim();
                if (!string.IsNullOrWhiteSpace(longMemory))
                {
                    if (longMemory.Length > MaxLongMemoryPromptLength)
                    {
                        longMemory = longMemory[^MaxLongMemoryPromptLength..];
                        longMemory = "...\n" + longMemory;
                    }

                    builder.AppendLine("[长期记忆]");
                    builder.AppendLine("以下内容来自 LolisLongMemory.md，记录主人偏好、重要事实和长期关系设定：");
                    builder.AppendLine(longMemory);
                }

                var shortMemory = LoadShortMemory();
                var events = shortMemory.RecentEvents.TakeLast(PromptShortEvents).ToArray();
                if (events.Length > 0)
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.AppendLine("[短期记忆]");
                    builder.AppendLine("以下是最近发生的对话和动作，只用于理解当前上下文：");
                    foreach (var item in events)
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"- {item.Time:HH:mm} {item.Kind}: ");
                        builder.AppendLine(item.Text);
                    }
                }

                return builder.ToString().Trim();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to build memory prompt: {ex}");
                return string.Empty;
            }
        }
    }

    private LolisShortMemory LoadShortMemory()
    {
        if (!File.Exists(_shortMemoryPath))
        {
            return new LolisShortMemory();
        }

        var json = File.ReadAllText(_shortMemoryPath);
        return JsonSerializer.Deserialize<LolisShortMemory>(json, JsonOptions) ?? new LolisShortMemory();
    }

    private void SaveShortMemory(LolisShortMemory memory)
    {
        File.WriteAllText(_shortMemoryPath, JsonSerializer.Serialize(memory, JsonOptions));
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string LimitSingleLine(string text, int maxLength)
    {
        var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private const string DefaultLongMemory = """
# 萝莉斯长期记忆

这里记录主人明确要求记住的长期信息，例如偏好、重要事实、常买商品、关系设定和重要事件。

写入规则：

- 只有当主人明确说“记住……”或表达需要长期保留时，才追加内容。
- 不要把普通闲聊都写进长期记忆。
- 不要记录敏感隐私，除非主人明确要求。
""";

    private sealed class LolisShortMemory
    {
        public List<LolisMemoryEvent> RecentEvents { get; set; } = new();
    }

    private sealed class LolisMemoryEvent
    {
        public DateTime Time { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;
    }
}

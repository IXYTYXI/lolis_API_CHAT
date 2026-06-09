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
    private const int MaxDiaryPromptLength = 3500;
    private readonly object _sync = new();
    private readonly string _shortMemoryPath;
    private readonly string _longMemoryPath;
    private readonly string _preferencesPath;
    private readonly string _diaryPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LLMChatMemoryStore(
        string shortMemoryPath,
        string longMemoryPath,
        string preferencesPath,
        string diaryPath)
    {
        _shortMemoryPath = shortMemoryPath;
        _longMemoryPath = longMemoryPath;
        _preferencesPath = preferencesPath;
        _diaryPath = diaryPath;
    }

    public void EnsureFiles()
    {
        lock (_sync)
        {
            EnsureDirectory(_shortMemoryPath);
            EnsureDirectory(_longMemoryPath);
            EnsureDirectory(_preferencesPath);
            EnsureDirectory(_diaryPath);

            if (!File.Exists(_shortMemoryPath))
            {
                SaveShortMemory(new LolisShortMemory());
            }

            if (!File.Exists(_longMemoryPath))
            {
                File.WriteAllText(_longMemoryPath, DefaultLongMemory);
            }

            if (!File.Exists(_preferencesPath))
            {
                SavePreferences(new LolisPreferences());
            }

            if (!File.Exists(_diaryPath))
            {
                File.WriteAllText(_diaryPath, DefaultDiary);
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

    public void RecordPreference(string category, string name, int increment = 1)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                EnsureFiles();
                var preferences = LoadPreferences();
                var key = category.Trim();
                if (!preferences.Counts.TryGetValue(key, out var values))
                {
                    values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    preferences.Counts[key] = values;
                }

                var item = LimitSingleLine(name, 80);
                values.TryGetValue(item, out var existingCount);
                values[item] = existingCount + Math.Max(1, increment);
                SavePreferences(preferences);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to record preference: {ex}");
            }
        }
    }

    public void WriteDailyDiaryIfNeeded(Func<string> diaryFactory)
    {
        lock (_sync)
        {
            try
            {
                EnsureFiles();
                var memory = LoadShortMemory();
                var today = DateTime.Now.Date;
                if (memory.LastDiaryDate.Date == today)
                {
                    return;
                }

                var text = diaryFactory().Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = BuildFallbackDiaryText(memory);
                }

                File.AppendAllText(
                    _diaryPath,
                    string.Create(CultureInfo.InvariantCulture, $"\n## {today:yyyy-MM-dd}\n\n{text}\n"));
                memory.LastDiaryDate = today;
                SaveShortMemory(memory);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to write diary: {ex}");
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

                var preferences = BuildPreferencesPrompt();
                if (!string.IsNullOrWhiteSpace(preferences))
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.AppendLine(preferences);
                }

                var diary = BuildDiaryPrompt();
                if (!string.IsNullOrWhiteSpace(diary))
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.AppendLine(diary);
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

    private LolisPreferences LoadPreferences()
    {
        if (!File.Exists(_preferencesPath))
        {
            return new LolisPreferences();
        }

        var json = File.ReadAllText(_preferencesPath);
        return JsonSerializer.Deserialize<LolisPreferences>(json, JsonOptions) ?? new LolisPreferences();
    }

    private void SavePreferences(LolisPreferences preferences)
    {
        File.WriteAllText(_preferencesPath, JsonSerializer.Serialize(preferences, JsonOptions));
    }

    private void SaveShortMemory(LolisShortMemory memory)
    {
        File.WriteAllText(_shortMemoryPath, JsonSerializer.Serialize(memory, JsonOptions));
    }

    private string BuildPreferencesPrompt()
    {
        var preferences = LoadPreferences();
        if (preferences.Counts.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("[偏好统计]");
        builder.AppendLine("以下是萝莉斯从互动中形成的偏好计数，次数越高表示越常发生，不等于绝对喜欢：");
        foreach (var category in preferences.Counts.OrderBy(pair => pair.Key))
        {
            var top = category.Value
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Take(8)
                .Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}({pair.Value})"));
            builder.Append(category.Key);
            builder.Append(": ");
            builder.AppendLine(string.Join("、", top));
        }

        return builder.ToString().Trim();
    }

    private string BuildDiaryPrompt()
    {
        if (!File.Exists(_diaryPath))
        {
            return string.Empty;
        }

        var text = File.ReadAllText(_diaryPath).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (text.Length > MaxDiaryPromptLength)
        {
            text = "...\n" + text[^MaxDiaryPromptLength..];
        }

        return "[日记]\n以下是萝莉斯最近的生活记录：\n" + text;
    }

    private static string BuildFallbackDiaryText(LolisShortMemory memory)
    {
        var recent = memory.RecentEvents.TakeLast(8).Select(item => item.Text).ToArray();
        if (recent.Length == 0)
        {
            return "今天安静地陪在主人身边。";
        }

        return "今天发生了这些事：" + string.Join("；", recent) + "。";
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

    private const string DefaultDiary = """
# 萝莉斯日记

这里会自动记录每天发生过的简短生活片段。
""";

    private sealed class LolisShortMemory
    {
        public List<LolisMemoryEvent> RecentEvents { get; set; } = new();

        public DateTime LastDiaryDate { get; set; } = DateTime.MinValue;
    }

    private sealed class LolisMemoryEvent
    {
        public DateTime Time { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;
    }

    private sealed class LolisPreferences
    {
        public Dictionary<string, Dictionary<string, int>> Counts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

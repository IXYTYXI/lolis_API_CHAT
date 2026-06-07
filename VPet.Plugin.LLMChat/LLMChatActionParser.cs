using System.Text.Json;
using System.Text.RegularExpressions;

namespace VPet.Plugin.LLMChat;

public sealed record LLMChatAction(string Name, IReadOnlyDictionary<string, string> Args);

public sealed record LLMChatActionPlan(string Reply, IReadOnlyList<LLMChatAction> Actions);

public static class LLMChatActionParser
{
    private static readonly Regex FencedJsonRegex = new(
        @"```(?:json)?\s*(\{.*?\})\s*```",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static LLMChatActionPlan Parse(string content)
    {
        if (TryParseJson(content, out var plan))
        {
            return plan;
        }

        foreach (Match match in FencedJsonRegex.Matches(content))
        {
            if (TryParseJson(match.Groups[1].Value, out plan))
            {
                return plan;
            }
        }

        foreach (var candidate in EnumerateJsonObjects(content))
        {
            if (TryParseJson(candidate, out plan))
            {
                return plan;
            }
        }

        return new LLMChatActionPlan(content.Trim(), Array.Empty<LLMChatAction>());
    }

    private static IEnumerable<string> EnumerateJsonObjects(string content)
    {
        for (var start = content.IndexOf('{'); start >= 0; start = content.IndexOf('{', start + 1))
        {
            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var index = start; index < content.Length; index++)
            {
                var current = content[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == '{')
                {
                    depth++;
                    continue;
                }

                if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return content[start..(index + 1)];
                        break;
                    }
                }
            }
        }
    }

    private static bool TryParseJson(string json, out LLMChatActionPlan plan)
    {
        plan = new LLMChatActionPlan(string.Empty, Array.Empty<LLMChatAction>());

        var trimmed = json.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var reply = ReadString(root, "reply")
                ?? ReadString(root, "message")
                ?? ReadString(root, "content")
                ?? ReadString(root, "text");
            if (string.IsNullOrWhiteSpace(reply))
            {
                return false;
            }

            var actions = new List<LLMChatAction>();
            if (TryGetProperty(root, "actions", out var actionsElement)
                && actionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in actionsElement.EnumerateArray())
                {
                    if (TryReadAction(item, out var action))
                    {
                        actions.Add(action);
                    }
                }
            }

            if (TryGetProperty(root, "action", out var actionElement)
                && TryReadAction(actionElement, out var singleAction))
            {
                actions.Add(singleAction);
            }

            plan = new LLMChatActionPlan(reply.Trim(), actions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadAction(JsonElement element, out LLMChatAction action)
    {
        action = new LLMChatAction(string.Empty, new Dictionary<string, string>());
        if (element.ValueKind == JsonValueKind.String)
        {
            var name = element.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            action = new LLMChatAction(name.Trim(), new Dictionary<string, string>());
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var actionName = ReadString(element, "name")
            ?? ReadString(element, "type")
            ?? ReadString(element, "action");
        if (string.IsNullOrWhiteSpace(actionName))
        {
            return false;
        }

        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (TryGetProperty(element, "args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Object)
        {
            AddProperties(args, argsElement);
        }
        else if (TryGetProperty(element, "arguments", out var argumentsElement)
                 && argumentsElement.ValueKind == JsonValueKind.Object)
        {
            AddProperties(args, argumentsElement);
        }
        else if (TryGetProperty(element, "params", out var paramsElement) && paramsElement.ValueKind == JsonValueKind.Object)
        {
            AddProperties(args, paramsElement);
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("name")
                || property.NameEquals("type")
                || property.NameEquals("action")
                || property.NameEquals("args")
                || property.NameEquals("arguments")
                || property.NameEquals("params"))
            {
                continue;
            }

            args[property.Name] = ValueToString(property.Value);
        }

        action = new LLMChatAction(actionName.Trim(), args);
        return true;
    }

    private static void AddProperties(Dictionary<string, string> args, JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            args[property.Name] = ValueToString(property.Value);
        }
    }

    private static string ValueToString(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName)
                || property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

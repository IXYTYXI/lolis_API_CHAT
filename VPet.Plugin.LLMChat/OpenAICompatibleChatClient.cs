using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VPet.Plugin.LLMChat;

public sealed class OpenAICompatibleChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly LLMChatSettings _settings;
    private readonly List<ChatMessage> _history = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public OpenAICompatibleChatClient(LLMChatSettings settings)
    {
        _settings = settings;
    }

    public async Task<ChatReply> AskAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messages = BuildMessages(userMessage);
            var request = new ChatCompletionRequest(
                _settings.Model,
                messages,
                _settings.Temperature,
                _settings.MaxTokens);

            using var httpClient = CreateHttpClient();
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri())
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(request, JsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };

            var apiKey = _settings.GetApiKey();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"发送请求失败: {BuildChatCompletionsUri()}\n{CreateExceptionSummary(ex)}",
                    ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"请求超时: {BuildChatCompletionsUri()}\n当前超时秒数: {_settings.TimeoutSeconds}",
                    ex);
            }

            using (response)
            {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"HTTP {(int)response.StatusCode}: {CreateResponsePreview(responseText)}");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!LooksLikeJson(responseText, contentType))
            {
                throw new InvalidOperationException(
                    "API 返回的不是 JSON。请检查 Base URL 是否是 OpenAI-compatible API 地址，"
                    + $"当前实际请求地址: {BuildChatCompletionsUri()}\n"
                    + $"返回内容: {CreateResponsePreview(responseText)}");
            }

            ChatCompletionResponse completion;
            try
            {
                completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseText, JsonOptions)
                    ?? throw new InvalidOperationException("API returned an empty response.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    "API 返回 JSON 解析失败。请确认该模型走 /chat/completions 兼容接口。\n"
                    + $"解析错误: {ex.Message}\n"
                    + $"返回内容: {CreateResponsePreview(responseText)}",
                    ex);
            }

            var choice = completion.Choices.FirstOrDefault()
                ?? throw new InvalidOperationException("API returned no choices.");

            var content = CleanAssistantContent(choice.Message?.Content);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("API returned an empty message.");
            }

            _history.Add(new ChatMessage("user", userMessage));
            _history.Add(new ChatMessage("assistant", content));
            TrimHistory();

            if (choice.FinishReason == "length")
            {
                content += " ...";
            }

            return new ChatReply(content, completion.Usage?.TotalTokens);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(_settings.ProxyUrl))
        {
            handler.Proxy = new WebProxy(_settings.ProxyUrl);
            handler.UseProxy = true;
        }

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, _settings.TimeoutSeconds))
        };
    }

    private Uri BuildChatCompletionsUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? "https://api.openai.com/v1"
            : _settings.BaseUrl.TrimEnd('/');

        const string suffix = "/chat/completions";
        if (baseUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^suffix.Length];
        }

        return new Uri($"{baseUrl}/chat/completions");
    }

    private static bool LooksLikeJson(string text, string contentType)
    {
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trimmed = text.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static string CreateResponsePreview(string text)
    {
        var preview = text.Trim();
        if (preview.Length > 400)
        {
            preview = preview[..400] + "...";
        }

        return preview.Replace("\r", " ").Replace("\n", " ");
    }

    private static string CreateExceptionSummary(Exception exception)
    {
        var builder = new StringBuilder();
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (builder.Length > 0)
            {
                builder.Append(" -> ");
            }

            builder.Append(current.GetType().Name).Append(": ").Append(current.Message);
        }

        return builder.ToString();
    }

    private static string? CleanAssistantContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var cleaned = Regex.Replace(
            content,
            @"<think>.*?</think>",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        cleaned = Regex.Replace(cleaned, @"</?think>", string.Empty, RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    private List<ChatMessage> BuildMessages(string userMessage)
    {
        var messages = new List<ChatMessage>
        {
            new("system", _settings.SystemPrompt)
        };

        messages.AddRange(_history);
        messages.Add(new ChatMessage("user", userMessage));
        return messages;
    }

    private void TrimHistory()
    {
        var maxMessages = Math.Max(0, _settings.KeepHistoryTurns) * 2;
        while (_history.Count > maxMessages)
        {
            _history.RemoveAt(0);
        }
    }

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        float Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record ChatCompletionResponse(
        IReadOnlyList<ChatChoice> Choices,
        ChatUsage? Usage);

    private sealed record ChatChoice(
        ChatMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record ChatUsage([property: JsonPropertyName("total_tokens")] int TotalTokens);
}

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatReply(string Content, int? TotalTokens);

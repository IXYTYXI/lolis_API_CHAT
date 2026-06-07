using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VPet.Plugin.LLMChat;

public sealed class OpenAICompatibleTextToSpeechClient : ITextToSpeechClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly LLMChatSettings _settings;
    private readonly string _cacheDirectory;

    public OpenAICompatibleTextToSpeechClient(LLMChatSettings settings, string cacheDirectory)
    {
        _settings = settings;
        _cacheDirectory = cacheDirectory;
    }

    public async Task<string> SynthesizeToFileAsync(string text, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDirectory);

        var format = NormalizeFormat(_settings.TtsResponseFormat);
        var filePath = Path.Combine(_cacheDirectory, $"{CreateCacheKey(text)}.{format}");
        if (File.Exists(filePath))
        {
            return filePath;
        }

        var request = new SpeechRequest(
            _settings.TtsModel,
            text,
            _settings.TtsVoice,
            CreateResponseFormat(format),
            string.IsNullOrWhiteSpace(_settings.TtsInstructions) ? null : _settings.TtsInstructions);

        using var httpClient = CreateHttpClient();
        var speechUri = BuildSpeechUri();
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, speechUri)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        var apiKey = _settings.GetTtsApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            ApplyAuthorizationHeader(httpRequest, apiKey);
        }

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        if (!response.IsSuccessStatusCode)
        {
            var error = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException(
                $"TTS HTTP {(int)response.StatusCode}: {CreateResponsePreview(error)}\n当前实际请求地址: {speechUri}");
        }

        if (LooksLikeJson(bytes, contentType))
        {
            bytes = ExtractAudioBytesFromJson(Encoding.UTF8.GetString(bytes));
        }

        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken).ConfigureAwait(false);
        return filePath;
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

    private Uri BuildSpeechUri()
    {
        var baseUrl = _settings.GetTtsBaseUrl().Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "https://api.openai.com/v1";
        }

        if (!string.IsNullOrWhiteSpace(_settings.TtsEndpointPath))
        {
            var endpoint = _settings.TtsEndpointPath.Trim();
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri;
            }

            endpoint = endpoint.StartsWith('/') ? endpoint : "/" + endpoint;
            return new Uri($"{baseUrl}{endpoint}");
        }

        const string suffix = "/audio/speech";
        if (baseUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^suffix.Length];
        }

        return new Uri($"{baseUrl}/audio/speech");
    }

    private void ApplyAuthorizationHeader(HttpRequestMessage request, string apiKey)
    {
        var scheme = _settings.TtsAuthorizationScheme.Trim();
        if (string.IsNullOrWhiteSpace(scheme))
        {
            request.Headers.TryAddWithoutValidation("Authorization", apiKey);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, apiKey);
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

    private static bool LooksLikeJson(byte[] bytes, string contentType)
    {
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var index = 0;
        while (index < bytes.Length && char.IsWhiteSpace((char)bytes[index]))
        {
            index++;
        }

        return index < bytes.Length && (bytes[index] == (byte)'{' || bytes[index] == (byte)'[');
    }

    private static byte[] ExtractAudioBytesFromJson(string json)
    {
        SpeechJsonResponse result;
        try
        {
            result = JsonSerializer.Deserialize<SpeechJsonResponse>(json, JsonOptions)
                ?? throw new InvalidOperationException("TTS returned an empty JSON response.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "TTS 返回 JSON 解析失败。\n"
                + $"解析错误: {ex.Message}\n"
                + $"返回内容: {CreateResponsePreview(json)}",
                ex);
        }

        if (result.BaseResp?.StatusCode is int statusCode && statusCode != 0)
        {
            var message = result.BaseResp.StatusMsg ?? "unknown error";
            throw new InvalidOperationException($"TTS failed: {statusCode} {message}");
        }

        var audioHex = result.Data?.Audio;
        if (string.IsNullOrWhiteSpace(audioHex))
        {
            throw new InvalidOperationException(
                "TTS 返回 JSON 中没有音频数据。\n"
                + $"返回内容: {CreateResponsePreview(json)}");
        }

        try
        {
            return Convert.FromHexString(audioHex.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("TTS 返回的 audio 字段不是有效 hex 音频。", ex);
        }
    }

    private string CreateCacheKey(string text)
    {
        var material = string.Join(
            "\n",
            TextToSpeechClientFactory.OpenAICompatibleProvider,
            _settings.GetTtsBaseUrl(),
            _settings.TtsEndpointPath,
            _settings.TtsModel,
            _settings.TtsVoice,
            _settings.TtsResponseFormat,
            _settings.TtsInstructions,
            text);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static string NormalizeFormat(string value)
    {
        var format = value.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(format) ? "mp3" : format;
    }

    private static string? CreateResponseFormat(string format)
    {
        return format.Equals("mp3", StringComparison.OrdinalIgnoreCase)
            ? null
            : format;
    }

    private sealed record SpeechRequest(
        string Model,
        string Input,
        string Voice,
        [property: JsonPropertyName("response_format")] string? ResponseFormat,
        string? Instructions);

    private sealed record SpeechJsonResponse(
        SpeechJsonData? Data,
        [property: JsonPropertyName("base_resp")] SpeechJsonBaseResponse? BaseResp);

    private sealed record SpeechJsonData(string? Audio, int Status);

    private sealed record SpeechJsonBaseResponse(
        [property: JsonPropertyName("status_code")] int StatusCode,
        [property: JsonPropertyName("status_msg")] string? StatusMsg);
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VPet.Plugin.LLMChat;

public sealed class MiniMaxTextToSpeechClient : ITextToSpeechClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly LLMChatSettings _settings;
    private readonly string _cacheDirectory;

    public MiniMaxTextToSpeechClient(LLMChatSettings settings, string cacheDirectory)
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

        var request = new MiniMaxSpeechRequest(
            _settings.TtsModel,
            text,
            Stream: false,
            LanguageBoost: string.IsNullOrWhiteSpace(_settings.MiniMaxLanguageBoost)
                ? "auto"
                : _settings.MiniMaxLanguageBoost,
            OutputFormat: "hex",
            VoiceSetting: new MiniMaxVoiceSetting(
                _settings.TtsVoice,
                _settings.MiniMaxSpeed,
                _settings.MiniMaxVolume,
                _settings.MiniMaxPitch),
            AudioSetting: new MiniMaxAudioSetting(
                _settings.MiniMaxSampleRate,
                _settings.MiniMaxBitrate,
                format,
                _settings.MiniMaxChannel));

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
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("MiniMax TTS requires an API key.");
        }

        ApplyAuthorizationHeader(httpRequest, apiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"MiniMax TTS 发送请求失败: {BuildSpeechUri()}\n{CreateExceptionSummary(ex)}",
                ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"MiniMax TTS 请求超时: {BuildSpeechUri()}\n当前超时秒数: {_settings.TimeoutSeconds}",
                ex);
        }

        using (response)
        {
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"MiniMax TTS HTTP {(int)response.StatusCode}: {CreateResponsePreview(responseText)}");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!LooksLikeJson(responseText, contentType))
        {
            throw new InvalidOperationException(
                "MiniMax TTS 返回的不是 JSON。请检查 TTS Base URL 是否为 https://api.minimax.io/v1，"
                + "TTS API Key 是否为 MiniMax 平台的 API Key，而不是 OpenCode Key。\n"
                + $"当前实际请求地址: {speechUri}\n"
                + $"返回内容: {CreateResponsePreview(responseText)}");
        }

        MiniMaxSpeechResponse result;
        try
        {
            result = JsonSerializer.Deserialize<MiniMaxSpeechResponse>(responseText, JsonOptions)
                ?? throw new InvalidOperationException("MiniMax TTS returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "MiniMax TTS 返回 JSON 解析失败。\n"
                + $"解析错误: {ex.Message}\n"
                + $"返回内容: {CreateResponsePreview(responseText)}",
                ex);
        }

        if (result.BaseResp?.StatusCode is not 0)
        {
            var code = result.BaseResp?.StatusCode.ToString() ?? "unknown";
            var message = result.BaseResp?.StatusMsg ?? "unknown error";
            throw new InvalidOperationException($"MiniMax TTS failed: {code} {message}");
        }

        var audioHex = result.Data?.Audio;
        if (string.IsNullOrWhiteSpace(audioHex))
        {
            throw new InvalidOperationException("MiniMax TTS returned no audio data.");
        }

        var audioBytes = Convert.FromHexString(audioHex);
        await File.WriteAllBytesAsync(filePath, audioBytes, cancellationToken).ConfigureAwait(false);
        return filePath;
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

    private Uri BuildSpeechUri()
    {
        var baseUrl = _settings.GetTtsBaseUrl().Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "https://api.minimax.io/v1";
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

        const string suffix = "/t2a_v2";
        if (baseUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^suffix.Length];
        }

        return new Uri($"{baseUrl}/t2a_v2");
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

    private string CreateCacheKey(string text)
    {
        var material = string.Join(
            "\n",
            TextToSpeechClientFactory.MiniMaxProvider,
            _settings.GetTtsBaseUrl(),
            _settings.TtsEndpointPath,
            _settings.TtsModel,
            _settings.TtsVoice,
            _settings.TtsResponseFormat,
            _settings.MiniMaxLanguageBoost,
            _settings.MiniMaxSpeed,
            _settings.MiniMaxVolume,
            _settings.MiniMaxPitch,
            _settings.MiniMaxSampleRate,
            _settings.MiniMaxBitrate,
            _settings.MiniMaxChannel,
            text);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static string NormalizeFormat(string value)
    {
        var format = value.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(format) ? "mp3" : format;
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

    private sealed record MiniMaxSpeechRequest(
        string Model,
        string Text,
        bool Stream,
        [property: JsonPropertyName("language_boost")] string LanguageBoost,
        [property: JsonPropertyName("output_format")] string OutputFormat,
        [property: JsonPropertyName("voice_setting")] MiniMaxVoiceSetting VoiceSetting,
        [property: JsonPropertyName("audio_setting")] MiniMaxAudioSetting AudioSetting);

    private sealed record MiniMaxVoiceSetting(
        [property: JsonPropertyName("voice_id")] string VoiceId,
        float Speed,
        float Vol,
        int Pitch);

    private sealed record MiniMaxAudioSetting(
        [property: JsonPropertyName("sample_rate")] int SampleRate,
        int Bitrate,
        string Format,
        int Channel);

    private sealed record MiniMaxSpeechResponse(
        MiniMaxSpeechData? Data,
        [property: JsonPropertyName("base_resp")] MiniMaxBaseResponse? BaseResp);

    private sealed record MiniMaxSpeechData(string? Audio, int Status);

    private sealed record MiniMaxBaseResponse(
        [property: JsonPropertyName("status_code")] int StatusCode,
        [property: JsonPropertyName("status_msg")] string? StatusMsg);
}

using System.IO;
using System.Text.Json;

namespace VPet.Plugin.LLMChat;

public sealed class LLMChatSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public string Model { get; set; } = "gpt-4.1-mini";

    public string ApiKey { get; set; } = string.Empty;

    public string ApiKeyEnvironmentVariable { get; set; } = "VPET_LLM_API_KEY";

    public string SystemPrompt { get; set; } =
        "你是虚拟桌宠模拟器里的桌宠。回复要可爱、自然、简短，像正在陪伴主人聊天。";

    public float Temperature { get; set; } = 0.8f;

    public int MaxTokens { get; set; } = 500;

    public int KeepHistoryTurns { get; set; } = 12;

    public int TimeoutSeconds { get; set; } = 60;

    public string ProxyUrl { get; set; } = string.Empty;

    public bool ShowTokenUsage { get; set; } = true;

    public bool EnableModelActions { get; set; } = true;

    public float LlmWorkMoneyMultiplier { get; set; } = 2.0f;

    public bool EnableTextToSpeech { get; set; }

    public string TtsProvider { get; set; } = TextToSpeechClientFactory.OpenAICompatibleProvider;

    public string TtsBaseUrl { get; set; } = string.Empty;

    public string TtsEndpointPath { get; set; } = string.Empty;

    public string TtsModel { get; set; } = "gpt-4o-mini-tts";

    public string TtsVoice { get; set; } = "alloy";

    public string TtsResponseFormat { get; set; } = "mp3";

    public string TtsInstructions { get; set; } = "用自然、温柔、适合桌宠陪伴的语气说话。";

    public string TtsApiKey { get; set; } = string.Empty;

    public string TtsApiKeyEnvironmentVariable { get; set; } = string.Empty;

    public string TtsAuthorizationScheme { get; set; } = "Bearer";

    public string MiniMaxLanguageBoost { get; set; } = "auto";

    public float MiniMaxSpeed { get; set; } = 1.0f;

    public float MiniMaxVolume { get; set; } = 1.0f;

    public int MiniMaxPitch { get; set; }

    public int MiniMaxSampleRate { get; set; } = 32000;

    public int MiniMaxBitrate { get; set; } = 128000;

    public int MiniMaxChannel { get; set; } = 1;

    public static LLMChatSettings CreateDefault() => new();

    public LLMChatSettings Clone() => new()
    {
        BaseUrl = BaseUrl,
        Model = Model,
        ApiKey = ApiKey,
        ApiKeyEnvironmentVariable = ApiKeyEnvironmentVariable,
        SystemPrompt = SystemPrompt,
        Temperature = Temperature,
        MaxTokens = MaxTokens,
        KeepHistoryTurns = KeepHistoryTurns,
        TimeoutSeconds = TimeoutSeconds,
        ProxyUrl = ProxyUrl,
        ShowTokenUsage = ShowTokenUsage,
        EnableModelActions = EnableModelActions,
        LlmWorkMoneyMultiplier = LlmWorkMoneyMultiplier,
        EnableTextToSpeech = EnableTextToSpeech,
        TtsProvider = TtsProvider,
        TtsBaseUrl = TtsBaseUrl,
        TtsEndpointPath = TtsEndpointPath,
        TtsModel = TtsModel,
        TtsVoice = TtsVoice,
        TtsResponseFormat = TtsResponseFormat,
        TtsInstructions = TtsInstructions,
        TtsApiKey = TtsApiKey,
        TtsApiKeyEnvironmentVariable = TtsApiKeyEnvironmentVariable,
        TtsAuthorizationScheme = TtsAuthorizationScheme,
        MiniMaxLanguageBoost = MiniMaxLanguageBoost,
        MiniMaxSpeed = MiniMaxSpeed,
        MiniMaxVolume = MiniMaxVolume,
        MiniMaxPitch = MiniMaxPitch,
        MiniMaxSampleRate = MiniMaxSampleRate,
        MiniMaxBitrate = MiniMaxBitrate,
        MiniMaxChannel = MiniMaxChannel
    };

    public static LLMChatSettings LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var settings = CreateDefault();
            settings.Save(path);
            return settings;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LLMChatSettings>(json, JsonOptions) ?? CreateDefault();
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public string GetApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey;
        }

        return string.IsNullOrWhiteSpace(ApiKeyEnvironmentVariable)
            ? string.Empty
            : Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable) ?? string.Empty;
    }

    public string GetTtsBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(TtsBaseUrl))
        {
            return TtsBaseUrl;
        }

        return TtsProvider.Equals(TextToSpeechClientFactory.MiniMaxProvider, StringComparison.OrdinalIgnoreCase)
            ? "https://api.minimax.io/v1"
            : BaseUrl;
    }

    public string GetTtsApiKey()
    {
        if (!string.IsNullOrWhiteSpace(TtsApiKey))
        {
            return TtsApiKey;
        }

        if (!string.IsNullOrWhiteSpace(TtsApiKeyEnvironmentVariable))
        {
            var value = Environment.GetEnvironmentVariable(TtsApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return GetApiKey();
    }
}

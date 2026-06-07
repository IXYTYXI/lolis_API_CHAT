namespace VPet.Plugin.LLMChat;

public static class TextToSpeechClientFactory
{
    public const string OpenAICompatibleProvider = "OpenAI-compatible";
    public const string MiniMaxProvider = "MiniMax";

    public static ITextToSpeechClient Create(LLMChatSettings settings, string cacheDirectory)
    {
        return settings.TtsProvider.Equals(MiniMaxProvider, StringComparison.OrdinalIgnoreCase)
            ? new MiniMaxTextToSpeechClient(settings, cacheDirectory)
            : new OpenAICompatibleTextToSpeechClient(settings, cacheDirectory);
    }
}

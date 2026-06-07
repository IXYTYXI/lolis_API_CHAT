namespace VPet.Plugin.LLMChat;

public interface ITextToSpeechClient
{
    Task<string> SynthesizeToFileAsync(string text, CancellationToken cancellationToken = default);
}

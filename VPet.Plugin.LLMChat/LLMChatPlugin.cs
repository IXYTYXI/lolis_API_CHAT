using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.LLMChat;

public sealed class LLMChatPlugin : MainPlugin
{
    public LLMChatPlugin(IMainWindow mainwin) : base(mainwin)
    {
    }

    public LLMChatSettings Settings { get; private set; } = LLMChatSettings.CreateDefault();

    public OpenAICompatibleChatClient ChatClient { get; private set; } = null!;

    public ITextToSpeechClient TextToSpeechClient { get; private set; } = null!;

    public LLMChatTalkAPI? ChatTalkApi { get; private set; }

    public string SettingsPath { get; private set; } = string.Empty;

    public string VoiceCacheDirectory { get; private set; } = string.Empty;

    private readonly SemaphoreSlim _speechLock = new(1, 1);
    private bool _toolbarButtonsRegistered;
    private bool _modConfigMenuRegistered;

    public override string PluginName => "LLM Chat";

    public override void LoadPlugin()
    {
        SettingsPath = Path.Combine(ExtensionValue.BaseDirectory, "LLMChatSetting.json");
        VoiceCacheDirectory = Path.Combine(ExtensionValue.BaseDirectory, "voice-cache");
        Directory.CreateDirectory(VoiceCacheDirectory);

        Settings = LLMChatSettings.LoadOrCreate(SettingsPath);
        ChatClient = new OpenAICompatibleChatClient(Settings);
        TextToSpeechClient = TextToSpeechClientFactory.Create(Settings, VoiceCacheDirectory);

        ChatTalkApi = new LLMChatTalkAPI(this);
        MW.TalkAPI.Add(ChatTalkApi);

        AddModConfigMenuEntrance();
    }

    public override void LoadDIY()
    {
        AddToolbarEntrances();
        AddModConfigMenuEntrance();
    }

    public override void GameLoaded()
    {
        AddToolbarEntrances();
        AddModConfigMenuEntrance();
    }

    public override void Save()
    {
        Settings.Save(SettingsPath);
    }

    public override void Setting()
    {
        var window = new LLMChatSettingWindow(this);
        if (Application.Current?.MainWindow != null)
        {
            window.Owner = Application.Current.MainWindow;
        }

        window.ShowDialog();
    }

    internal void ApplySettings(LLMChatSettings settings)
    {
        Settings = settings;
        ChatClient = new OpenAICompatibleChatClient(Settings);
        TextToSpeechClient = TextToSpeechClientFactory.Create(Settings, VoiceCacheDirectory);
        Save();
    }

    private void AddToolbarEntrances()
    {
        if (!_toolbarButtonsRegistered)
        {
            MW.Main.ToolBar.AddMenuButton(VPet_Simulator.Core.ToolBar.MenuType.DIY, "LLM聊天", OpenChatWindow);
            MW.Main.ToolBar.AddMenuButton(VPet_Simulator.Core.ToolBar.MenuType.DIY, "LLM聊天设置", Setting);
            MW.Main.ToolBar.AddMenuButton(VPet_Simulator.Core.ToolBar.MenuType.Setting, "LLM聊天", OpenChatWindow);
            MW.Main.ToolBar.AddMenuButton(VPet_Simulator.Core.ToolBar.MenuType.Setting, "LLM聊天设置", Setting);
            _toolbarButtonsRegistered = true;
        }
    }

    private void AddModConfigMenuEntrance()
    {
        if (!_modConfigMenuRegistered)
        {
            var menuItem = new MenuItem
            {
                Header = "LLM Chat",
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            menuItem.Click += (_, _) => Setting();
            MW.Main.ToolBar.MenuMODConfig.Visibility = Visibility.Visible;
            MW.Main.ToolBar.MenuMODConfig.Items.Add(menuItem);
            _modConfigMenuRegistered = true;
        }
    }

    public void QueueSpeak(string text)
    {
        if (!Settings.EnableTextToSpeech || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await SpeakAsync(text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        });
    }

    public void OpenChatWindow()
    {
        var window = new LLMChatInputWindow(this);
        if (Application.Current?.MainWindow != null)
        {
            window.Owner = Application.Current.MainWindow;
        }

        window.ShowDialog();
    }

    public void SubmitChat(string text)
    {
        if (ChatTalkApi == null)
        {
            MessageBox.Show("LLM Chat 尚未初始化完成。", "LLM Chat", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ChatTalkApi.Responded(text);
    }

    public Task SpeakPreviewAsync(string text)
    {
        return SpeakAsync(text);
    }

    private async Task SpeakAsync(string text)
    {
        await _speechLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var audioPath = await TextToSpeechClient.SynthesizeToFileAsync(text).ConfigureAwait(false);
            Application.Current.Dispatcher.Invoke(() => MW.Main.PlayVoice(new Uri(audioPath)));
        }
        finally
        {
            _speechLock.Release();
        }
    }
}

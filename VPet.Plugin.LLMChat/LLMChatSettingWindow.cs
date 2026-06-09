using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VPet.Plugin.LLMChat;

public sealed class LLMChatSettingWindow : Window
{
    private static readonly Dictionary<string, string> ModelBaseUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-4.1-mini"] = "https://api.openai.com/v1",
        ["gpt-4o-mini"] = "https://api.openai.com/v1",
        ["deepseek-chat"] = "https://api.deepseek.com/v1",
        ["deepseek-reasoner"] = "https://api.deepseek.com/v1",
        ["qwen-plus"] = "https://dashscope.aliyuncs.com/compatible-mode/v1",
        ["doubao-seed-1-6"] = "https://ark.cn-beijing.volces.com/api/v3",
        ["llama3.1"] = "http://localhost:11434/v1"
    };

    private readonly LLMChatPlugin _plugin;
    private readonly TextBox _baseUrlBox = CreateTextBox();
    private readonly ComboBox _modelBox = new() { IsEditable = true, MinHeight = 30 };
    private readonly PasswordBox _apiKeyBox = new() { MinHeight = 30 };
    private readonly TextBox _apiKeyEnvBox = CreateTextBox();
    private readonly TextBox _systemPromptBox = CreateTextBox(multiline: true);
    private readonly TextBox _temperatureBox = CreateTextBox();
    private readonly TextBox _maxTokensBox = CreateTextBox();
    private readonly TextBox _historyTurnsBox = CreateTextBox();
    private readonly TextBox _timeoutBox = CreateTextBox();
    private readonly TextBox _proxyUrlBox = CreateTextBox();
    private readonly CheckBox _showTokenUsageBox = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _enableModelActionsBox = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _llmWorkMoneyMultiplierBox = CreateTextBox();
    private readonly CheckBox _enableTtsBox = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _ttsProviderBox = new() { IsEditable = false, MinHeight = 30 };
    private readonly TextBox _ttsBaseUrlBox = CreateTextBox();
    private readonly TextBox _ttsEndpointPathBox = CreateTextBox();
    private readonly ComboBox _ttsModelBox = new() { IsEditable = true, MinHeight = 30 };
    private readonly ComboBox _ttsVoiceBox = new() { IsEditable = true, MinHeight = 30 };
    private readonly ComboBox _ttsFormatBox = new() { IsEditable = true, MinHeight = 30 };
    private readonly PasswordBox _ttsApiKeyBox = new() { MinHeight = 30 };
    private readonly TextBox _ttsApiKeyEnvBox = CreateTextBox();
    private readonly TextBox _ttsAuthorizationSchemeBox = CreateTextBox();
    private readonly TextBox _ttsInstructionsBox = CreateTextBox(multiline: true);
    private readonly TextBox _miniMaxLanguageBoostBox = CreateTextBox();
    private readonly TextBox _miniMaxSpeedBox = CreateTextBox();
    private readonly TextBox _miniMaxVolumeBox = CreateTextBox();
    private readonly TextBox _miniMaxPitchBox = CreateTextBox();
    private readonly TextBox _miniMaxSampleRateBox = CreateTextBox();
    private readonly TextBox _miniMaxBitrateBox = CreateTextBox();
    private readonly TextBox _miniMaxChannelBox = CreateTextBox();
    private bool _isLoadingSettings;

    public LLMChatSettingWindow(LLMChatPlugin plugin)
    {
        _plugin = plugin;

        Title = "LLM Chat 设置";
        Width = 560;
        Height = 640;
        MinWidth = 480;
        MinHeight = 520;
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        Content = BuildContent();
        LoadSettings(plugin.Settings);
    }

    private DockPanel BuildContent()
    {
        foreach (var model in ModelBaseUrls.Keys)
        {
            _modelBox.Items.Add(model);
        }

        foreach (var model in new[] { "gpt-4o-mini-tts", "tts-1", "tts-1-hd" })
        {
            _ttsModelBox.Items.Add(model);
        }

        foreach (var model in new[] { "speech-2.8-turbo", "speech-2.8-hd", "speech-2.6-turbo", "speech-2.6-hd", "speech-02-turbo", "speech-02-hd" })
        {
            _ttsModelBox.Items.Add(model);
        }

        foreach (var voice in new[]
        {
            "alloy",
            "ash",
            "ballad",
            "coral",
            "echo",
            "fable",
            "nova",
            "onyx",
            "sage",
            "shimmer",
            "Chinese (Mandarin)_Cute_Spirit",
            "Chinese (Mandarin)_Warm_Girl",
            "Chinese (Mandarin)_Soft_Girl",
            "Chinese (Mandarin)_Sweet_Lady",
            "Chinese (Mandarin)_Crisp_Girl",
            "English_PlayfulGirl",
            "English_WhimsicalGirl"
        })
        {
            _ttsVoiceBox.Items.Add(voice);
        }

        foreach (var format in new[] { "mp3", "wav", "aac", "flac", "opus" })
        {
            _ttsFormatBox.Items.Add(format);
        }

        _ttsProviderBox.Items.Add(TextToSpeechClientFactory.OpenAICompatibleProvider);
        _ttsProviderBox.Items.Add(TextToSpeechClientFactory.MiniMaxProvider);
        _ttsProviderBox.SelectionChanged += (_, _) =>
        {
            if (!_isLoadingSettings)
            {
                ApplyProviderDefaults(overwriteCurrentProviderValues: true);
            }
        };

        _modelBox.SelectionChanged += (_, _) =>
        {
            if (_modelBox.SelectedItem is string model && ModelBaseUrls.TryGetValue(model, out var baseUrl))
            {
                _baseUrlBox.Text = baseUrl;
            }
        };

        var root = new DockPanel
        {
            Margin = new Thickness(16),
            LastChildFill = true
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var saveButton = new Button
        {
            Content = "保存",
            MinWidth = 88,
            MinHeight = 32,
            Margin = new Thickness(8, 0, 0, 0)
        };
        saveButton.Click += SaveButton_Click;

        var testButton = new Button
        {
            Content = "Test voice",
            MinWidth = 96,
            MinHeight = 32,
            Margin = new Thickness(8, 0, 0, 0)
        };
        testButton.Click += TestButton_Click;

        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 88,
            MinHeight = 32
        };
        cancelButton.Click += (_, _) => Close();

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(testButton);
        buttons.Children.Add(saveButton);
        root.Children.Add(buttons);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(scroll);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scroll.Content = grid;

        var row = 0;
        AddRow(grid, row++, "模型", _modelBox);
        AddRow(grid, row++, "Base URL", _baseUrlBox);
        AddRow(grid, row++, "API Key", _apiKeyBox);
        AddRow(grid, row++, "Key 环境变量", _apiKeyEnvBox);
        AddRow(grid, row++, "人设提示词", _systemPromptBox);
        AddRow(grid, row++, "温度", _temperatureBox);
        AddRow(grid, row++, "最大 Tokens", _maxTokensBox);
        AddRow(grid, row++, "历史轮数", _historyTurnsBox);
        AddRow(grid, row++, "超时秒数", _timeoutBox);
        AddRow(grid, row++, "代理 URL", _proxyUrlBox);
        AddRow(grid, row++, "Token 显示", _showTokenUsageBox);
        AddRow(grid, row++, "模型动作", _enableModelActionsBox);
        AddRow(grid, row++, "工作收益系数", _llmWorkMoneyMultiplierBox);
        AddSeparator(grid, row++);
        AddRow(grid, row++, "启用 TTS", _enableTtsBox);
        AddRow(grid, row++, "TTS Provider", _ttsProviderBox);
        AddRow(grid, row++, "TTS Base URL", _ttsBaseUrlBox);
        AddRow(grid, row++, "TTS Endpoint", _ttsEndpointPathBox);
        AddRow(grid, row++, "TTS 模型", _ttsModelBox);
        AddRow(grid, row++, "TTS 音色", _ttsVoiceBox);
        AddRow(grid, row++, "TTS 格式", _ttsFormatBox);
        AddRow(grid, row++, "TTS API Key", _ttsApiKeyBox);
        AddRow(grid, row++, "TTS Key 环境变量", _ttsApiKeyEnvBox);
        AddRow(grid, row++, "TTS Auth Scheme", _ttsAuthorizationSchemeBox);
        AddRow(grid, row++, "TTS 提示词", _ttsInstructionsBox);
        AddRow(grid, row++, "MiniMax Language", _miniMaxLanguageBoostBox);
        AddRow(grid, row++, "MiniMax Speed", _miniMaxSpeedBox);
        AddRow(grid, row++, "MiniMax Volume", _miniMaxVolumeBox);
        AddRow(grid, row++, "MiniMax Pitch", _miniMaxPitchBox);
        AddRow(grid, row++, "MiniMax SampleRate", _miniMaxSampleRateBox);
        AddRow(grid, row++, "MiniMax Bitrate", _miniMaxBitrateBox);
        AddRow(grid, row++, "MiniMax Channel", _miniMaxChannelBox);
        AddRow(grid, row, "配置文件", CreateReadOnlyText(_plugin.SettingsPath));

        return root;
    }

    private void LoadSettings(LLMChatSettings settings)
    {
        _isLoadingSettings = true;
        _baseUrlBox.Text = settings.BaseUrl;
        _modelBox.Text = settings.Model;
        _apiKeyBox.Password = settings.ApiKey;
        _apiKeyEnvBox.Text = settings.ApiKeyEnvironmentVariable;
        _systemPromptBox.Text = settings.SystemPrompt;
        _temperatureBox.Text = settings.Temperature.ToString(CultureInfo.InvariantCulture);
        _maxTokensBox.Text = settings.MaxTokens.ToString(CultureInfo.InvariantCulture);
        _historyTurnsBox.Text = settings.KeepHistoryTurns.ToString(CultureInfo.InvariantCulture);
        _timeoutBox.Text = settings.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _proxyUrlBox.Text = settings.ProxyUrl;
        _showTokenUsageBox.IsChecked = settings.ShowTokenUsage;
        _enableModelActionsBox.IsChecked = settings.EnableModelActions;
        _llmWorkMoneyMultiplierBox.Text = settings.LlmWorkMoneyMultiplier.ToString(CultureInfo.InvariantCulture);
        _enableTtsBox.IsChecked = settings.EnableTextToSpeech;
        var provider = string.IsNullOrWhiteSpace(settings.TtsProvider)
            ? TextToSpeechClientFactory.OpenAICompatibleProvider
            : settings.TtsProvider;
        _ttsProviderBox.SelectedItem = provider;
        _ttsProviderBox.Text = provider;
        _ttsBaseUrlBox.Text = settings.TtsBaseUrl;
        _ttsEndpointPathBox.Text = settings.TtsEndpointPath;
        _ttsModelBox.Text = settings.TtsModel;
        _ttsVoiceBox.Text = settings.TtsVoice;
        _ttsFormatBox.Text = settings.TtsResponseFormat;
        _ttsApiKeyBox.Password = settings.TtsApiKey;
        _ttsApiKeyEnvBox.Text = settings.TtsApiKeyEnvironmentVariable;
        _ttsAuthorizationSchemeBox.Text = settings.TtsAuthorizationScheme;
        _ttsInstructionsBox.Text = settings.TtsInstructions;
        _miniMaxLanguageBoostBox.Text = settings.MiniMaxLanguageBoost;
        _miniMaxSpeedBox.Text = settings.MiniMaxSpeed.ToString(CultureInfo.InvariantCulture);
        _miniMaxVolumeBox.Text = settings.MiniMaxVolume.ToString(CultureInfo.InvariantCulture);
        _miniMaxPitchBox.Text = settings.MiniMaxPitch.ToString(CultureInfo.InvariantCulture);
        _miniMaxSampleRateBox.Text = settings.MiniMaxSampleRate.ToString(CultureInfo.InvariantCulture);
        _miniMaxBitrateBox.Text = settings.MiniMaxBitrate.ToString(CultureInfo.InvariantCulture);
        _miniMaxChannelBox.Text = settings.MiniMaxChannel.ToString(CultureInfo.InvariantCulture);
        _isLoadingSettings = false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadSettings();
            _plugin.ApplySettings(settings);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "LLM Chat 设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadSettings();
            settings.EnableTextToSpeech = true;
            _plugin.ApplySettings(settings);
            await _plugin.SpeakPreviewAsync("你好，我是你的桌宠。MiniMax 语音测试开始啦。").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "TTS Test", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private LLMChatSettings ReadSettings()
    {
        var baseUrl = NormalizeBaseUrl(_baseUrlBox.Text);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("请填写 Base URL。");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Base URL 格式不正确。");
        }

        var model = _modelBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("请填写模型名称。");
        }

        var settings = _plugin.Settings.Clone();
        settings.BaseUrl = baseUrl;
        settings.Model = model;
        settings.ApiKey = _apiKeyBox.Password.Trim();
        settings.ApiKeyEnvironmentVariable = _apiKeyEnvBox.Text.Trim();
        settings.SystemPrompt = _systemPromptBox.Text.Trim();
        settings.Temperature = ReadFloat(_temperatureBox, "温度", 0, 2);
        settings.MaxTokens = ReadInt(_maxTokensBox, "最大 Tokens", 10, 32000);
        settings.KeepHistoryTurns = ReadInt(_historyTurnsBox, "历史轮数", 0, 100);
        settings.TimeoutSeconds = ReadInt(_timeoutBox, "超时秒数", 5, 300);
        settings.ProxyUrl = _proxyUrlBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(settings.ProxyUrl)
            && !Uri.TryCreate(settings.ProxyUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("代理 URL 格式不正确，例如 http://127.0.0.1:7890。");
        }

        settings.ShowTokenUsage = _showTokenUsageBox.IsChecked == true;
        settings.EnableModelActions = _enableModelActionsBox.IsChecked == true;
        settings.LlmWorkMoneyMultiplier = ReadFloat(_llmWorkMoneyMultiplierBox, "工作收益系数", 0.1f, 10.0f);
        settings.EnableTextToSpeech = _enableTtsBox.IsChecked == true;
        var selectedProvider = (_ttsProviderBox.SelectedItem as string) ?? _ttsProviderBox.Text;
        settings.TtsProvider = string.IsNullOrWhiteSpace(selectedProvider)
            ? TextToSpeechClientFactory.OpenAICompatibleProvider
            : selectedProvider.Trim();
        var isMiniMax = IsMiniMaxProvider(settings.TtsProvider);
        settings.TtsBaseUrl = NormalizeOptionalEndpointBaseUrl(
            _ttsBaseUrlBox.Text,
            isMiniMax ? "/t2a_v2" : "/audio/speech");
        settings.TtsEndpointPath = _ttsEndpointPathBox.Text.Trim();
        settings.TtsModel = _ttsModelBox.Text.Trim();
        settings.TtsVoice = _ttsVoiceBox.Text.Trim();
        settings.TtsResponseFormat = _ttsFormatBox.Text.Trim().ToLowerInvariant();
        settings.TtsApiKey = _ttsApiKeyBox.Password.Trim();
        settings.TtsApiKeyEnvironmentVariable = _ttsApiKeyEnvBox.Text.Trim();
        settings.TtsAuthorizationScheme = _ttsAuthorizationSchemeBox.Text.Trim();
        settings.TtsInstructions = _ttsInstructionsBox.Text.Trim();
        settings.MiniMaxLanguageBoost = string.IsNullOrWhiteSpace(_miniMaxLanguageBoostBox.Text)
            ? "auto"
            : _miniMaxLanguageBoostBox.Text.Trim();

        if (settings.EnableTextToSpeech)
        {
            if (!string.IsNullOrWhiteSpace(settings.TtsBaseUrl)
                && !Uri.TryCreate(settings.TtsBaseUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException("TTS Base URL 格式不正确。");
            }

            if (!string.IsNullOrWhiteSpace(settings.TtsEndpointPath)
                && !settings.TtsEndpointPath.StartsWith('/')
                && !Uri.TryCreate(settings.TtsEndpointPath, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException("TTS Endpoint 应填写 /audio/speech 这样的路径，或完整 URL。");
            }

            if (string.IsNullOrWhiteSpace(settings.TtsModel))
            {
                throw new InvalidOperationException("启用 TTS 时请填写 TTS 模型。");
            }

            if (string.IsNullOrWhiteSpace(settings.TtsVoice))
            {
                throw new InvalidOperationException("启用 TTS 时请填写 TTS 音色。");
            }

            if (string.IsNullOrWhiteSpace(settings.TtsResponseFormat))
            {
                throw new InvalidOperationException("启用 TTS 时请填写 TTS 格式。");
            }

            settings.MiniMaxSpeed = ReadFloat(_miniMaxSpeedBox, "MiniMax Speed", 0.5f, 2.0f);
            settings.MiniMaxVolume = ReadFloat(_miniMaxVolumeBox, "MiniMax Volume", 0.1f, 10.0f);
            settings.MiniMaxPitch = ReadInt(_miniMaxPitchBox, "MiniMax Pitch", -12, 12);
            settings.MiniMaxSampleRate = ReadInt(_miniMaxSampleRateBox, "MiniMax SampleRate", 8000, 48000);
            settings.MiniMaxBitrate = ReadInt(_miniMaxBitrateBox, "MiniMax Bitrate", 32000, 320000);
            settings.MiniMaxChannel = ReadInt(_miniMaxChannelBox, "MiniMax Channel", 1, 2);
        }

        return settings;
    }

    private void ApplyProviderDefaults(bool overwriteCurrentProviderValues)
    {
        if (IsMiniMaxProvider(_ttsProviderBox.Text))
        {
            if (overwriteCurrentProviderValues || string.IsNullOrWhiteSpace(_ttsBaseUrlBox.Text))
            {
                _ttsBaseUrlBox.Text = "https://api.minimax.io/v1";
            }

            if (overwriteCurrentProviderValues || _ttsModelBox.Text.StartsWith("tts-", StringComparison.OrdinalIgnoreCase))
            {
                _ttsModelBox.Text = "speech-2.8-turbo";
            }

            if (overwriteCurrentProviderValues || IsOpenAIVoice(_ttsVoiceBox.Text))
            {
                _ttsVoiceBox.Text = "Chinese (Mandarin)_Cute_Spirit";
            }

            if (overwriteCurrentProviderValues || _ttsApiKeyEnvBox.Text == "VPET_LLM_API_KEY")
            {
                _ttsApiKeyEnvBox.Text = "MINIMAX_API_KEY";
            }

            if (overwriteCurrentProviderValues || !IsMiniMaxFormat(_ttsFormatBox.Text))
            {
                _ttsFormatBox.Text = "mp3";
            }

            if (overwriteCurrentProviderValues || string.IsNullOrWhiteSpace(_ttsEndpointPathBox.Text))
            {
                _ttsEndpointPathBox.Text = string.Empty;
            }

            return;
        }

        if (overwriteCurrentProviderValues || string.IsNullOrWhiteSpace(_ttsBaseUrlBox.Text))
        {
            _ttsBaseUrlBox.Text = string.Empty;
        }

        if (overwriteCurrentProviderValues || _ttsModelBox.Text.StartsWith("speech-", StringComparison.OrdinalIgnoreCase))
        {
            _ttsModelBox.Text = "gpt-4o-mini-tts";
        }

        if (overwriteCurrentProviderValues || _ttsVoiceBox.Text.Contains("Chinese (Mandarin)", StringComparison.OrdinalIgnoreCase))
        {
            _ttsVoiceBox.Text = "alloy";
        }

        if (overwriteCurrentProviderValues || _ttsApiKeyEnvBox.Text == "MINIMAX_API_KEY")
        {
            _ttsApiKeyEnvBox.Text = string.Empty;
        }

        if (overwriteCurrentProviderValues || string.IsNullOrWhiteSpace(_ttsFormatBox.Text))
        {
            _ttsFormatBox.Text = "mp3";
        }

        if (overwriteCurrentProviderValues || string.IsNullOrWhiteSpace(_ttsEndpointPathBox.Text))
        {
            _ttsEndpointPathBox.Text = string.Empty;
        }
    }

    private static string NormalizeBaseUrl(string value)
    {
        var url = value.Trim().TrimEnd('/');
        const string suffix = "/chat/completions";
        return url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? url[..^suffix.Length]
            : url;
    }

    private static string NormalizeOptionalEndpointBaseUrl(string value, string suffix)
    {
        var url = value.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        return url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? url[..^suffix.Length]
            : url;
    }

    private static bool IsMiniMaxProvider(string value)
    {
        return value.Equals(TextToSpeechClientFactory.MiniMaxProvider, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenAIVoice(string value)
    {
        return new[] { "alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer" }
            .Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsMiniMaxFormat(string value)
    {
        return new[] { "mp3", "wav", "flac" }.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    private static int ReadInt(TextBox box, string fieldName, int min, int max)
    {
        if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"{fieldName} 必须是整数。");
        }

        return Math.Clamp(value, min, max);
    }

    private static float ReadFloat(TextBox box, string fieldName, float min, float max)
    {
        if (!float.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"{fieldName} 必须是数字，例如 0.8。");
        }

        return Math.Clamp(value, min, max);
    }

    private static void AddRow(Grid grid, int row, string label, FrameworkElement editor)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 7, 14, 7),
            VerticalAlignment = VerticalAlignment.Top
        };

        editor.Margin = new Thickness(0, 4, 0, 4);
        editor.VerticalAlignment = VerticalAlignment.Top;

        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(editor);
    }

    private static void AddSeparator(Grid grid, int row)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 12, 0, 12),
            Background = Brushes.LightGray
        };

        Grid.SetRow(separator, row);
        Grid.SetColumn(separator, 0);
        Grid.SetColumnSpan(separator, 2);
        grid.Children.Add(separator);
    }

    private static TextBox CreateTextBox(bool multiline = false)
    {
        var textBox = new TextBox
        {
            MinHeight = multiline ? 96 : 30,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            AcceptsReturn = multiline,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
        };

        if (multiline)
        {
            textBox.MaxHeight = 180;
        }

        return textBox;
    }

    private static TextBox CreateReadOnlyText(string text)
    {
        return new TextBox
        {
            Text = text,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            TextWrapping = TextWrapping.Wrap
        };
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VPet.Plugin.LLMChat;

public sealed class LLMChatInputWindow : Window
{
    private readonly LLMChatPlugin _plugin;
    private readonly TextBox _inputBox = new()
    {
        AcceptsReturn = true,
        Height = 56,
        MinHeight = 48,
        MaxHeight = 56,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    public LLMChatInputWindow(LLMChatPlugin plugin)
    {
        _plugin = plugin;

        Title = "LLM 聊天";
        Width = 360;
        Height = 150;
        MinWidth = 300;
        MinHeight = 140;
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        Content = BuildContent();
        Loaded += (_, _) => FocusInput();
    }

    public void FocusInput()
    {
        _inputBox.Focus();
        _inputBox.CaretIndex = _inputBox.Text.Length;
    }

    private DockPanel BuildContent()
    {
        var root = new DockPanel
        {
            Margin = new Thickness(12),
            LastChildFill = true
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var closeButton = new Button
        {
            Content = "关闭",
            MinWidth = 72,
            MinHeight = 30
        };
        closeButton.Click += (_, _) => Close();

        var sendButton = new Button
        {
            Content = "发送",
            MinWidth = 72,
            MinHeight = 30,
            Margin = new Thickness(8, 0, 0, 0)
        };
        sendButton.Click += (_, _) => Send();

        buttons.Children.Add(closeButton);
        buttons.Children.Add(sendButton);
        root.Children.Add(buttons);

        _inputBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Send();
                e.Handled = true;
            }
        };
        root.Children.Add(_inputBox);

        return root;
    }

    private void Send()
    {
        var text = _inputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this, "请输入聊天内容。", "LLM 聊天", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _plugin.SubmitChat(text);
        _inputBox.Clear();
        FocusInput();
    }
}

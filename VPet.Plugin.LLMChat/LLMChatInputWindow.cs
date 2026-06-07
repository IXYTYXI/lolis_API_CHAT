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
        MinHeight = 120,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    public LLMChatInputWindow(LLMChatPlugin plugin)
    {
        _plugin = plugin;

        Title = "LLM聊天";
        Width = 460;
        Height = 260;
        MinWidth = 360;
        MinHeight = 220;
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildContent();
        Loaded += (_, _) => _inputBox.Focus();
    }

    private DockPanel BuildContent()
    {
        var root = new DockPanel
        {
            Margin = new Thickness(14),
            LastChildFill = true
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 88,
            MinHeight = 32
        };
        cancelButton.Click += (_, _) => Close();

        var sendButton = new Button
        {
            Content = "发送",
            MinWidth = 88,
            MinHeight = 32,
            Margin = new Thickness(8, 0, 0, 0)
        };
        sendButton.Click += (_, _) => Send();

        buttons.Children.Add(cancelButton);
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
            MessageBox.Show(this, "请输入聊天内容。", "LLM聊天", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _plugin.SubmitChat(text);
        Close();
    }
}

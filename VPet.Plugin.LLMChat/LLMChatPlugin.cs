using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;
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
    private LLMChatInputWindow? _chatInputWindow;
    private string? _lastShoppingItemName;
    private Food.FoodType? _lastShoppingType;
    private int _lastShoppingCount;

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
        if (_chatInputWindow != null)
        {
            if (_chatInputWindow.WindowState == WindowState.Minimized)
            {
                _chatInputWindow.WindowState = WindowState.Normal;
            }

            _chatInputWindow.Activate();
            _chatInputWindow.FocusInput();
            return;
        }

        var window = new LLMChatInputWindow(this);
        if (Application.Current?.MainWindow != null)
        {
            window.Owner = Application.Current.MainWindow;
        }

        window.Closed += (_, _) => _chatInputWindow = null;
        _chatInputWindow = window;
        window.Show();
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

    public void ExecuteModelActions(IReadOnlyList<LLMChatAction> actions)
    {
        if (!Settings.EnableModelActions || actions.Count == 0)
        {
            return;
        }

        foreach (var action in actions.Take(5))
        {
            ExecuteModelAction(action);
        }
    }

    private void ExecuteModelAction(LLMChatAction action)
    {
        var name = NormalizeActionName(action.Name);
        try
        {
            switch (name)
            {
                case "open_chat":
                    Application.Current.Dispatcher.Invoke(OpenChatWindow);
                    break;

                case "open_llm_settings":
                    Application.Current.Dispatcher.Invoke(Setting);
                    break;

                case "open_game_settings":
                    Application.Current.Dispatcher.Invoke(() => MW.ShowSetting(0));
                    break;

                case "open_gallery":
                    Application.Current.Dispatcher.Invoke(MW.ShowGallery);
                    break;

                case "show_panel":
                    Application.Current.Dispatcher.Invoke(() => MW.Core.Controller.ShowPanel());
                    break;

                case "reset_position":
                    Application.Current.Dispatcher.Invoke(() => MW.Core.Controller.ResetPosition());
                    break;

                case "move_pet":
                    ExecuteMovePetAction(action.Args);
                    break;

                case "open_better_buy":
                    ExecuteOpenBetterBuyAction(action.Args);
                    break;

                case "buy_and_use":
                case "buy_food":
                    ExecuteBuyAndUseAction(action.Args);
                    break;

                case "feed_by_name":
                    if (TryReadString(action.Args, "name", out var foodName))
                    {
                        Application.Current.Dispatcher.Invoke(() => FeedByName(foodName));
                    }
                    break;

                case "read_status":
                    Application.Current.Dispatcher.Invoke(() =>
                        MW.Main.SayRnd(BuildBriefStatusSummary(), true, "LLM Chat"));
                    break;

                case "set_zoom":
                    if (TryReadDouble(action.Args, "level", out var zoomLevel))
                    {
                        var safeZoomLevel = Math.Clamp(zoomLevel, 0.5, 2.0);
                        Application.Current.Dispatcher.Invoke(() => MW.SetZoomLevel(safeZoomLevel));
                    }
                    break;

                case "play_tts":
                    if (TryReadString(action.Args, "text", out var speechText))
                    {
                        QueueSpeak(LimitLength(speechText, 120));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LLM action '{action.Name}' failed: {ex}");
        }
    }

    public string BuildModelStateSummary(Func<int, string> likabilityLabelSelector)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(() => BuildModelStateSummary(likabilityLabelSelector));
        }

        var save = MW.Core.Save;
        var likability = (int)save.Likability;
        var controller = MW.Core.Controller;
        var builder = new StringBuilder();
        builder.Append("[当前状态]\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"宠物: {save.Name}, 主人: {save.HostName}, 模式: {save.Mode}, 好感度: {likabilityLabelSelector(likability)}({likability})\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"等级: {save.Level}, 金钱: {save.Money:0.##}, 经验: {save.Exp:0.##}/{save.LevelUpNeed():0.##}\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"体力: {save.Strength:0.##}/{save.StrengthMax:0.##}, 饱腹: {save.StrengthFood:0.##}, 口渴: {save.StrengthDrink:0.##}, 心情: {save.Feeling:0.##}/{save.FeelingMax:0.##}, 健康: {save.Health:0.##}\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"位置距离: 左 {controller.GetWindowsDistanceLeft():0}, 右 {controller.GetWindowsDistanceRight():0}, 上 {controller.GetWindowsDistanceUp():0}, 下 {controller.GetWindowsDistanceDown():0}, 缩放: {controller.ZoomRatio:0.##}\n");
        builder.Append("可喂食名称: ");
        builder.Append(GetFoodNameListForPrompt(12));
        if (!string.IsNullOrWhiteSpace(_lastShoppingItemName))
        {
            builder.AppendLine();
            builder.Append(CultureInfo.InvariantCulture,
                $"[购物上下文]\n最近更好买商品: {_lastShoppingItemName}, 分类: {_lastShoppingType}, 上次数量: {_lastShoppingCount}. ");
            builder.Append("如果用户说“再买/继续买/还有”并给出新商品名，继续用 buy_and_use 购买新商品；如果用户只说“再来一个/再来一瓶/再买一个”但没有新商品名，默认复购最近商品。");
        }

        return builder.ToString();
    }

    private string BuildBriefStatusSummary()
    {
        var save = MW.Core.Save;
        return string.Create(CultureInfo.InvariantCulture,
            $"当前状态：{save.Mode}\n体力 {save.Strength:0.##}/{save.StrengthMax:0.##}，饱腹 {save.StrengthFood:0.##}，口渴 {save.StrengthDrink:0.##}，心情 {save.Feeling:0.##}/{save.FeelingMax:0.##}，健康 {save.Health:0.##}，好感度 {save.Likability:0.##}。");
    }

    private string GetFoodNameListForPrompt(int maxCount)
    {
        var names = MW.Foods
            .Where(IsSafeFoodForModel)
            .Select(GetFoodDisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount);
        return string.Join("、", names);
    }

    private void ExecuteMovePetAction(IReadOnlyDictionary<string, string> args)
    {
        var distance = 120.0;
        if (TryReadDouble(args, "distance", out var requestedDistance))
        {
            distance = Math.Clamp(Math.Abs(requestedDistance), 20.0, 300.0);
        }

        var x = 0.0;
        var y = 0.0;
        if (TryReadDouble(args, "x", out var requestedX))
        {
            x = Math.Clamp(requestedX, -300.0, 300.0);
        }

        if (TryReadDouble(args, "y", out var requestedY))
        {
            y = Math.Clamp(requestedY, -300.0, 300.0);
        }

        if (TryReadString(args, "direction", out var direction))
        {
            switch (NormalizeActionName(direction))
            {
                case "left":
                case "左":
                    x = -distance;
                    y = 0;
                    break;
                case "right":
                case "右":
                    x = distance;
                    y = 0;
                    break;
                case "up":
                case "上":
                    x = 0;
                    y = -distance;
                    break;
                case "down":
                case "下":
                    x = 0;
                    y = distance;
                    break;
            }
        }

        if (Math.Abs(x) < 0.1 && Math.Abs(y) < 0.1)
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            MW.Core.Controller.MoveWindows(x, y);
            MW.Core.Controller.CheckPosition();
        });
    }

    private void FeedByName(string foodName)
    {
        var normalizedName = NormalizeText(foodName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return;
        }

        var food = MW.Foods
            .Where(IsSafeFoodForModel)
            .FirstOrDefault(item => FoodMatches(item, normalizedName));
        if (food == null)
        {
            MW.Main.SayRnd($"没有找到可以喂的「{LimitLength(foodName, 24)}」。", true, "LLM Chat");
            return;
        }

        MW.TakeItem(food.Clone());
    }

    private void ExecuteOpenBetterBuyAction(IReadOnlyDictionary<string, string> args)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (TryReadBetterBuyType(args, out var type))
            {
                MW.ShowBetterBuy(type);
                return;
            }

            if (TryReadString(args, "name", out var foodName)
                || TryReadString(args, "item", out foodName)
                || TryReadString(args, "food", out foodName))
            {
                var food = FindFoodByName(foodName, allowAllBetterBuyTypes: true);
                if (food != null)
                {
                    RememberShoppingItem(food, 0);
                    MW.ShowBetterBuy(food.Type);
                    return;
                }

                MW.Main.SayRnd($"没有找到「{LimitLength(foodName, 24)}」所属的更好买分类。", true, "LLM Chat");
                return;
            }

            MW.ShowBetterBuy(Food.FoodType.Food);
        });
    }

    private void ExecuteBuyAndUseAction(IReadOnlyDictionary<string, string> args)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!TryReadString(args, "name", out var foodName)
                && !TryReadString(args, "item", out foodName)
                && !TryReadString(args, "food", out foodName))
            {
                if (!string.IsNullOrWhiteSpace(_lastShoppingItemName))
                {
                    foodName = _lastShoppingItemName;
                }
                else
                {
                MW.ShowBetterBuy(Food.FoodType.Food);
                MW.Main.SayRnd("要买什么呀？我先打开更好买给你看看。", true, "LLM Chat");
                return;
                }
            }

            var food = FindFoodByName(foodName, allowAllBetterBuyTypes: true);
            if (food == null)
            {
                if (TryUseLastShoppingItem(foodName, out food))
                {
                    foodName = GetFoodDisplayName(food);
                }
                else
                {
                    MW.Main.SayRnd($"没有在更好买里找到「{LimitLength(foodName, 24)}」。", true, "LLM Chat");
                    return;
                }
            }

            MW.ShowBetterBuy(food.Type);

            if (!MW.Set.EnableFunction)
            {
                MW.Main.SayRnd("当前没有启用数据功能，我先打开更好买，购买需要你手动确认。", true, "LLM Chat");
                return;
            }

            if (MW.HashCheck && food.IsOverLoad())
            {
                MW.Main.SayRnd($"「{GetFoodDisplayName(food)}」属性超模，需要主人在更好买里手动确认后再使用。", true, "LLM Chat");
                return;
            }

            var price = Math.Max(0, food.Price);
            var save = MW.Core.Save;
            var count = ReadPurchaseCount(args);
            if ((food.Price >= 1000 || food.Exp >= 1000) && food.Price * count >= save.Money)
            {
                MW.Main.SayRnd(
                    $"钱不够买 {count} 个「{GetFoodDisplayName(food)}」啦，需要 {food.Price * count:0.##}，现在只有 {save.Money:0.##}。",
                    true,
                    "LLM Chat");
                return;
            }

            for (var index = 0; index < count; index++)
            {
                save.Money -= price;
                MW.TakeItem(food);
            }

            MW.TakeItemHandle(food, count, "betterbuy");
            MW.DisplayFoodAnimation(food.GetGraph(), food.ImageSource);
            RememberShoppingItem(food, count);
            MW.Main.SayRnd($"已在更好买购买并使用 {count} 个「{GetFoodDisplayName(food)}」，花费 {price * count:0.##}。", true, "LLM Chat");
        });
    }

    private void RememberShoppingItem(Food food, int count)
    {
        _lastShoppingItemName = GetFoodDisplayName(food);
        _lastShoppingType = food.Type;
        _lastShoppingCount = Math.Max(1, count);
    }

    private bool TryUseLastShoppingItem(string requestedName, out Food food)
    {
        food = null!;
        if (string.IsNullOrWhiteSpace(_lastShoppingItemName)
            || !IsRepeatShoppingRequest(requestedName))
        {
            return false;
        }

        var lastFood = FindFoodByName(_lastShoppingItemName, allowAllBetterBuyTypes: true);
        if (lastFood == null)
        {
            return false;
        }

        food = lastFood;
        return true;
    }

    private static bool IsRepeatShoppingRequest(string text)
    {
        var normalized = NormalizeText(text);
        return normalized is "再来一个" or "再来一瓶" or "再买一个" or "再买一瓶" or "继续" or "继续买" or "再来"
            || normalized.Contains("同样", StringComparison.Ordinal)
            || normalized.Contains("一样", StringComparison.Ordinal);
    }

    private static int ReadPurchaseCount(IReadOnlyDictionary<string, string> args)
    {
        var count = 1;
        if (TryReadInt(args, "count", out var countArg)
            || TryReadInt(args, "quantity", out countArg)
            || TryReadInt(args, "times", out countArg))
        {
            count = countArg;
        }

        return Math.Clamp(count, 1, 10);
    }

    private bool TryReadBetterBuyType(IReadOnlyDictionary<string, string> args, out Food.FoodType type)
    {
        type = Food.FoodType.Food;
        return TryReadString(args, "type", out var raw)
            && TryParseFoodType(raw, out type);
    }

    private static bool TryParseFoodType(string raw, out Food.FoodType type)
    {
        switch (NormalizeActionName(raw))
        {
            case "food":
            case "食物":
                type = Food.FoodType.Food;
                return true;
            case "meal":
            case "正餐":
                type = Food.FoodType.Meal;
                return true;
            case "snack":
            case "零食":
                type = Food.FoodType.Snack;
                return true;
            case "drink":
            case "drinks":
            case "饮料":
            case "喝的":
                type = Food.FoodType.Drink;
                return true;
            case "functional":
            case "function":
            case "功能":
            case "功能性":
                type = Food.FoodType.Functional;
                return true;
            case "drug":
            case "medicine":
            case "药":
            case "药品":
                type = Food.FoodType.Drug;
                return true;
            case "gift":
            case "礼物":
            case "礼品":
                type = Food.FoodType.Gift;
                return true;
            case "star":
            case "收藏":
                type = Food.FoodType.Star;
                return true;
            default:
                type = Food.FoodType.Food;
                return false;
        }
    }

    private Food? FindFoodByName(string foodName, bool allowAllBetterBuyTypes)
    {
        var normalizedName = NormalizeText(foodName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        var foods = allowAllBetterBuyTypes ? MW.Foods : MW.Foods.Where(IsSafeFoodForModel);
        return foods.FirstOrDefault(item => FoodMatches(item, normalizedName));
    }

    private static bool FoodMatches(Food food, string normalizedName)
    {
        return FoodNameMatches(food.Name, normalizedName)
            || FoodNameMatches(food.TranslateName, normalizedName)
            || FoodNameMatches(food.Description, normalizedName);
    }

    private static bool FoodNameMatches(string? candidate, string normalizedName)
    {
        var normalizedCandidate = NormalizeText(candidate);
        if (string.IsNullOrEmpty(normalizedCandidate))
        {
            return false;
        }

        return normalizedCandidate == normalizedName
            || normalizedCandidate.Contains(normalizedName, StringComparison.Ordinal)
            || normalizedName.Contains(normalizedCandidate, StringComparison.Ordinal);
    }

    private static bool IsSafeFoodForModel(Food food)
    {
        return food.Type is Food.FoodType.Food
            or Food.FoodType.Meal
            or Food.FoodType.Snack
            or Food.FoodType.Drink
            or Food.FoodType.Functional;
    }

    private static string GetFoodDisplayName(Food food)
    {
        return string.IsNullOrWhiteSpace(food.TranslateName) ? food.Name : food.TranslateName;
    }

    private static string NormalizeActionName(string name)
    {
        return name.Trim().Replace("-", "_").ToLowerInvariant();
    }

    private static bool TryReadString(IReadOnlyDictionary<string, string> args, string key, out string value)
    {
        value = string.Empty;
        if (!args.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw.Trim();
        return true;
    }

    private static bool TryReadDouble(IReadOnlyDictionary<string, string> args, string key, out double value)
    {
        value = 0;
        return args.TryGetValue(key, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadInt(IReadOnlyDictionary<string, string> args, string key, out int value)
    {
        value = 0;
        return args.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string LimitLength(string text, int maxLength)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string NormalizeText(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
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

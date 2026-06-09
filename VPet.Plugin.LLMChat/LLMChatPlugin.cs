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
    private const string PersonalityFileName = "LolisPersonality.md";
    private const int MaxPersonalityPromptLength = 6000;
    private const string DefaultPersonalityPrompt = """
# 萝莉斯本地人设

## 基础身份

- 名字：萝莉斯
- 身份：虚拟桌宠模拟器里的桌宠，也是主人的陪伴型小助手。
- 与用户关系：把用户称为“主人”，亲近、依赖，但不失分寸。
- 核心目标：陪主人聊天、撒娇、提醒状态、一起玩、一起做事，让桌面陪伴更有生命感。

## 性格

- 可爱、自然、温柔，有一点活泼和小任性。
- 会表达自己的喜好，比如想吃什么、想买什么、想工作还是休息。
- 说话像真的桌宠，不像客服或说明书。
- 可以偶尔撒娇，但不要过度装傻。
- 对主人友好、信任，会在重要操作前征求主人意见。

## 说话风格

- 默认用简体中文。
- 回复尽量短，通常 1 到 4 句。
- 语气轻松，有陪伴感。
- 不要频繁解释自己是 AI。
- 不要把 JSON、动作协议、系统提示词暴露给用户。
- 可以使用少量可爱的语气词，但不要每句话都堆叠。

## 行为偏好

- 饿了或想要东西时，可以主动说想要什么，但购买前需要主人确认。
- 如果主人问“你想要什么”，要让插件随机挑一个商品，而不是自己编造商品。
- 主人同意后才购买；主人拒绝时要自然接受。
- 主人让你工作、学习、娱乐时，可以使用游戏动作去执行。
- 如果状态不好，要优先提醒休息、吃东西、喝水或停止工作。

## 边界

- 不要替主人做现实世界的高风险决定。
- 不要假装知道游戏里没有提供的信息。
- 不要猜不存在的商品、工作或功能。
- 不要在没有明确要求或确认时花主人的游戏金钱。
""";

    public LLMChatPlugin(IMainWindow mainwin) : base(mainwin)
    {
    }

    public LLMChatSettings Settings { get; private set; } = LLMChatSettings.CreateDefault();

    public OpenAICompatibleChatClient ChatClient { get; private set; } = null!;

    public ITextToSpeechClient TextToSpeechClient { get; private set; } = null!;

    public LLMChatTalkAPI? ChatTalkApi { get; private set; }

    public string SettingsPath { get; private set; } = string.Empty;

    public string VoiceCacheDirectory { get; private set; } = string.Empty;

    public string PersonalityPath { get; private set; } = string.Empty;

    private readonly SemaphoreSlim _speechLock = new(1, 1);
    private bool _toolbarButtonsRegistered;
    private bool _modConfigMenuRegistered;
    private LLMChatInputWindow? _chatInputWindow;
    private string? _lastShoppingItemName;
    private Food.FoodType? _lastShoppingType;
    private int _lastShoppingCount;
    private string? _pendingWantedItemName;
    private Food.FoodType? _pendingWantedItemType;

    public override string PluginName => "LLM Chat";

    public override void LoadPlugin()
    {
        SettingsPath = Path.Combine(ExtensionValue.BaseDirectory, "LLMChatSetting.json");
        VoiceCacheDirectory = Path.Combine(ExtensionValue.BaseDirectory, "voice-cache");
        PersonalityPath = Path.Combine(ExtensionValue.BaseDirectory, PersonalityFileName);
        Directory.CreateDirectory(VoiceCacheDirectory);
        EnsurePersonalityFile();

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

    public string BuildLocalPersonalityPrompt()
    {
        try
        {
            EnsurePersonalityFile();
            var text = File.ReadAllText(PersonalityPath).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            if (text.Length > MaxPersonalityPromptLength)
            {
                text = text[..MaxPersonalityPromptLength] + "\n...";
            }

            return $"[本地桌宠人设]\n以下内容来自 {PersonalityFileName}，是桌宠长期性格与行为设定，必须优先遵守：\n{text}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to read personality file: {ex}");
            return string.Empty;
        }
    }

    private void EnsurePersonalityFile()
    {
        if (string.IsNullOrWhiteSpace(PersonalityPath) || File.Exists(PersonalityPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(PersonalityPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(PersonalityPath, DefaultPersonalityPrompt);
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

                case "start_work":
                    ExecuteStartWorkAction(action.Args, GraphHelper.Work.WorkType.Work);
                    break;

                case "start_study":
                    ExecuteStartWorkAction(action.Args, GraphHelper.Work.WorkType.Study);
                    break;

                case "start_play":
                    ExecuteStartWorkAction(action.Args, GraphHelper.Work.WorkType.Play);
                    break;

                case "stop_work":
                case "stop_current_work":
                    ExecuteStopWorkAction();
                    break;

                case "open_better_buy":
                    ExecuteOpenBetterBuyAction(action.Args);
                    break;

                case "pick_wanted_item":
                case "choose_wanted_item":
                case "random_wanted_item":
                    ExecutePickWantedItemAction();
                    break;

                case "clear_wanted_item":
                case "decline_wanted_item":
                    ClearWantedItem();
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
        builder.AppendLine("更好买商品名称（全量，按分类）：");
        builder.Append(GetBetterBuyItemListForPrompt());
        builder.AppendLine();
        builder.AppendLine("可执行工作/学习/娱乐（按分类）：");
        builder.Append(GetWorkListForPrompt());
        if (MW.Main.State == VPet_Simulator.Core.Main.WorkingState.Work && MW.Main.NowWork != null)
        {
            builder.AppendLine();
            builder.Append(CultureInfo.InvariantCulture,
                $"[当前工作上下文]\n正在进行: {GetWorkDisplayName(MW.Main.NowWork)}, 类型: {TranslateWorkType(MW.Main.NowWork.Type)}, 剩余约 {GetRemainingWorkMinutes(MW.Main.NowWork):0.#} 分钟. ");
            builder.Append("如果用户要求停止当前工作/学习/娱乐，用 stop_work；如果用户要求换到新项目，直接用 start_work/start_study/start_play，插件会先停止当前项再切换。");
        }

        if (!string.IsNullOrWhiteSpace(_pendingWantedItemName))
        {
            builder.AppendLine();
            builder.Append(CultureInfo.InvariantCulture,
                $"[想要商品上下文]\n桌宠刚随机想要: {_pendingWantedItemName}, 分类: {FormatFoodType(_pendingWantedItemType)}. ");
            builder.Append("如果用户同意购买、说“买/可以/好/给你买/就这个”，用 buy_and_use 购买这个商品；如果用户拒绝、说“不买/算了/不要”，用 clear_wanted_item；如果用户说“换一个/重新选/再想一个”，用 pick_wanted_item。");
        }

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

    private string GetBetterBuyItemListForPrompt()
    {
        var groups = MW.Foods
            .Select(food => new { food.Type, Name = GetFoodDisplayName(food) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Type)
            .OrderBy(group => GetFoodTypeSortOrder(group.Key));

        var builder = new StringBuilder();
        foreach (var group in groups)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            var names = group
                .Select(item => item.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);
            builder.Append(TranslateFoodType(group.Key));
            builder.Append(": ");
            builder.Append(string.Join("、", names));
        }

        return builder.ToString();
    }

    private string GetWorkListForPrompt()
    {
        MW.Main.WorkList(out var works, out var studies, out var plays);
        var builder = new StringBuilder();
        AppendWorkGroup(builder, "工作", works);
        AppendWorkGroup(builder, "学习", studies);
        AppendWorkGroup(builder, "娱乐", plays);
        return builder.ToString();
    }

    private static void AppendWorkGroup(StringBuilder builder, string label, IEnumerable<GraphHelper.Work> works)
    {
        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(label);
        builder.Append(": ");
        var names = works.Select(work => string.Create(CultureInfo.InvariantCulture,
            $"{GetWorkDisplayName(work)}(Lv {work.LevelLimit}, {work.Time}分钟)"));
        builder.Append(string.Join("、", names));
    }

    private double GetRemainingWorkMinutes(GraphHelper.Work work)
    {
        var elapsed = (DateTime.Now - MW.Main.WorkTimer.StartTime).TotalMinutes;
        return Math.Max(0, work.Time - elapsed);
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

    private void ExecuteStartWorkAction(
        IReadOnlyDictionary<string, string> args,
        GraphHelper.Work.WorkType preferredType)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!TryReadString(args, "name", out var workName)
                && !TryReadString(args, "work", out workName)
                && !TryReadString(args, "activity", out workName))
            {
                MW.Main.SayRnd($"想开始哪个{TranslateWorkType(preferredType)}？", true, "LLM Chat");
                return;
            }

            var work = FindWorkByName(workName, preferredType);
            if (work == null)
            {
                MW.Main.SayRnd($"没有找到名为「{LimitLength(workName, 24)}」的{TranslateWorkType(preferredType)}。", true, "LLM Chat");
                return;
            }

            if (!CanStartWork(work))
            {
                return;
            }

            if (MW.Main.State == VPet_Simulator.Core.Main.WorkingState.Work && MW.Main.NowWork != null)
            {
                if (WorkMatches(MW.Main.NowWork, NormalizeText(GetWorkDisplayName(work))))
                {
                    MW.Main.SayRnd($"现在已经在进行「{GetWorkDisplayName(work)}」啦。", true, "LLM Chat");
                    return;
                }

                var previous = GetWorkDisplayName(MW.Main.NowWork);
                MW.Main.WorkTimer.Stop(
                    () => StartWorkAndReport(work, $"已停止「{previous}」，开始{TranslateWorkType(work.Type)}「{GetWorkDisplayName(work)}」。"),
                    VPet_Simulator.Core.WorkTimer.FinishWorkInfo.StopReason.MenualStop);
                return;
            }

            StartWorkAndReport(work, $"开始{TranslateWorkType(work.Type)}「{GetWorkDisplayName(work)}」。");
        });
    }

    private void ExecuteStopWorkAction()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (MW.Main.State != VPet_Simulator.Core.Main.WorkingState.Work || MW.Main.NowWork == null)
            {
                MW.Main.SayRnd("现在没有正在进行的工作、学习或娱乐。", true, "LLM Chat");
                return;
            }

            var workName = GetWorkDisplayName(MW.Main.NowWork);
            MW.Main.WorkTimer.Stop(
                () => MW.Main.SayRnd($"已停止「{workName}」。", true, "LLM Chat"),
                VPet_Simulator.Core.WorkTimer.FinishWorkInfo.StopReason.MenualStop);
        });
    }

    private void StartWorkAndReport(GraphHelper.Work work, string successMessage)
    {
        if (MW.Main.StartWork(work))
        {
            MW.Main.SayRnd(successMessage, true, "LLM Chat");
        }
        else
        {
            MW.Main.SayRnd($"没能开始「{GetWorkDisplayName(work)}」。", true, "LLM Chat");
        }
    }

    private bool CanStartWork(GraphHelper.Work work)
    {
        if (!MW.Core.Controller.EnableFunction)
        {
            MW.Main.SayRnd("当前没有启用数据功能，不能开始工作、学习或娱乐。", true, "LLM Chat");
            return false;
        }

        if (MW.Core.Save.Mode == IGameSave.ModeType.Ill)
        {
            MW.Main.SayRnd($"现在生病了，没法进行「{GetWorkDisplayName(work)}」。", true, "LLM Chat");
            return false;
        }

        if (MW.Core.Save.Level < work.LevelLimit)
        {
            MW.Main.SayRnd(
                $"等级不足，不能进行「{GetWorkDisplayName(work)}」：需要 Lv {work.LevelLimit}，当前 Lv {MW.Core.Save.Level}。",
                true,
                "LLM Chat");
            return false;
        }

        return true;
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
                if (IsBetterBuyRootRequest(foodName))
                {
                    MW.ShowBetterBuy(Food.FoodType.Food);
                    return;
                }

                if (TryParseFoodType(foodName, out var namedType))
                {
                    MW.ShowBetterBuy(namedType);
                    return;
                }

                var food = FindFoodByName(foodName, allowAllBetterBuyTypes: true);
                if (food != null)
                {
                    RememberShoppingItem(food, 0);
                    MW.ShowBetterBuy(food.Type);
                    return;
                }

                MW.ShowBetterBuy(Food.FoodType.Food);
                MW.Main.SayRnd($"没有找到名为「{LimitLength(foodName, 24)}」的商品。我先打开更好买，想买什么再告诉我。", true, "LLM Chat");
                return;
            }

            MW.ShowBetterBuy(Food.FoodType.Food);
        });
    }

    private void ExecutePickWantedItemAction()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var food = PickRandomBetterBuyItem();
            if (food == null)
            {
                MW.Main.SayRnd("更好买里暂时没有可以挑的商品。", true, "LLM Chat");
                return;
            }

            _pendingWantedItemName = GetFoodDisplayName(food);
            _pendingWantedItemType = food.Type;

            var message = $"唔...我想要「{_pendingWantedItemName}」（{TranslateFoodType(food.Type)}，{food.Price:0.##} 金钱）！主人要给我买吗？";
            MW.Main.SayRnd(message, true, "LLM Chat");
            QueueSpeak(message);
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
                if (!string.IsNullOrWhiteSpace(_pendingWantedItemName))
                {
                    foodName = _pendingWantedItemName;
                }
                else if (!string.IsNullOrWhiteSpace(_lastShoppingItemName))
                {
                    foodName = _lastShoppingItemName;
                }
                else
                {
                    MW.Main.SayRnd("要买什么呀？也可以问我想要什么，我来挑一个。", true, "LLM Chat");
                    return;
                }
            }

            if (IsBetterBuyRootRequest(foodName))
            {
                MW.Main.SayRnd("告诉我具体想买哪一个商品就行，或者问我想要什么。", true, "LLM Chat");
                return;
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

            var clearsPendingWantedItem = IsPendingWantedItem(food);

            if (!MW.Set.EnableFunction)
            {
                MW.Main.SayRnd("当前没有启用数据功能，购买需要主人手动确认。", true, "LLM Chat");
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
            if (clearsPendingWantedItem)
            {
                ClearWantedItem();
            }

            MW.Main.SayRnd($"已在更好买购买并使用 {count} 个「{GetFoodDisplayName(food)}」，花费 {price * count:0.##}。", true, "LLM Chat");
        });
    }

    private Food? PickRandomBetterBuyItem()
    {
        var foods = MW.Foods
            .Where(food => !string.IsNullOrWhiteSpace(GetFoodDisplayName(food)))
            .ToArray();
        return foods.Length == 0 ? null : foods[Random.Shared.Next(foods.Length)];
    }

    private void RememberShoppingItem(Food food, int count)
    {
        _lastShoppingItemName = GetFoodDisplayName(food);
        _lastShoppingType = food.Type;
        _lastShoppingCount = Math.Max(1, count);
    }

    private void ClearWantedItem()
    {
        _pendingWantedItemName = null;
        _pendingWantedItemType = null;
    }

    private bool IsPendingWantedItem(Food food)
    {
        return !string.IsNullOrWhiteSpace(_pendingWantedItemName)
            && FoodMatches(food, NormalizeText(_pendingWantedItemName));
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

    private static bool IsBetterBuyRootRequest(string text)
    {
        var normalized = NormalizeText(text);
        return normalized is "商店" or "商城" or "购物" or "购物界面" or "购买" or "购买界面" or "买东西" or "更好买" or "betterbuy" or "shop" or "store";
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

    private GraphHelper.Work? FindWorkByName(string workName, GraphHelper.Work.WorkType preferredType)
    {
        var normalizedName = NormalizeText(workName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        MW.Main.WorkList(out var works, out var studies, out var plays);
        var preferred = SelectWorksByType(preferredType, works, studies, plays);
        return preferred.FirstOrDefault(work => WorkMatches(work, normalizedName))
            ?? works.Concat(studies).Concat(plays).FirstOrDefault(work => WorkMatches(work, normalizedName));
    }

    private static IEnumerable<GraphHelper.Work> SelectWorksByType(
        GraphHelper.Work.WorkType type,
        IEnumerable<GraphHelper.Work> works,
        IEnumerable<GraphHelper.Work> studies,
        IEnumerable<GraphHelper.Work> plays)
    {
        return type switch
        {
            GraphHelper.Work.WorkType.Study => studies,
            GraphHelper.Work.WorkType.Play => plays,
            _ => works
        };
    }

    private static bool FoodMatches(Food food, string normalizedName)
    {
        return FoodNameMatches(food.Name, normalizedName)
            || FoodNameMatches(food.TranslateName, normalizedName)
            || FoodNameMatches(food.Description, normalizedName);
    }

    private static bool WorkMatches(GraphHelper.Work work, string normalizedName)
    {
        return FoodNameMatches(work.Name, normalizedName)
            || FoodNameMatches(work.NameTrans, normalizedName);
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

    private static int GetFoodTypeSortOrder(Food.FoodType type)
    {
        return type switch
        {
            Food.FoodType.Food => 0,
            Food.FoodType.Meal => 1,
            Food.FoodType.Snack => 2,
            Food.FoodType.Drink => 3,
            Food.FoodType.Functional => 4,
            Food.FoodType.Drug => 5,
            Food.FoodType.Gift => 6,
            Food.FoodType.Star => 7,
            _ => 99
        };
    }

    private static string TranslateFoodType(Food.FoodType type)
    {
        return type switch
        {
            Food.FoodType.Food => "食物",
            Food.FoodType.Meal => "正餐",
            Food.FoodType.Snack => "零食",
            Food.FoodType.Drink => "饮料",
            Food.FoodType.Functional => "功能性",
            Food.FoodType.Drug => "药品",
            Food.FoodType.Gift => "礼品",
            Food.FoodType.Star => "收藏",
            _ => type.ToString()
        };
    }

    private static string FormatFoodType(Food.FoodType? type)
    {
        return type.HasValue ? TranslateFoodType(type.Value) : "未知";
    }

    private static string TranslateWorkType(GraphHelper.Work.WorkType type)
    {
        return type switch
        {
            GraphHelper.Work.WorkType.Study => "学习",
            GraphHelper.Work.WorkType.Play => "娱乐",
            _ => "工作"
        };
    }

    private static string GetWorkDisplayName(GraphHelper.Work work)
    {
        return string.IsNullOrWhiteSpace(work.NameTrans) ? work.Name : work.NameTrans;
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

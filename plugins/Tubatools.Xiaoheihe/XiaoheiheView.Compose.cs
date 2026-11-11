using System.Text;
using System.Text.Json;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Tubatools.Xiaoheihe.Services;

namespace Tubatools.Xiaoheihe;

internal sealed partial class XiaoheiheView
{
    private enum ComposeBlockKind { Text, Heading, Image }

    private sealed class ComposeBlock
    {
        public ComposeBlockKind Kind { get; init; }
        public string Text { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string LocalPath { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
    }

    private readonly List<ComposeBlock> _composeBlocks = [];
    private readonly List<XhhTopic> _composeTopics = [];
    private bool _composeTopicsLoaded;
    private bool _composeTopicUpdating;
    private ComboBox? _composeTopicBox;
    private TextBox? _composeCustomTopicBox;
    private TextBox? _composeTopicSearchBox;
    private TextBlock? _composeTopicStatus;
    private ComboBox? _composeModeBox;
    private TextBox? _composeTitleBox;
    private StackPanel? _composeBlocksPanel;
    private StackPanel? _composePreviewPanel;
    private ToggleButton? _composePreviewToggle;
    private TextBlock? _composeStatus;
    private Button? _composeHeadingButton;
    private TextBox? _lastComposeTextBox;
    private string _composeSelectedTopicId = "7214";
    private string _composeSelectedTopicName = "盒友杂谈";

    private async Task ShowComposePageAsync()
    {
        _pageTitle.Text = "发帖";
        _pageSubtitle.Text = "发布图文帖或文章到小黑盒社区";
        _refreshAction = () => ReloadComposeTopicsAsync();
        if (!_client.IsLoggedIn)
        {
            BuildComposeLoginPage();
            return;
        }
        BuildComposePage();
        if (!_composeTopicsLoaded) await LoadComposeTopicsAsync();
    }

    private void BuildComposeLoginPage()
    {
        _refreshAction = null;
        var surface = Surface();
        var card = Card(28);
        var stack = new StackPanel { Spacing = 14, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new FontIcon
        {
            Glyph = "\uE70F",
            FontSize = 48,
            Foreground = _palette.AccentText,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        TextBlock title = T("登录后发帖", 24, weight: FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(title);
        TextBlock subtitle = T("发帖需要登录态与风控令牌。建议使用「账号 → 网页登录」读取一次 Cookie。",
            13, secondary: true, wrap: true);
        subtitle.TextAlignment = TextAlignment.Center;
        stack.Children.Add(subtitle);
        Button login = AccentBtn("去登录", "\uE77B");
        login.HorizontalAlignment = HorizontalAlignment.Center;
        login.Click += async (_, _) => await NavigateAsync("account");
        stack.Children.Add(login);
        card.Child = stack;
        surface.Children.Add(card);
        ShowPage(Scroll(surface));
    }

    private void BuildComposePage()
    {
        var surface = Surface();
        var card = Card(20);
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(Section("发布新帖", "选择社区、组织正文块，插入图片后可预览再发布。"));

        var topicGrid = new Grid { ColumnSpacing = 10 };
        topicGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topicGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        _composeTopicBox = new ComboBox { Header = "发布到社区", HorizontalAlignment = HorizontalAlignment.Stretch };
        _composeTopicBox.SelectionChanged += (_, _) =>
        {
            if (_composeTopicUpdating) return;
            if (_composeTopicBox.SelectedItem is ComboBoxItem { Tag: string id } item)
            {
                _composeSelectedTopicId = id;
                _composeSelectedTopicName = item.Content?.ToString() ?? id;
            }
        };
        topicGrid.Children.Add(_composeTopicBox);
        _composeCustomTopicBox = new TextBox
        {
            Header = "自定义 topic_id",
            PlaceholderText = "可选",
            MaxLength = 20,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetColumn(_composeCustomTopicBox, 1);
        topicGrid.Children.Add(_composeCustomTopicBox);
        stack.Children.Add(topicGrid);

        var searchGrid = new Grid { ColumnSpacing = 10 };
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _composeTopicSearchBox = new TextBox
        {
            Header = "搜索社区",
            PlaceholderText = "例如：盒友杂谈、数码硬件、影之刃零",
            MaxLength = 40
        };
        _composeTopicSearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) await RunAsync(SearchComposeTopicsAsync);
        };
        searchGrid.Children.Add(_composeTopicSearchBox);
        Button search = AccentBtn("搜索", "\uE721");
        search.VerticalAlignment = VerticalAlignment.Bottom;
        search.Click += async (_, _) => await RunAsync(SearchComposeTopicsAsync);
        Grid.SetColumn(search, 1);
        searchGrid.Children.Add(search);
        stack.Children.Add(searchGrid);

        _composeTopicStatus = T("正在获取可发布社区…", 10, tertiary: true, wrap: true);
        stack.Children.Add(_composeTopicStatus);

        _composeModeBox = new ComboBox { Header = "帖子类型", Width = 300, SelectedIndex = 0 };
        _composeModeBox.Items.Add(new ComboBoxItem { Content = "图文帖（文字与图片分开）", Tag = "27" });
        _composeModeBox.Items.Add(new ComboBoxItem { Content = "文章帖（图文混合，可加小标题）", Tag = "11" });
        _composeModeBox.SelectionChanged += (_, _) =>
        {
            if (_composeHeadingButton is not null) _composeHeadingButton.IsEnabled = ComposeArticleMode;
            RenderComposePreview();
        };
        stack.Children.Add(_composeModeBox);

        _composeTitleBox = new TextBox
        {
            Header = "标题",
            PlaceholderText = "一句话说清楚这篇帖子讲什么",
            MaxLength = 60
        };
        stack.Children.Add(_composeTitleBox);

        var blockHeader = new Grid { ColumnSpacing = 10 };
        blockHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        blockHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var blockTitle = new StackPanel { Spacing = 2 };
        blockTitle.Children.Add(T("正文", 17, weight: FontWeights.SemiBold));
        blockTitle.Children.Add(T("按块组织内容：文字、小标题和图片。", 11, secondary: true, wrap: true));
        blockHeader.Children.Add(blockTitle);
        var blockButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        Button addText = Btn("添加文字", "\uE8A5");
        addText.Click += (_, _) =>
        {
            _composeBlocks.Add(new ComposeBlock { Kind = ComposeBlockKind.Text });
            RenderComposeBlocks();
        };
        blockButtons.Children.Add(addText);
        _composeHeadingButton = Btn("添加小标题", "\uE8A4");
        _composeHeadingButton.IsEnabled = false;
        _composeHeadingButton.Click += (_, _) =>
        {
            _composeBlocks.Add(new ComposeBlock { Kind = ComposeBlockKind.Heading });
            RenderComposeBlocks();
        };
        blockButtons.Children.Add(_composeHeadingButton);
        Button addImage = AccentBtn("插入图片", "\uEB9F");
        addImage.Click += async (_, _) => await RunAsync(() => PickAndUploadImageAsync(null));
        blockButtons.Children.Add(addImage);
        Button emoji = Btn("表情", "\uE76E");
        emoji.Click += (_, _) => ShowEmojiFlyout(emoji, token =>
        {
            if (_lastComposeTextBox is not null) InsertTextAtCaret(_lastComposeTextBox, token);
        }, above: false);
        blockButtons.Children.Add(emoji);
        Grid.SetColumn(blockButtons, 1);
        blockHeader.Children.Add(blockButtons);
        stack.Children.Add(blockHeader);

        _composeBlocksPanel = new StackPanel { Spacing = 8 };
        stack.Children.Add(_composeBlocksPanel);
        RenderComposeBlocks();

        var previewRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _composePreviewToggle = new ToggleButton { Content = "预览" };
        _composePreviewToggle.Checked += (_, _) =>
        {
            if (_composePreviewPanel is not null) _composePreviewPanel.Visibility = Visibility.Visible;
            RenderComposePreview();
        };
        _composePreviewToggle.Unchecked += (_, _) =>
        {
            if (_composePreviewPanel is not null) _composePreviewPanel.Visibility = Visibility.Collapsed;
        };
        previewRow.Children.Add(_composePreviewToggle);
        previewRow.Children.Add(T("预览按当前块顺序渲染；发布后以服务端渲染为准。", 10, tertiary: true, wrap: true));
        stack.Children.Add(previewRow);
        _composePreviewPanel = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        stack.Children.Add(_composePreviewPanel);

        _composeStatus = T("图片会先上传到小黑盒图床。发布前会再次确认。", 11, tertiary: true, wrap: true);
        stack.Children.Add(_composeStatus);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        Button publish = AccentBtn("发布帖子", "\uE724");
        publish.Click += async (_, _) => await RunAsync(PublishComposeAsync);
        actions.Children.Add(publish);
        Button clear = Btn("清空", "\uE74D");
        clear.Click += (_, _) => ClearCompose();
        actions.Children.Add(clear);
        stack.Children.Add(actions);

        stack.Children.Add(Divider());
        stack.Children.Add(T("图文帖：文字与图片作为独立块提交；文章帖：生成 HTML，图片按块位置夹在文字之间。两种帖子都有标题。",
            10, tertiary: true, wrap: true));
        card.Child = stack;
        surface.Children.Add(card);
        ShowPage(Scroll(surface));
        RenderComposeTopicItems();
    }

    private bool ComposeArticleMode
        => ((_composeModeBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "27") == "11";

    private void RenderComposeTopicItems()
    {
        if (_composeTopicBox is null) return;
        _composeTopicUpdating = true;
        _composeTopicBox.Items.Clear();
        foreach (XhhTopic topic in _composeTopics)
            _composeTopicBox.Items.Add(new ComboBoxItem { Content = $"{topic.Name}（{topic.Id}）", Tag = topic.Id });
        int index = _composeTopics.FindIndex(t => t.Id == _composeSelectedTopicId);
        if (index < 0 && _composeTopics.Count > 0) index = 0;
        if (index >= 0)
        {
            _composeTopicBox.SelectedIndex = index;
            _composeSelectedTopicId = _composeTopics[index].Id;
            _composeSelectedTopicName = _composeTopics[index].Name;
        }
        _composeTopicUpdating = false;
    }

    private void AddComposeTopic(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return;
        if (_composeTopics.Any(t => t.Id == id)) return;
        _composeTopics.Add(new XhhTopic(id, name, "", 0));
    }

    private async Task LoadComposeTopicsAsync()
    {
        AddComposeTopic("7214", "盒友杂谈");
        AddComposeTopic("18745", "数码硬件");
        AddComposeTopic("549998", "百科知识");
        try
        {
            JsonElement result = await _client.EditorTopicsAsync(_lifetime.Token);
            foreach (XhhTopic topic in XhhParser.ParseTopicList(result, "topic_list"))
                AddComposeTopic(topic.Id, topic.Name);
            _composeTopicsLoaded = true;
        }
        catch (XhhException ex)
        {
            if (_composeTopicStatus is not null)
                _composeTopicStatus.Text = "社区列表加载失败：" + ex.Message + "（仍可使用默认社区或自定义 ID）";
        }
        try
        {
            JsonElement categories = await _client.TopicCategoriesAsync(_lifetime.Token);
            foreach (XhhTopicGroup group in XhhParser.ParseTopicGroups(categories))
                foreach (XhhTopic topic in group.Topics)
                    AddComposeTopic(topic.Id, topic.Name);
        }
        catch (XhhException)
        {
        }
        if (_composeTopicStatus is not null)
            _composeTopicStatus.Text = $"已加载 {_composeTopics.Count} 个可发布社区，可搜索更多。";
        RenderComposeTopicItems();
    }

    private async Task ReloadComposeTopicsAsync()
    {
        _composeTopicsLoaded = false;
        _composeTopics.Clear();
        await LoadComposeTopicsAsync();
    }

    private async Task SearchComposeTopicsAsync()
    {
        if (_composeTopicSearchBox is null || _composeTopicStatus is null) return;
        string query = _composeTopicSearchBox.Text.Trim();
        if (query.Length == 0)
        {
            _composeTopicStatus.Text = "请输入社区名称后再搜索。";
            return;
        }
        _composeTopicStatus.Text = $"正在搜索「{query}」…";
        JsonElement result = await _client.SearchEditorTopicsAsync(query, _lifetime.Token);
        var topics = XhhParser.ParseTopicList(result, "search_result");
        foreach (XhhTopic topic in topics) AddComposeTopic(topic.Id, topic.Name);
        if (topics.Count > 0)
        {
            _composeSelectedTopicId = topics[0].Id;
            _composeSelectedTopicName = topics[0].Name;
            RenderComposeTopicItems();
            _composeTopicStatus.Text = $"找到 {topics.Count} 个社区，已选择「{topics[0].Name}」。";
        }
        else
        {
            _composeTopicStatus.Text = "没有找到匹配的社区，可换关键词或直接填写 topic_id。";
        }
    }

    private void RenderComposeBlocks()
    {
        if (_composeBlocksPanel is null) return;
        if (_composeBlocks.Count == 0)
            _composeBlocks.Add(new ComposeBlock { Kind = ComposeBlockKind.Text });
        _composeBlocksPanel.Children.Clear();
        _lastComposeTextBox = null;
        for (int i = 0; i < _composeBlocks.Count; i++)
            _composeBlocksPanel.Children.Add(BuildComposeBlock(_composeBlocks[i], i));
        RenderComposePreview();
    }

    private FrameworkElement BuildComposeBlock(ComposeBlock block, int index)
    {
        Border card = Card(12);
        var stack = new StackPanel { Spacing = 7 };
        var header = new Grid { ColumnSpacing = 6 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string label = block.Kind switch
        {
            ComposeBlockKind.Heading => "小标题",
            ComposeBlockKind.Image => "图片",
            _ => "文字"
        };
        header.Children.Add(T(label, 11, secondary: true, weight: FontWeights.SemiBold));

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        Button up = IconButton("\uE74A", "上移");
        up.IsEnabled = index > 0;
        up.Click += (_, _) => MoveComposeBlock(index, -1);
        tools.Children.Add(up);
        Button down = IconButton("\uE74B", "下移");
        down.IsEnabled = index < _composeBlocks.Count - 1;
        down.Click += (_, _) => MoveComposeBlock(index, 1);
        tools.Children.Add(down);
        Button remove = IconButton("\uE74D", "删除此块");
        remove.Click += (_, _) =>
        {
            if (index >= 0 && index < _composeBlocks.Count) _composeBlocks.RemoveAt(index);
            RenderComposeBlocks();
        };
        tools.Children.Add(remove);
        Grid.SetColumn(tools, 1);
        header.Children.Add(tools);
        stack.Children.Add(header);

        if (block.Kind == ComposeBlockKind.Image)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(RemoteImage(block.ImageUrl, 220, 138, 8));
            var info = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(T(block.Width > 0 ? $"{block.Width} × {block.Height}" : "尺寸未知", 11, secondary: true));
            if (!string.IsNullOrWhiteSpace(block.LocalPath))
                info.Children.Add(T(Path.GetFileName(block.LocalPath), 10, tertiary: true));
            var imageButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            Button replace = Btn("更换图片", "\uE8B9");
            replace.Click += async (_, _) => await RunAsync(() => PickAndUploadImageAsync(block));
            imageButtons.Children.Add(replace);
            Button open = Btn("浏览器打开", "\uE774");
            open.IsEnabled = !string.IsNullOrWhiteSpace(block.ImageUrl);
            open.Click += async (_, _) =>
            {
                if (Uri.TryCreate(block.ImageUrl, UriKind.Absolute, out Uri? uri))
                    await _host.Services.OpenUriAsync(uri);
            };
            imageButtons.Children.Add(open);
            info.Children.Add(imageButtons);
            Grid.SetColumn(info, 1);
            row.Children.Add(info);
            stack.Children.Add(row);
        }
        else
        {
            var input = new TextBox
            {
                Text = block.Text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = block.Kind == ComposeBlockKind.Heading ? 48 : 78,
                FontSize = block.Kind == ComposeBlockKind.Heading ? 18 : 14,
                FontWeight = block.Kind == ComposeBlockKind.Heading ? FontWeights.SemiBold : FontWeights.Normal,
                PlaceholderText = block.Kind == ComposeBlockKind.Heading ? "小标题" : "输入正文，空行分段"
            };
            input.TextChanged += (_, _) => block.Text = input.Text;
            input.GotFocus += (_, _) => _lastComposeTextBox = input;
            if (_lastComposeTextBox is null) _lastComposeTextBox = input;
            stack.Children.Add(input);
        }

        card.Child = stack;
        return card;
    }

    private void MoveComposeBlock(int index, int delta)
    {
        int target = index + delta;
        if (index < 0 || index >= _composeBlocks.Count || target < 0 || target >= _composeBlocks.Count) return;
        ComposeBlock item = _composeBlocks[index];
        _composeBlocks.RemoveAt(index);
        _composeBlocks.Insert(target, item);
        RenderComposeBlocks();
    }

    private async Task PickAndUploadImageAsync(ComposeBlock? target)
    {
        string? path = await _host.Services.PickOpenFileAsync("选择图片", _lifetime.Token);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (_composeStatus is not null) _composeStatus.Text = "正在上传图片…";
        XhhUploadedImage uploaded = await XhhUploader.UploadAsync(_client, path, _lifetime.Token);
        if (target is null)
        {
            target = new ComposeBlock { Kind = ComposeBlockKind.Image };
            _composeBlocks.Add(target);
        }
        target.ImageUrl = uploaded.Url;
        target.LocalPath = path;
        target.Width = uploaded.Width;
        target.Height = uploaded.Height;
        if (_composeStatus is not null) _composeStatus.Text = "图片已上传。";
        RenderComposeBlocks();
    }

    private void RenderComposePreview()
    {
        if (_composePreviewPanel is null || _composePreviewToggle?.IsChecked != true) return;
        _composePreviewPanel.Children.Clear();
        _composePreviewPanel.Children.Add(T(
            ComposeArticleMode
                ? "文章帖预览：按块顺序生成 HTML，图片夹在文字之间。"
                : "图文帖预览：文字与图片作为独立块提交。",
            11, secondary: true, wrap: true));
        foreach (ComposeBlock block in _composeBlocks)
        {
            if (block.Kind == ComposeBlockKind.Image)
            {
                if (string.IsNullOrWhiteSpace(block.ImageUrl)) continue;
                _composePreviewPanel.Children.Add(RemoteImage(block.ImageUrl, 420, 260, 10));
                continue;
            }
            if (string.IsNullOrWhiteSpace(block.Text)) continue;
            Border card = Card(10);
            card.Child = T(block.Text, block.Kind == ComposeBlockKind.Heading ? 20 : 13,
                weight: block.Kind == ComposeBlockKind.Heading ? FontWeights.SemiBold : FontWeights.Normal,
                wrap: true);
            _composePreviewPanel.Children.Add(card);
        }
    }

    private async Task PublishComposeAsync()
    {
        if (_composeTitleBox is null || _composeStatus is null) return;
        string mode = ComposeArticleMode ? "11" : "27";
        string title = _composeTitleBox.Text.Trim();
        string customTopic = _composeCustomTopicBox?.Text.Trim() ?? "";
        string topicId = customTopic.Length > 0 ? customTopic : _composeSelectedTopicId;
        string topicName = customTopic.Length > 0 ? customTopic : _composeSelectedTopicName;
        var textBlocks = _composeBlocks.Where(b => b.Kind != ComposeBlockKind.Image &&
                                                   !string.IsNullOrWhiteSpace(b.Text)).ToList();
        int imageCount = _composeBlocks.Count(b => b.Kind == ComposeBlockKind.Image &&
                                                   !string.IsNullOrWhiteSpace(b.ImageUrl));

        if (title.Length == 0)
        {
            ShowNotice("请填写标题。", InfoBarSeverity.Warning);
            return;
        }
        if (textBlocks.Count == 0 && imageCount == 0)
        {
            ShowNotice("请至少添加一段正文或一张图片。", InfoBarSeverity.Warning);
            return;
        }
        if (title.Length > 30)
        {
            ShowNotice(mode == "11" ? "文章帖标题最多 30 字。" : "图文帖标题最多 30 字。", InfoBarSeverity.Warning);
            return;
        }
        int wordCount = textBlocks.Sum(b => b.Text.Count(c => !char.IsWhiteSpace(c)));
        if (mode == "11" && wordCount < 50)
        {
            ShowNotice("文章帖正文至少 50 字。", InfoBarSeverity.Warning);
            return;
        }
        if (!long.TryParse(topicId, out _))
        {
            ShowNotice("topic_id 必须是数字。", InfoBarSeverity.Warning);
            return;
        }

        bool confirmed = await _host.ConfirmAsync("发布帖子",
            $"将发布到「{topicName}」。\n\n标题：{title}\n正文：{textBlocks.Count} 个文字块 / {imageCount} 张图片\n类型：{(mode == "11" ? "文章帖" : "图文帖")}");
        if (!confirmed) return;

        string textJson = mode == "11" ? BuildArticlePayload() : BuildImageTextPayload();
        string firstText = textBlocks.FirstOrDefault()?.Text ?? title;
        string plain = firstText.ReplaceLineEndings(" ");
        var form = new Dictionary<string, string?>
        {
            ["link_tag"] = mode,
            ["text"] = textJson,
            ["title"] = title,
            ["desc"] = XhhFormat.Clip(plain, 100),
            ["words_count"] = wordCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["topic_ids"] = topicId,
            ["hashtags"] = "[]",
            ["schedule_ts"] = "0",
            ["view_limit"] = "1",
            ["post_type"] = "1",
            ["original"] = "1",
            ["declaration"] = "0"
        };

        JsonElement response = await _client.PublishPostAsync(form, _lifetime.Token);
        string status = response.Str("status");
        if (status == "show_captcha")
            throw new XhhCaptchaException("发布帖子触发了小黑盒风控，请先获取 x_xhh_tokenid。", "/bbs/app/api/link/post");
        if (status != "ok")
        {
            string message = response.FirstStr("msg", "message");
            _composeStatus.Text = string.IsNullOrWhiteSpace(message) ? $"发布失败（status={status}）。" : message;
            ShowNotice(_composeStatus.Text, InfoBarSeverity.Error);
            return;
        }

        string linkId = response.Obj("result").FirstStr("link_id", "linkid");
        if (linkId.Length == 0) linkId = response.FirstStr("link_id", "linkid");
        _mineLoadedSections.Remove("posts");
        _minePosts.Clear();
        _composeTitleBox.Text = "";
        _composeBlocks.Clear();
        _composeBlocks.Add(new ComposeBlock { Kind = ComposeBlockKind.Text });
        RenderComposeBlocks();
        _composeStatus.Text = linkId.Length > 0 ? $"发布成功，帖子 ID：{linkId}" : "发布成功。";
        ShowNotice(linkId.Length > 0 ? $"帖子已发布（ID {linkId}）。" : "帖子已发布。", InfoBarSeverity.Success,
            linkId.Length > 0 ? "打开帖子" : null,
            linkId.Length > 0 ? async () => await OpenPostAsync(new XhhPost { Id = linkId }) : null);
    }

    private string BuildImageTextPayload()
    {
        var blocks = new List<Dictionary<string, object>>();
        foreach (ComposeBlock block in _composeBlocks)
        {
            if (block.Kind == ComposeBlockKind.Image)
            {
                if (string.IsNullOrWhiteSpace(block.ImageUrl)) continue;
                blocks.Add(new Dictionary<string, object>
                {
                    ["type"] = "img",
                    ["url"] = block.ImageUrl,
                    ["width"] = block.Width,
                    ["height"] = block.Height
                });
            }
            else if (!string.IsNullOrWhiteSpace(block.Text))
            {
                blocks.Add(new Dictionary<string, object>
                {
                    ["type"] = "text",
                    ["text"] = block.Text
                });
            }
        }
        return JsonSerializer.Serialize(blocks);
    }

    private string BuildArticlePayload()
    {
        var html = new StringBuilder();
        var imageRefs = new List<Dictionary<string, object>>();
        foreach (ComposeBlock block in _composeBlocks)
        {
            if (block.Kind == ComposeBlockKind.Image)
            {
                if (string.IsNullOrWhiteSpace(block.ImageUrl)) continue;
                html.Append("<p><img data-type=\"image\" data-original=\"")
                    .Append(EscapeHtml(block.ImageUrl))
                    .Append("\" data-width=\"").Append(block.Width)
                    .Append("\" data-height=\"").Append(block.Height)
                    .Append("\" data-description=\"\"></p>");
                imageRefs.Add(new Dictionary<string, object>
                {
                    ["type"] = "img",
                    ["url"] = block.ImageUrl,
                    ["width"] = block.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["height"] = block.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
                continue;
            }
            string text = block.Text.Trim();
            if (text.Length == 0) continue;
            if (block.Kind == ComposeBlockKind.Heading)
            {
                html.Append("<h2>").Append(EscapeHtml(text)).Append("</h2>");
                continue;
            }
            foreach (string paragraph in text.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = paragraph.Trim();
                if (trimmed.Length == 0) continue;
                html.Append("<p>").Append(EscapeHtml(trimmed).Replace("\n", "<br>")).Append("</p>");
            }
        }
        var blocks = new List<Dictionary<string, object>>
        {
            new() { ["type"] = "html", ["text"] = html.ToString() }
        };
        blocks.AddRange(imageRefs);
        return JsonSerializer.Serialize(blocks);
    }

    private static string EscapeHtml(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    private void ClearCompose()
    {
        if (_composeTitleBox is not null) _composeTitleBox.Text = "";
        _composeBlocks.Clear();
        _composeBlocks.Add(new ComposeBlock { Kind = ComposeBlockKind.Text });
        if (_composePreviewToggle is not null) _composePreviewToggle.IsChecked = false;
        RenderComposeBlocks();
        if (_composeStatus is not null) _composeStatus.Text = "已清空。";
    }
}

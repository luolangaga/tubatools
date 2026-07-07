using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed class PcTutorialPage : Page
{
    private readonly Window _window;
    private ListView _navList = null!;
    private ScrollViewer _contentScroll = null!;
    private StackPanel _contentPanel = null!;
    private TextBlock _headerTitle = null!;
    private TextBlock _headerSubtitle = null!;
    private ProgressBar _progressBar = null!;
    private TextBlock _progressText = null!;
    private Border _celebrationOverlay = null!;
    private Border _particleCanvas = null!;
    private int _currentModule = 0;
    private readonly HashSet<string> _readItems = [];
    private readonly HashSet<string> _completedExperiences = [];
    private string _progressKey => "pc-tutorial-read";
    private string _experienceKey => "pc-tutorial-experience";
    private IntPtr _keyboardHook = IntPtr.Zero;
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelKeyboardProc? _hookProc;
    private string? _currentChallengeKey;

    private static readonly Color ModuleColor1 = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color ModuleColor2 = Color.FromArgb(255, 167, 139, 250);
    private static readonly Color ModuleColor3 = Color.FromArgb(255, 251, 191, 36);
    private static readonly Color ModuleColor4 = Color.FromArgb(255, 248, 113, 113);
    private static readonly Color ModuleColor5 = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color ModuleColor6 = Color.FromArgb(255, 244, 114, 182);
    private static readonly Color ModuleColor7 = Color.FromArgb(255, 45, 212, 191);

    private static readonly string[] ModuleColors =
    [
        "#60A5FA", "#A78BFA", "#FBBF24", "#F87171", "#4ADE80", "#F472B6", "#2DD4BF"
    ];

    private static readonly (string Glyph, string Title, string Subtitle, string Type)[] Modules =
    [
        ("\uE777", "新电脑开箱", "分区 · 激活 · 首次设置", "qna"),
        ("\uE7C1", "基础操作", "触控板 · 鼠标 · 快捷键", "guide"),
        ("\uE8F1", "软件管理", "安装 · 卸载 · 避坑", "qna"),
        ("\uED56", "硬件检测", "烤机 · 监控 · 判定", "qna"),
        ("\uE946", "电脑常识", "驱动 · 更新 · 优化", "qna"),
        ("\uE7BA", "辟谣专区", "那些年信过的谎言", "qna"),
    ];

    public PcTutorialPage(Window window)
    {
        _window = window;
        LoadProgress();
        try
        {
            Content = BuildUI();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PcTutorial] BuildUI FAILED: {ex.Message}\n{ex.StackTrace}");
            Content = new TextBlock { Text = $"BuildUI error: {ex.Message}", FontSize = 20 };
            return;
        }
        Loaded += (_, _) =>
        {
            try
            {
                ShowModule(0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PcTutorial] ShowModule(0) FAILED: {ex.Message}\n{ex.StackTrace}");
            }
        };
    }

    private Grid BuildUI()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = BuildSidebar();
        root.Children.Add(sidebar);
        Grid.SetColumn(sidebar, 0);

        var contentArea = BuildContentArea();
        root.Children.Add(contentArea);
        Grid.SetColumn(contentArea, 1);

        _celebrationOverlay = new Border
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "🎉",
                        FontSize = 72,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "太强了！",
                        FontSize = 40,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "你已经掌握了这项操作！",
                        FontSize = 18,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
        root.Children.Add(_celebrationOverlay);
        Grid.SetColumnSpan(_celebrationOverlay, 2);

        _particleCanvas = new Border
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        root.Children.Add(_particleCanvas);
        Grid.SetColumnSpan(_particleCanvas, 2);

        return root;
    }

    private Border BuildSidebar()
    {
        var headerIcon = new FontIcon
        {
            Glyph = "\uE8D7",
            FontSize = 24,
            Foreground = new SolidColorBrush(ThemeColors.AccentBlue)
        };
        var headerTitle = new TextBlock
        {
            Text = "电脑使用教程",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };
        var headerSub = new TextBlock
        {
            Text = "从零开始，玩转电脑",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        var headerStack = new StackPanel { Spacing = 2 };
        headerStack.Children.Add(headerTitle);
        headerStack.Children.Add(headerSub);

        var headerGrid = new Grid { ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var iconBorder = new Border
        {
            Width = 42, Height = 42,
            Background = new SolidColorBrush(Color.FromArgb(30, ThemeColors.AccentBlue.R, ThemeColors.AccentBlue.G, ThemeColors.AccentBlue.B)),
            CornerRadius = new CornerRadius(10),
            Child = headerIcon
        };
        headerGrid.Children.Add(iconBorder);
        headerGrid.Children.Add(headerStack);
        Grid.SetColumn(headerStack, 1);

        _navList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            SelectedIndex = 0,
            Margin = new Thickness(-4, 8, -4, 0)
        };
        _navList.SelectionChanged += (_, _) =>
        {
            if (_navList.SelectedIndex >= 0)
                ShowModule(_navList.SelectedIndex);
        };

        for (var i = 0; i < Modules.Length; i++)
        {
            _navList.Items.Add(BuildNavItem(i));
        }

        _progressBar = new ProgressBar
        {
            Value = 0,
            Minimum = 0,
            Maximum = 100,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 12, 0, 4)
        };

        _progressText = new TextBlock
        {
            Text = "进度 0%",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var sidebar = new StackPanel
        {
            Padding = new Thickness(20, 48, 16, 20),
            Spacing = 4,
            Children =
            {
                headerGrid,
                new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(ThemeColors.Separator),
                    Margin = new Thickness(0, 12, 0, 4)
                },
                _navList,
                new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(ThemeColors.Separator),
                    Margin = new Thickness(0, 8, 0, 4)
                },
                _progressBar,
                _progressText
            }
        };

        return new Border
        {
            Background = new SolidColorBrush(ThemeColors.HeaderBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = sidebar
        };
    }

    private Border BuildNavItem(int index)
    {
        var m = Modules[index];
        var color = HexToColor(ModuleColors[index]);
        var glyphIcon = new FontIcon
        {
            Glyph = m.Glyph,
            FontSize = 16,
            Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var iconBorder = new Border
        {
            Width = 34, Height = 34,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(26, color.R, color.G, color.B)),
            Child = glyphIcon
        };

        var title = new TextBlock
        {
            Text = m.Title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };
        var sub = new TextBlock
        {
            Text = m.Subtitle,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        var textStack = new StackPanel { Spacing = 1 };
        textStack.Children.Add(title);
        textStack.Children.Add(sub);

        var checkIcon = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.AccentGreen),
            Visibility = Visibility.Collapsed
        };

        var grid = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconBorder);
        grid.Children.Add(textStack); Grid.SetColumn(textStack, 1);
        grid.Children.Add(checkIcon); Grid.SetColumn(checkIcon, 2);

        var border = new Border
        {
            Padding = new Thickness(8, 8, 8, 8),
            CornerRadius = new CornerRadius(8),
            Tag = index,
            Child = grid
        };

        border.PointerEntered += (_, _) =>
        {
            border.Background = new SolidColorBrush(ThemeColors.RowHover);
        };
        border.PointerExited += (_, _) =>
        {
            if (_currentModule != index)
                border.Background = new SolidColorBrush(Colors.Transparent);
        };

        return border;
    }

    private Grid BuildContentArea()
    {
        _headerTitle = new TextBlock
        {
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        _headerSubtitle = new TextBlock
        {
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var headerStack = new StackPanel { Spacing = 2 };
        headerStack.Children.Add(_headerTitle);
        headerStack.Children.Add(_headerSubtitle);

        _contentPanel = new StackPanel { Spacing = 12 };

        _contentScroll = new ScrollViewer
        {
            Content = _contentPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 8, 0, 20)
        };

        var grid = new Grid { RowSpacing = 16 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(headerStack); Grid.SetRow(headerStack, 0);
        grid.Children.Add(_contentScroll); Grid.SetRow(_contentScroll, 1);

        return new Grid
        {
            Padding = new Thickness(32, 48, 32, 20),
            Children = { grid }
        };
    }

    private bool _moduleSwitching;

    private void ShowModule(int index)
    {
        if (_moduleSwitching || index < 0 || index >= Modules.Length) return;
        _moduleSwitching = true;
        try
        {
            _currentModule = index;
            if (_navList.SelectedIndex != index)
                _navList.SelectedIndex = index;

            for (var i = 0; i < _navList.Items.Count; i++)
            {
                if (_navList.Items[i] is Border border)
                {
                    border.Background = i == index
                        ? new SolidColorBrush(ThemeColors.RowHover)
                        : new SolidColorBrush(Colors.Transparent);
                }
            }

            var m = Modules[index];
            _headerTitle.Text = m.Title;
            _headerSubtitle.Text = m.Subtitle;

            _contentPanel.Children.Clear();

            switch (index)
            {
                case 0: BuildModule_QnA(GetModule0Data()); break;
                case 1: BuildModule_Guide(); break;
                case 2: BuildModule_QnA(GetModule2Data()); break;
                case 3: BuildModule_QnA(GetModule3Data()); break;
                case 4: BuildModule_QnA(GetModule4Data()); break;
                case 5: BuildModule_QnA(GetModule5Data()); break;
            }

            _contentScroll.ChangeView(null, 0, null, true);
            UpdateProgress();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PcTutorial] ShowModule({index}) FAILED: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            _moduleSwitching = false;
        }
    }

    #region Q&A Module Builder

    private void BuildModule_QnA(List<(string Q, string A, List<ActionLink>? Links)> data)
    {
        var color = HexToColor(ModuleColors[_currentModule]);
        for (var i = 0; i < data.Count; i++)
        {
            var item = data[i];
            var itemId = $"{_currentModule}_{i}";
            var isRead = _readItems.Contains(itemId);

            var card = BuildQnACard(item.Q, item.A, item.Links, color, itemId, isRead, i + 1, data.Count);
            _contentPanel.Children.Add(card);
        }
    }

    private Border BuildQnACard(string question, string answer, List<ActionLink>? links, Color accent, string itemId, bool isRead, int index, int total)
    {
        var isInitiallyExpanded = !isRead;

        var numberBadge = new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(20, accent.R, accent.G, accent.B)),
            Child = new TextBlock
            {
                Text = (index).ToString(),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var qText = new TextBlock
        {
            Text = question,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            TextWrapping = TextWrapping.Wrap
        };

        var readBadge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(15, ThemeColors.AccentGreen.R, ThemeColors.AccentGreen.G, ThemeColors.AccentGreen.B)),
            Padding = new Thickness(8, 2, 8, 2),
            Visibility = isRead ? Visibility.Visible : Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "已读",
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(ThemeColors.AccentGreen)
            }
        };

        var expandIcon = new FontIcon
        {
            Glyph = isInitiallyExpanded ? "\uE70E" : "\uE70D",
            FontSize = 10,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var headerGrid = new Grid { ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(numberBadge);
        headerGrid.Children.Add(qText); Grid.SetColumn(qText, 1);
        headerGrid.Children.Add(readBadge); Grid.SetColumn(readBadge, 2);
        headerGrid.Children.Add(expandIcon); Grid.SetColumn(expandIcon, 3);

        var answerText = new TextBlock
        {
            Text = answer,
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.SecondaryText),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
            IsTextSelectionEnabled = true
        };

        var answerPanel = new StackPanel
        {
            Spacing = 10,
            Visibility = isInitiallyExpanded ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(40, 10, 0, 2)
        };
        answerPanel.Children.Add(answerText);

        if (links?.Count > 0)
        {
            var linkBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 4, 0, 0)
            };
            foreach (var link in links)
            {
                var linkBtn = new HyperlinkButton
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            new FontIcon { Glyph = link.Glyph, FontSize = 11, Foreground = new SolidColorBrush(accent) },
                            new TextBlock { Text = link.Label, FontSize = 12, Foreground = new SolidColorBrush(accent) }
                        }
                    },
                    Tag = link,
                    Padding = new Thickness(8, 2, 8, 2)
                };
                linkBtn.Click += OnActionLinkClick;
                linkBar.Children.Add(linkBtn);
            }
            answerPanel.Children.Add(linkBar);
        }

        var cardStack = new StackPanel { Spacing = 4 };
        cardStack.Children.Add(headerGrid);
        cardStack.Children.Add(answerPanel);

        var border = new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            CornerRadius = new CornerRadius(8),
            Child = cardStack,
            Tag = itemId
        };

        border.PointerEntered += (_, _) =>
        {
            border.Background = new SolidColorBrush(ThemeColors.RowHover);
        };
        border.PointerExited += (_, _) =>
        {
            border.Background = new SolidColorBrush(ThemeColors.CardBg);
        };

        border.Tapped += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement fe && (fe is Button or HyperlinkButton || Ancestors(fe).Any(a => a is Button or HyperlinkButton)))
                return;
            var isExpanded = answerPanel.Visibility == Visibility.Visible;
            if (isExpanded)
            {
                answerPanel.Visibility = Visibility.Collapsed;
                expandIcon.Glyph = "\uE70D";
            }
            else
            {
                answerPanel.Visibility = Visibility.Visible;
                expandIcon.Glyph = "\uE70E";
                if (!_readItems.Contains(itemId))
                {
                    _readItems.Add(itemId);
                    SaveProgress();
                    readBadge.Visibility = Visibility.Visible;
                    UpdateProgress();
                }
            }
        };

        return border;
    }

    #endregion

    #region Guide Module (Module 1 - 触控板/鼠标 引导式交互)

    private void BuildModule_Guide()
    {
        var introCard = BuildIntroCard(
            "基础操作指南",
            "跟着引导一步步操作，每完成一步都会获得确认反馈。像手机教程一样，亲手试过的才记得住！",
            ModuleColors[1]
        );
        _contentPanel.Children.Add(introCard);

        var guides = GetGuideSteps();
        foreach (var guide in guides)
        {
            var card = BuildGuideCard(guide);
            _contentPanel.Children.Add(card);
        }
    }

    private Border BuildGuideCard(GuideStep guide)
    {
        var color = HexToColor(ModuleColors[1]);
        var stepIcon = new FontIcon
        {
            Glyph = guide.Icon,
            FontSize = 20,
            Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var stepIconBorder = new Border
        {
            Width = 44, Height = 44,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(26, color.R, color.G, color.B)),
            Child = stepIcon
        };

        var titleBlock = new TextBlock
        {
            Text = guide.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        var descBlock = new TextBlock
        {
            Text = guide.Description,
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };

        var actionLabel = new TextBlock
        {
            Text = "👉 " + guide.ActionHint,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color),
            TextWrapping = TextWrapping.Wrap
        };

        var checkIcon = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 18,
            Foreground = new SolidColorBrush(ThemeColors.AccentGreen),
            Visibility = Visibility.Collapsed
        };

        var completedText = new TextBlock
        {
            Text = "已完成！",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.AccentGreen),
            Visibility = Visibility.Collapsed
        };

        var statusStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusStack.Children.Add(checkIcon);
        statusStack.Children.Add(completedText);

        var actionBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE768", FontSize = 12 },
                    new TextBlock { Text = guide.ButtonText, FontSize = 12, FontWeight = FontWeights.SemiBold }
                }
            },
            Background = new SolidColorBrush(Color.FromArgb(26, color.R, color.G, color.B)),
            Foreground = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 7, 14, 7),
            Tag = guide
        };

        if (guide.Type == "shortcut")
        {
            var shortcutHint = new Border
            {
                Background = new SolidColorBrush(ThemeColors.SubtleBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 6, 0, 0),
                Child = new TextBlock
                {
                    Text = "⌨ 请按下: " + guide.ShortcutDisplay,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                    FontFamily = new FontFamily("Cascadia Code, Consolas")
                }
            };

            var contentStack = new StackPanel { Spacing = 6 };
            contentStack.Children.Add(titleBlock);
            contentStack.Children.Add(descBlock);
            contentStack.Children.Add(actionLabel);
            contentStack.Children.Add(shortcutHint);

            var statusRow = new Grid
            {
                ColumnSpacing = 12,
                Margin = new Thickness(0, 8, 0, 0)
            };
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusRow.Children.Add(contentStack);
            statusRow.Children.Add(statusStack); Grid.SetColumn(statusStack, 1);
            statusRow.Children.Add(actionBtn); Grid.SetColumn(actionBtn, 2);

            var topRow = new Grid { ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Top };
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.Children.Add(stepIconBorder);
            topRow.Children.Add(statusRow); Grid.SetColumn(statusRow, 1);

            var card = new Border
            {
                Padding = new Thickness(18, 16, 18, 16),
                Background = new SolidColorBrush(ThemeColors.CardBg),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = topRow,
                Tag = guide.Id
            };

            actionBtn.Click += (_, _) => StartShortcutDetection(guide, shortcutHint, checkIcon, completedText, actionBtn, card);
            return card;
        }
        else
        {
            var contentStack = new StackPanel { Spacing = 6 };
            contentStack.Children.Add(titleBlock);
            contentStack.Children.Add(descBlock);
            contentStack.Children.Add(actionLabel);

            var statusRow = new Grid
            {
                ColumnSpacing = 12,
                Margin = new Thickness(0, 8, 0, 0)
            };
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusRow.Children.Add(contentStack);
            statusRow.Children.Add(statusStack); Grid.SetColumn(statusStack, 1);
            statusRow.Children.Add(actionBtn); Grid.SetColumn(actionBtn, 2);

            var topRow = new Grid { ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Top };
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.Children.Add(stepIconBorder);
            topRow.Children.Add(statusRow); Grid.SetColumn(statusRow, 1);

            var card = new Border
            {
                Padding = new Thickness(18, 16, 18, 16),
                Background = new SolidColorBrush(ThemeColors.CardBg),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = topRow,
                Tag = guide.Id
            };

            actionBtn.Click += (_, _) => ExecuteGuideAction(guide, checkIcon, completedText, actionBtn, card);
            return card;
        }
    }

    private void StartShortcutDetection(GuideStep guide, Border shortcutHint, FontIcon checkIcon, TextBlock completedText, Button actionBtn, Border card)
    {
        if (_completedExperiences.Contains(guide.Id))
        {
            ShowCelebration("又来一次？记住了！");
            return;
        }

        actionBtn.IsEnabled = false;
        shortcutHint.Background = new SolidColorBrush(Color.FromArgb(40, ThemeColors.AccentBlue.R, ThemeColors.AccentBlue.G, ThemeColors.AccentBlue.B));
        ((TextBlock)shortcutHint.Child).Text = "🎧 监听中... 请按下: " + guide.ShortcutDisplay;
        ((TextBlock)shortcutHint.Child).Foreground = new SolidColorBrush(ThemeColors.AccentBlue);

        InstallKeyboardHook(guide, checkIcon, completedText, actionBtn, card, shortcutHint);
    }

    private void InstallKeyboardHook(GuideStep guide, FontIcon checkIcon, TextBlock completedText, Button actionBtn, Border card, Border shortcutHint)
    {
        _currentChallengeKey = guide.ShortcutKey;

        _hookProc = (nCode, wParam, lParam) =>
        {
            if (nCode >= 0 && (wParam == (IntPtr)0x0100 || wParam == (IntPtr)0x0104))
            {
                var vkCode = Marshal.ReadInt32(lParam);
                _ = _window.DispatcherQueue.TryEnqueue(() =>
                {
                    var pressed = KeyToString(vkCode);
                    var target = guide.ShortcutKey;

                    if (IsShortcutMatch(vkCode, target))
                    {
                        _completedExperiences.Add(guide.Id);
                        SaveProgress();

                        _window.DispatcherQueue.TryEnqueue(() =>
                        {
                            UninstallKeyboardHook();
                            checkIcon.Visibility = Visibility.Visible;
                            completedText.Visibility = Visibility.Visible;
                            actionBtn.Content = new TextBlock { Text = "✓ 已掌握", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ThemeColors.AccentGreen) };

                            shortcutHint.Background = new SolidColorBrush(Color.FromArgb(30, ThemeColors.AccentGreen.R, ThemeColors.AccentGreen.G, ThemeColors.AccentGreen.B));
                            ((TextBlock)shortcutHint.Child).Text = "✓ 成功按下: " + guide.ShortcutDisplay;
                            ((TextBlock)shortcutHint.Child).Foreground = new SolidColorBrush(ThemeColors.AccentGreen);

                            AnimateCardSuccess(card);
                            ShowCelebration(guide.SuccessMessage);
                            UpdateProgress();
                        });
                    }
                });
            }
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        };

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        _keyboardHook = SetWindowsHookEx(0x000D, _hookProc, GetModuleHandle(module?.ModuleName), 0);
    }

    private void UninstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        _hookProc = null;
        _currentChallengeKey = null;
    }

    private static bool IsShortcutMatch(int vkCode, string shortcutKey)
    {
        return shortcutKey switch
        {
            "Win+D" => vkCode == 0x44 && IsKeyPressed(0x5B),
            "Win+E" => vkCode == 0x45 && IsKeyPressed(0x5B),
            "Win+L" => vkCode == 0x4C && IsKeyPressed(0x5B),
            "Alt+Tab" => vkCode == 0x09 && IsKeyPressed(0x12),
            "Ctrl+Shift+Esc" => vkCode == 0x1B && IsKeyPressed(0x10) && IsKeyPressed(0x11),
            "Win+V" => vkCode == 0x56 && IsKeyPressed(0x5B),
            "Win+Shift+S" => vkCode == 0x53 && IsKeyPressed(0x5B) && IsKeyPressed(0x10),
            "Ctrl+W" => vkCode == 0x57 && IsKeyPressed(0x11),
            _ => false
        };
    }

    private static bool IsKeyPressed(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static string KeyToString(int vk) => vk switch
    {
        0x5B or 0x5C => "Win",
        0x11 => "Ctrl",
        0x12 => "Alt",
        0x10 => "Shift",
        0x09 => "Tab",
        0x1B => "Esc",
        0x44 => "D",
        0x45 => "E",
        0x4C => "L",
        0x56 => "V",
        0x53 => "S",
        0x57 => "W",
        _ => $"0x{vk:X2}"
    };

    private void ExecuteGuideAction(GuideStep guide, FontIcon checkIcon, TextBlock completedText, Button actionBtn, Border card)
    {
        try
        {
            switch (guide.Action)
            {
                case "open_taskmgr":
                    Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                    break;
                case "open_diskmgmt":
                    Process.Start(new ProcessStartInfo("diskmgmt.msc") { UseShellExecute = true });
                    break;
                case "open_settings":
                    Process.Start(new ProcessStartInfo("ms-settings:clipboard") { UseShellExecute = true });
                    break;
                case "open_touchpad":
                    Process.Start(new ProcessStartInfo("ms-settings:devices-touchpad") { UseShellExecute = true });
                    break;
                case "open_mouse":
                    Process.Start(new ProcessStartInfo("ms-settings:mouse") { UseShellExecute = true });
                    break;
                case "open_powershell":
                    Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true });
                    break;
                case "open_hardware":
                    NavigateMainWindow("HardwarePage");
                    break;
                case "open_monitor":
                    NavigateMainWindow("HardwarePage");
                    break;
                case "open_burncpu":
                    {
                        var builtinTool = BuiltinToolRegistry.GetById("cpu-burn");
                        if (builtinTool is not null && XamlRoot is not null)
                        {
                            var ctx = new BuiltinToolContext { XamlRoot = XamlRoot };
                            _ = builtinTool.ExecuteAsync(ctx);
                        }
                    }
                    break;
                case "open_burngpu":
                    LaunchToolByExeName("FurMark");
                    break;
                case "open_winget":
                    {
                        var builtinTool = BuiltinToolRegistry.GetById("winget-installer");
                        if (builtinTool is not null && XamlRoot is not null)
                        {
                            var ctx = new BuiltinToolContext { XamlRoot = XamlRoot };
                            _ = builtinTool.ExecuteAsync(ctx);
                        }
                        break;
                    }
                case "open_junkclean":
                    {
                        var builtinTool = BuiltinToolRegistry.GetById("junk-cleaner");
                        if (builtinTool is not null && XamlRoot is not null)
                        {
                            var ctx = new BuiltinToolContext { XamlRoot = XamlRoot };
                            _ = builtinTool.ExecuteAsync(ctx);
                        }
                        break;
                    }
            }

            _completedExperiences.Add(guide.Id);
            SaveProgress();

            checkIcon.Visibility = Visibility.Visible;
            completedText.Visibility = Visibility.Visible;
            actionBtn.Content = new TextBlock { Text = "✓ 已完成", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ThemeColors.AccentGreen) };
            actionBtn.IsEnabled = false;

            AnimateCardSuccess(card);
            ShowCelebration(guide.SuccessMessage);
            UpdateProgress();
        }
        catch
        {
            checkIcon.Visibility = Visibility.Visible;
            completedText.Text = "已尝试打开";
            completedText.Foreground = new SolidColorBrush(ThemeColors.AccentOrange);
            completedText.Visibility = Visibility.Visible;
        }
    }

    #endregion

    #region Intro Card

    private Border BuildIntroCard(string title, string desc, string hexColor)
    {
        var color = HexToColor(hexColor);

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        var descBlock = new TextBlock
        {
            Text = desc,
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.SecondaryText),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(titleBlock);
        stack.Children.Add(descBlock);

        return new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            Background = new SolidColorBrush(Color.FromArgb(8, color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(8),
            Child = stack
        };
    }

    #endregion

    #region Celebration & Animations

    private void ShowCelebration(string message)
    {
        try
        {
            var stack = (StackPanel)_celebrationOverlay.Child;
            if (stack is null) return;
            stack.Children.Clear();

            var emoji = new TextBlock
            {
                Text = RandomEmoji(),
                FontSize = 72,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var titleBlock = new TextBlock
            {
                Text = RandomPraise(),
                FontSize = 40,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var subBlock = new TextBlock
            {
                Text = message,
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            stack.Children.Add(emoji);
            stack.Children.Add(titleBlock);
            stack.Children.Add(subBlock);

            _celebrationOverlay.Visibility = Visibility.Visible;
            _celebrationOverlay.Opacity = 0;

            var overlayVisual = ElementCompositionPreview.GetElementVisual(_celebrationOverlay);
            var compositor = overlayVisual.Compositor;
            var fadeIn = compositor.CreateScalarKeyFrameAnimation();
            fadeIn.InsertKeyFrame(0f, 0f);
            fadeIn.InsertKeyFrame(1f, 1f);
            fadeIn.Duration = TimeSpan.FromMilliseconds(300);
            overlayVisual.StartAnimation("Opacity", fadeIn);

            var stackVisual = ElementCompositionPreview.GetElementVisual(stack);
            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.InsertKeyFrame(0f, new System.Numerics.Vector3(0.5f, 0.5f, 1f));
            scaleAnim.InsertKeyFrame(0.6f, new System.Numerics.Vector3(1.1f, 1.1f, 1f));
            scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f));
            scaleAnim.Duration = TimeSpan.FromMilliseconds(500);
            stackVisual.StartAnimation("Scale", scaleAnim);

            SpawnParticles();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var fadeOut = compositor.CreateScalarKeyFrameAnimation();
                fadeOut.InsertKeyFrame(0f, 1f);
                fadeOut.InsertKeyFrame(1f, 0f);
                fadeOut.Duration = TimeSpan.FromMilliseconds(400);
                overlayVisual.StartAnimation("Opacity", fadeOut);

                var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                hideTimer.Tick += (_, _) =>
                {
                    hideTimer.Stop();
                    _celebrationOverlay.Visibility = Visibility.Collapsed;
                    _particleCanvas.Visibility = Visibility.Collapsed;
                    _particleCanvas.Child = null;
                };
                hideTimer.Start();
            };
            timer.Start();
        }
        catch { }
    }

    private void SpawnParticles()
    {
        try
        {
            _particleCanvas.Visibility = Visibility.Visible;
            _particleCanvas.Child = null;

            var canvas = new Canvas();
            _particleCanvas.Child = canvas;

            var rnd = new Random();
            var colors = new[] { ThemeColors.AccentBlue, ThemeColors.AccentGreen, ThemeColors.AccentOrange, ThemeColors.AccentPurple, ThemeColors.AccentRed, Color.FromArgb(255, 244, 114, 182), Color.FromArgb(255, 45, 212, 191) };

            var cx = _particleCanvas.ActualSize.X > 0 ? _particleCanvas.ActualSize.X / 2 : 400;
            var cy = _particleCanvas.ActualSize.Y > 0 ? _particleCanvas.ActualSize.Y / 2 : 300;

            for (var i = 0; i < 40; i++)
            {
                var size = rnd.Next(6, 16);
                var color = colors[rnd.Next(colors.Length)];
                var ellipse = new Border
                {
                    Width = size,
                    Height = size,
                    CornerRadius = new CornerRadius(size / 2.0),
                    Background = new SolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B)),
                    RenderTransform = new CompositeTransform
                    {
                        TranslateX = rnd.Next(-400, 400),
                        TranslateY = rnd.Next(-200, 200),
                        ScaleX = 0,
                        ScaleY = 0
                    }
                };
                Canvas.SetLeft(ellipse, cx);
                Canvas.SetTop(ellipse, cy);
                canvas.Children.Add(ellipse);

                var targetX = rnd.Next(-500, 500);
                var targetY = rnd.Next(-400, 100);
                var delay = rnd.Next(0, 300);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
                var capturedEllipse = ellipse;
                var capturedTargetX = targetX;
                var capturedTargetY = targetY;
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        var ev = ElementCompositionPreview.GetElementVisual(capturedEllipse);
                        var compositor = ev.Compositor;

                        var scaleUp = compositor.CreateVector3KeyFrameAnimation();
                        scaleUp.InsertKeyFrame(0f, new System.Numerics.Vector3(0f, 0f, 1f));
                        scaleUp.InsertKeyFrame(0.4f, new System.Numerics.Vector3(1.5f, 1.5f, 1f));
                        scaleUp.InsertKeyFrame(1f, new System.Numerics.Vector3(0f, 0f, 1f));
                        scaleUp.Duration = TimeSpan.FromMilliseconds(800 + rnd.Next(200));
                        ev.StartAnimation("Scale", scaleUp);

                        var offset = compositor.CreateVector3KeyFrameAnimation();
                        offset.InsertKeyFrame(0f, new System.Numerics.Vector3(0f, 0f, 0f));
                        offset.InsertKeyFrame(1f, new System.Numerics.Vector3(capturedTargetX, capturedTargetY, 0f));
                        offset.Duration = TimeSpan.FromMilliseconds(900 + rnd.Next(200));
                        ev.StartAnimation("Offset", offset);
                    }
                    catch { }
                };
                timer.Start();
            }
        }
        catch { }
    }

    private void AnimateCardSuccess(Border card)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(card);
            var compositor = visual.Compositor;

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.InsertKeyFrame(0f, new System.Numerics.Vector3(1f, 1f, 1f));
            scaleAnim.InsertKeyFrame(0.3f, new System.Numerics.Vector3(1.03f, 1.03f, 1f));
            scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f));
            scaleAnim.Duration = TimeSpan.FromMilliseconds(400);
            visual.StartAnimation("Scale", scaleAnim);

            card.BorderBrush = new SolidColorBrush(Color.FromArgb(100, ThemeColors.AccentGreen.R, ThemeColors.AccentGreen.G, ThemeColors.AccentGreen.B));

            var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            resetTimer.Tick += (_, _) =>
            {
                resetTimer.Stop();
                card.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);
            };
            resetTimer.Start();
        }
        catch { }
    }

    private static string RandomPraise() =>
        new[] { "太强了！", "完美！", "牛啊！", "高手！", "绝了！", "漂亮！", "帅炸！", "666！" }[Random.Shared.Next(8)];

    private static string RandomEmoji() =>
        new[] { "🎉", "✨", "🔥", "💪", "🏆", "⭐", "🎯", "👑" }[Random.Shared.Next(8)];

    #endregion

    #region Action Links

    private void OnActionLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button and not HyperlinkButton) return;
        var btn = (FrameworkElement)sender;
        if (btn.Tag is not ActionLink link) return;
        try
        {
            switch (link.Action)
            {
                case "launch_tool":
                    LaunchToolByName(link.Target);
                    break;
                case "launch_exe":
                    LaunchToolByExeName(link.Target);
                    break;
                case "launch_builtin":
                    var builtin = BuiltinToolRegistry.GetById(link.Target);
                    if (builtin is not null && XamlRoot is not null)
                    {
                        var ctx = new BuiltinToolContext { XamlRoot = XamlRoot };
                        _ = builtin.ExecuteAsync(ctx);
                    }
                    break;
                case "open_system":
                    Process.Start(new ProcessStartInfo(link.Target) { UseShellExecute = true });
                    break;
                case "navigate_page":
                    NavigateMainWindow(link.Target);
                    break;
            }
        }
        catch { }
    }

    private static void NavigateMainWindow(string pageTarget)
    {
        var pageType = pageTarget switch
        {
            "HardwarePage" => typeof(HardwarePage),
            "LiteMonitorPage" => typeof(HardwarePage),
            "BuiltinToolsPage" => typeof(BuiltinToolsPage),
            _ => typeof(HomePage)
        };
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                App.MainWindow?.Activate();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                SetForegroundWindow(hwnd);
            }
            catch { }
            try
            {
                var navFrame = (App.MainWindow as MainWindow)?.NavigationFrame;
                if (navFrame is not null)
                    navFrame.Navigate(pageType);
            }
            catch { }
        });
    }

    private static void LaunchToolByName(string name)
    {
        var allTools = ToolCatalog.GetAllToolsCached();
        var tool = allTools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        if (tool is null)
            tool = allTools.FirstOrDefault(t => t.Name.Contains(name, StringComparison.CurrentCultureIgnoreCase));
        if (tool is null)
            tool = allTools.FirstOrDefault(t => t.Path.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
            tool = allTools.FirstOrDefault(t => t.EffectivePath.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (tool is not null)
        {
            var exePath = tool.EffectivePath;
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = tool.EffectiveWorkingDir,
                    UseShellExecute = true
                });
                LaunchHistoryService.RecordLaunch(tool.Path);
            }
        }
    }

    private static void LaunchToolByExeName(string exePartialName)
    {
        var allTools = ToolCatalog.GetAllToolsCached();
        var tool = allTools.FirstOrDefault(t =>
        {
            var p = t.EffectivePath;
            return !string.IsNullOrEmpty(p) && System.IO.Path.GetFileName(p).Contains(exePartialName, StringComparison.OrdinalIgnoreCase);
        });
        if (tool is not null && File.Exists(tool.EffectivePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = tool.EffectivePath,
                WorkingDirectory = tool.EffectiveWorkingDir,
                UseShellExecute = true
            });
            LaunchHistoryService.RecordLaunch(tool.Path);
            return;
        }
        var toolsRoot = ToolCatalog.ToolsRoot;
        if (Directory.Exists(toolsRoot))
        {
            var matches = Directory.EnumerateFiles(toolsRoot, "*", SearchOption.AllDirectories)
                .Where(f => System.IO.Path.GetFileName(f).Contains(exePartialName, StringComparison.OrdinalIgnoreCase) && IsLaunchableExe(f))
                .ToList();
            var pick = matches.FirstOrDefault(f => f.Contains("x64", StringComparison.OrdinalIgnoreCase) || f.Contains("64", StringComparison.OrdinalIgnoreCase) || f.Contains("win64", StringComparison.OrdinalIgnoreCase))
                       ?? matches.FirstOrDefault();
            if (pick is not null && File.Exists(pick))
            {
                Process.Start(new ProcessStartInfo(pick) { UseShellExecute = true });
            }
        }
    }

    private static bool IsLaunchableExe(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".bat" or ".cmd" or ".msc";
    }

    #endregion

    #region Progress

    private void LoadProgress()
    {
        var readStr = AppSettings.Get(_progressKey);
        if (!string.IsNullOrEmpty(readStr))
        {
            foreach (var item in readStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
                _readItems.Add(item.Trim());
        }

        var expStr = AppSettings.Get(_experienceKey);
        if (!string.IsNullOrEmpty(expStr))
        {
            foreach (var item in expStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
                _completedExperiences.Add(item.Trim());
        }
    }

    private void SaveProgress()
    {
        AppSettings.Set(_progressKey, string.Join(",", _readItems));
        AppSettings.Set(_experienceKey, string.Join(",", _completedExperiences));
    }

    private void UpdateProgress()
    {
        try
        {
            var totalItems = 0;
            var doneItems = 0;

            var qnaModules = new[] { 0, 2, 3, 4, 5 };
            foreach (var m in qnaModules)
            {
                var data = m switch
                {
                    0 => GetModule0Data(),
                    2 => GetModule2Data(),
                    3 => GetModule3Data(),
                    4 => GetModule4Data(),
                    5 => GetModule5Data(),
                    _ => []
                };
                for (var i = 0; i < data.Count; i++)
                {
                    totalItems++;
                    if (_readItems.Contains($"{m}_{i}")) doneItems++;
                }
            }

            var guideSteps = GetGuideSteps();
            foreach (var step in guideSteps)
            {
                totalItems++;
                if (_completedExperiences.Contains(step.Id)) doneItems++;
            }

            var pct = totalItems > 0 ? (int)(doneItems * 100.0 / totalItems) : 0;
            _progressBar.Value = pct;
            _progressText.Text = $"进度 {pct}%（{doneItems}/{totalItems}）";

            for (var i = 0; i < _navList.Items.Count; i++)
            {
                if (_navList.Items[i] is Border border && border.Child is Grid grid)
                {
                    var checkIcon = grid.Children.OfType<FontIcon>().LastOrDefault();
                    if (checkIcon is not null)
                    {
                        bool moduleDone = IsModuleComplete(i);
                        checkIcon.Visibility = moduleDone ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
        }
        catch { }
    }

    private bool IsModuleComplete(int moduleIndex)
    {
        return moduleIndex switch
        {
            0 => GetModule0Data().Count > 0 && GetModule0Data().Select((_, i) => $"{0}_{i}").All(id => _readItems.Contains(id)),
            1 => GetGuideSteps().All(s => _completedExperiences.Contains(s.Id)),
            2 => GetModule2Data().Count > 0 && GetModule2Data().Select((_, i) => $"{2}_{i}").All(id => _readItems.Contains(id)),
            3 => GetModule3Data().Count > 0 && GetModule3Data().Select((_, i) => $"{3}_{i}").All(id => _readItems.Contains(id)),
            4 => GetModule4Data().Count > 0 && GetModule4Data().Select((_, i) => $"{4}_{i}").All(id => _readItems.Contains(id)),
            5 => GetModule5Data().Count > 0 && GetModule5Data().Select((_, i) => $"{5}_{i}").All(id => _readItems.Contains(id)),
            _ => false
        };
    }

    #endregion

    #region Tutorial Data

    private static List<(string Q, string A, List<ActionLink>? Links)> GetModule0Data() =>
    [
        ("新电脑需要分区吗？",
         "大多数用户不需要分区。Windows 10/11 的文件管理和搜索已足够强大，分区反而浪费 SSD 空间（每个分区会预留备用空间）。但如果你习惯将数据与系统分开，可以分两个区。注意：C 盘建议至少留 150GB 以上空间。",
         null),

        ("SSD 和 HDD 怎么分区？",
         "纯 SSD 建议 1~2 个分区即可。如果是 SSD + HDD 双盘，SSD 做系统盘不分区，HDD 可按需分 2~3 个区（如 D 盘软件、E 盘数据）。分区时注意 4K 对齐（Windows 磁盘管理默认已对齐）。",
         [new ActionLink("打开磁盘管理", "\uEDA7", "open_system", "diskmgmt.msc")]),

        ("新电脑第一次开机要做什么？",
         "①跳过捆绑软件（开箱时厂商预装的可选卸载）②连接网络完成系统更新 ③安装显卡驱动 ④激活 Windows ⑤安装常用软件。图吧工具箱里就能帮你完成驱动和硬件检测！",
         [new ActionLink("查看硬件信息", "\uE950", "navigate_page", "HardwarePage")]),

        ("怎么激活 Windows？",
         "正版激活：①品牌机一般已内置数字许可证，联网自动激活 ②零售密钥在「设置→系统→激活」中输入。工具箱也提供了 KMS 激活工具，但建议优先使用正版授权。",
         [new ActionLink("KMS 激活工具", "\uEC19", "launch_builtin", "windows-activation")])
    ];

    private static List<GuideStep> GetGuideSteps() =>
    [
        new("guide_touchpad", "\uE7C1", "触控板手势", "Windows 精密触控板支持丰富的多点手势操作，掌握它们让你的效率翻倍。", "请在系统设置中查看和自定义你的触控板手势", "touchpad", "打开触控板设置", "触控板手势，拿下！", "\uE7C1")
        {
            Action = "open_touchpad"
        },

        new("guide_mouse", "\uE962", "鼠标技巧", "①滚轮点击链接 = 新标签页打开 ②Ctrl+滚轮 = 缩放页面 ③双击标题栏 = 最大化/还原。建议关闭「增强指针精确度」（实际是鼠标加速，影响瞄准）。", "打开鼠标设置，调整适合你的速度", "mouse", "打开鼠标设置", "鼠标调教完毕！", "\uE962")
        {
            Action = "open_mouse"
        },

        new("guide_win_d", "\uE768", "显示桌面：Win + D", "一键最小化所有窗口显示桌面，再按一次恢复。找文件、看壁纸必备！", "请按下 Win + D 显示桌面", "shortcut", "开始检测", "Win+D 掌握了！", "\uE768")
        {
            ShortcutKey = "Win+D", ShortcutDisplay = "Win + D", Type = "shortcut"
        },

        new("guide_win_e", "\uEDA7", "资源管理器：Win + E", "快速打开文件资源管理器，比从桌面找图标快多了。", "请按下 Win + E 打开资源管理器", "shortcut", "开始检测", "Win+E 信手拈来！", "\uEDA7")
        {
            ShortcutKey = "Win+E", ShortcutDisplay = "Win + E", Type = "shortcut"
        },

        new("guide_alt_tab", "\uE7F4", "切换窗口：Alt + Tab", "在打开的窗口间快速切换，按住 Alt 连续按 Tab 选择目标窗口。", "请按下 Alt + Tab 切换窗口", "shortcut", "开始检测", "Alt+Tab 切换自如！", "\uE7F4")
        {
            ShortcutKey = "Alt+Tab", ShortcutDisplay = "Alt + Tab", Type = "shortcut"
        },

        new("guide_taskmgr", "\uE9D9", "任务管理器：Ctrl+Shift+Esc", "电脑卡顿时第一时间打开任务管理器，结束卡死进程。比 Ctrl+Alt+Del 更快更直接！", "请按下 Ctrl + Shift + Esc 打开任务管理器", "shortcut", "开始检测", "任务管理器手到擒来！", "\uE9D9")
        {
            ShortcutKey = "Ctrl+Shift+Esc", ShortcutDisplay = "Ctrl + Shift + Esc", Type = "shortcut"
        },

        new("guide_win_v", "\uE8C9", "剪贴板历史：Win + V", "Windows 自带剪贴板历史记录功能，可以粘贴之前复制过的内容。比只能粘贴最后一次方便太多了！", "请按下 Win + V 打开剪贴板历史", "shortcut", "开始检测", "剪贴板历史？小意思！", "\uE8C9")
        {
            ShortcutKey = "Win+V", ShortcutDisplay = "Win + V", Type = "shortcut"
        },

        new("guide_screenshot", "\uE722", "截图：Win + Shift + S", "系统自带截图工具，可以截取任意区域、窗口或全屏。截图后自动复制到剪贴板，Win+V 即可粘贴。", "请按下 Win + Shift + S 截图", "shortcut", "开始检测", "截图？轻轻松松！", "\uE722")
        {
            ShortcutKey = "Win+Shift+S", ShortcutDisplay = "Win + Shift + S", Type = "shortcut"
        },

        new("guide_ctrl_w", "\uE711", "关闭标签：Ctrl + W", "在浏览器、资源管理器等应用中，Ctrl+W 关闭当前标签页。比找关闭按钮快得多！", "请按下 Ctrl + W 关闭当前标签", "shortcut", "开始检测", "关闭标签，干脆利落！", "\uE711")
        {
            ShortcutKey = "Ctrl+W", ShortcutDisplay = "Ctrl + W", Type = "shortcut"
        }
    ];

    private static List<(string Q, string A, List<ActionLink>? Links)> GetModule2Data() =>
    [
        ("安装软件推荐什么渠道？",
         "三大安全渠道，按推荐顺序：\n\n① Microsoft Store（微软商店）——最安全，UWP 应用沙盒运行，自动更新，无捆绑。打开方式：开始菜单搜索「Store」或「微软商店」\n② winget —— Windows 自带包管理器，命令行一键安装，干净无捆绑，适合常用软件批量装\n③ 官网下载 —— 最灵活但需辨别真假官网，认准官网域名，远离下载站\n\n不推荐：各种软件管家、应用市场、第三方下载站，它们本身就是捆绑软件的来源！",
         [new ActionLink("打开微软商店", "\uE719", "open_system", "ms-windows-store://home"), new ActionLink("UniGetUI 包管理器", "\uE8F1", "launch_builtin", "winget-installer")]),

        ("winget 是什么？怎么用？",
         "winget 是 Windows 自带的包管理器，类似手机的应用商店，但是命令行版的。优点：干净、无捆绑、一键安装卸载、自动更新。\n\n基本用法（在 PowerShell 或终端中输入）：\n\n• 搜索软件：winget search 关键词\n  例：winget search chrome\n\n• 安装软件：winget install 软件ID\n  例：winget install Google.Chrome\n\n• 查看已装：winget list\n\n• 更新软件：winget upgrade\n\n• 卸载软件：winget uninstall 软件ID\n\n第一次用 winget 可能会问是否同意协议，输入 Y 回车即可。如果提示「winget」不是命令，需要从微软商店安装「应用安装程序」。",
         [new ActionLink("打开 PowerShell 试试", "\uE756", "open_system", "powershell"), new ActionLink("UniGetUI 包管理器", "\uE8F1", "launch_builtin", "winget-installer")]),

        ("winget 实操：安装常用软件",
         "打开 PowerShell（右键开始菜单 → 终端/PowerShell），然后逐行输入以下命令：\n\n💡 安装浏览器：\n  winget install Google.Chrome\n  winget install Mozilla.Firefox\n\n💡 安装通讯工具：\n  winget install Tencent.WeChat\n  winget install Tencent.QQ\n\n💡 安装效率工具：\n  winget install Notion.Notion\n  winget install Obsidian.Obsidian\n\n💡 安装开发工具：\n  winget install Microsoft.VisualStudioCode\n  winget install Git.Git\n\n每条命令会自动下载安装，不用手动点下一步！安装完成后在开始菜单就能找到。\n\n如果命令行太麻烦，工具箱内置了 UniGetUI 包管理器，图形界面操作更直观！",
         [new ActionLink("打开 PowerShell", "\uE756", "open_system", "powershell"), new ActionLink("UniGetUI 包管理器", "\uE8F1", "launch_builtin", "winget-installer")]),

        ("怎么正确卸载软件？",
         "推荐方式（按优先级）：\n\n① 设置卸载：Win+I 打开设置 → 应用 → 已安装的应用 → 找到软件点卸载\n② winget 卸载：winget uninstall 软件ID（命令行一键搞定）\n③ 顽固软件：用 HiBit Uninstaller 强制卸载并清理残留，工具箱里就有！\n\n注意：不要直接删除文件夹！那样注册表和启动项会残留。卸载后建议检查任务管理器→启动，关掉不需要的自启项。",
         [new ActionLink("打开应用设置", "\uE713", "open_system", "ms-settings:appsfeatures"), new ActionLink("HiBit Uninstaller", "\uE74D", "launch_exe", "HiBitUninstaller")]),

        ("什么是捆绑安装？怎么避免？",
         "捆绑安装是安装一个软件时偷偷装上其他不需要的软件。识别和避免方法：\n\n① 用微软商店或 winget 安装 —— 永远不会有捆绑\n② 官网安装时选「自定义安装」—— 仔细看每一步\n③ 取消勾选「推荐安装 xxx」「设为首页」等选项\n④ 特别小心下载站的「高速下载」按钮 —— 那通常是捆绑器！点「普通下载」或「官方下载」\n⑤ 安装完成后检查桌面和开始菜单是否多了不认识的图标",
         [new ActionLink("打开微软商店", "\uE719", "open_system", "ms-windows-store://home"), new ActionLink("UniGetUI 包管理器", "\uE8F1", "launch_builtin", "winget-installer")]),

        ("软件装在 C 盘还是 D 盘？",
         "SSD 时代建议默认装 C 盘。原因：\n\n① SSD 随机读写快，装哪都一样快\n② C 盘装软件启动更快（系统盘 I/O 优先级高）\n③ 默认路径不容易出兼容性问题\n④ 只有大型游戏可以装 D 盘节省空间\n\nC 盘只要留 30GB+ 剩余空间就不会卡。如果 C 盘空间紧张，用工具箱的垃圾清理功能清理一下！",
         [new ActionLink("垃圾清理", "\uE74D", "launch_builtin", "junk-cleaner")])
    ];

    private static List<(string Q, string A, List<ActionLink>? Links)> GetModule3Data() =>
    [
        ("为什么要烤机？",
         "烤机（压力测试）是在极限负载下运行 CPU/GPU，目的是：①验证新电脑硬件是否稳定（是否有暗病）②检查散热是否合格（温度是否过高）③确认供电是否足够（是否掉电降频）④新电脑建议至少烤 15 分钟，无蓝屏死机才算通过。图吧工具箱里就有烤机工具！",
          [new ActionLink("CPU 烤鸡", "\uED56", "launch_builtin", "cpu-burn"), new ActionLink("硬件监控", "\uE9D9", "navigate_page", "HardwarePage")]),

        ("CPU 怎么烤机？",
         "①打开工具箱里的 FPU 压力测试工具 ②也可以用 AIDA64 → 工具 → 系统稳定性测试，勾选 Stress FPU ③运行 15~30 分钟，观察温度和频率 ④正常温度应在 95°C 以下，频率不应大幅下降。工具箱还内置了 CPU 烤鸡功能！",
          [new ActionLink("CPU 烤鸡", "\uED56", "launch_builtin", "cpu-burn"), new ActionLink("GPU 烤鸡", "\uEDA7", "launch_exe", "FurMark")]),

        ("显卡怎么烤机？",
         "①用 FurMark（俗称「甜甜圈」）进行 GPU 压力测试 ②运行 15~30 分钟 ③正常温度：笔记本 90°C 以下，台式机 85°C 以下 ④如果花屏、黑屏或温度超过 95°C，说明显卡有问题。工具箱里有显卡工具分类！",
          [new ActionLink("FurMark 烤鸡", "\uE721", "launch_exe", "FurMark"), new ActionLink("GPU 天梯图", "\uEEA1", "launch_builtin", "gpu-ranking")]),

        ("烤机时看什么指标？",
         "重点关注：①温度——CPU/GPU 不超 95°C ②频率——不应大幅降频（如 i7 从 4.5GHz 降到 2GHz 就不正常）③功耗——应接近 TDP 标称值 ④风扇转速——应随温度升高而加快。用工具箱的硬件监控功能可以实时查看这些指标！",
          [new ActionLink("硬件监控", "\uE9D9", "navigate_page", "HardwarePage"), new ActionLink("CPU 天梯图", "\uEEA1", "launch_builtin", "cpu-ranking")]),

        ("内存和硬盘需要测试吗？",
         "内存：用 MemTest86 或 Windows 内存诊断工具（Win+R 输入 mdsched）检测，新电脑建议至少跑一遍，内存故障会导致随机蓝屏。硬盘：用 CrystalDiskInfo 查看健康状态，CrystalDiskMark 测速对比标称值。工具箱里都能找到！",
          [new ActionLink("CrystalDiskInfo", "\uE721", "launch_exe", "DiskInfo64"), new ActionLink("MemTest", "\uE721", "launch_exe", "MemTest64")])
    ];

    private static List<(string Q, string A, List<ActionLink>? Links)> GetModule4Data() =>
    [
        ("驱动需要经常更新吗？",
         "不需要。①显卡驱动建议每 1~2 个月更新一次（新游戏优化）②其他驱动「能用就不动」，更新反而可能引入问题 ③不要用第三方驱动助手（驱动精灵、驱动人生等），Windows 更新已能自动安装驱动 ④品牌机去官网下载驱动最靠谱。",
         null),

        ("系统更新要开吗？",
         "建议开启自动更新。①安全补丁修复漏洞，防止被攻击 ②功能更新带来新特性 ③「更新会出问题」是少数情况，不更新的风险更大 ④如果担心，可以延迟更新 1~2 周等别人踩完坑。不要使用任何「关闭 Windows 更新」的工具。",
         null),

        ("电脑越来越慢怎么办？",
         "①检查开机自启项（任务管理器→启动），关掉不需要的 ②卸载不用的软件 ③清理临时文件（工具箱有垃圾清理工具）④检查 C 盘剩余空间（建议留 30GB+）⑤如果是机械硬盘，升级 SSD 效果最明显 ⑥重装系统是终极方案但通常不必要。",
         [new ActionLink("垃圾清理", "\uE74D", "launch_builtin", "junk-cleaner"), new ActionLink("启动项管理", "\uE774", "launch_builtin", "context-menu-mgr")]),

        ("需要装杀毒软件吗？",
         "Windows Defender 已经足够好。①它连续多年 AV-TEST 满分 ②装第三方杀毒反而可能拖慢系统 ③如果一定要额外防护，装个火绒即可——安静不弹窗 ④远离各种「安全卫士」「电脑管家」，它们本身就是流氓软件。",
         [new ActionLink("Defender 设置", "\uEC19", "open_system", "windowsdefender://")]),

        ("怎么保护电脑数据安全？",
         "①重要数据至少存两份（本地+云盘/移动硬盘）②开启 Windows 的文件历史记录功能 ③定期检查备份是否可用 ④不要把所有数据只放桌面——桌面在 C 盘，系统崩了数据就没了 ⑤OneDrive 自动同步桌面和文档，建议开启。",
         null)
    ];

    private static List<(string Q, string A, List<ActionLink>? Links)> GetModule5Data() =>
    [
        ("「手机充电器不能给电脑充电」——真的吗？",
         "部分正确。普通手机充电器功率太低确实不行。但现在很多轻薄本支持 USB-C PD 充电，只要充电器功率≥45W（最好 65W+）就可以充。只是高负载时可能掉电，但日常办公没问题。",
         null),

        ("「电脑要经常关机不然会坏」——真的吗？",
         "错！现代电脑的睡眠模式非常成熟，睡眠状态功耗极低（约 1~2W），硬件不会因此损坏。频繁开关机反而对 SSD 和电源冲击更大。建议：日常用睡眠，一周重启一次即可。",
         null),

        ("「C 盘满了电脑会卡」——真的吗？",
         "部分正确。C 盘空间不足确实会影响性能（虚拟内存和临时文件需要空间），但「满」的阈值是剩余不足 10~15GB，而不是「装了很多东西」。SSD 还需要留 10~20% 空间用于垃圾回收和磨损均衡。所以保持 C 盘有 30GB+ 剩余即可。",
         [new ActionLink("垃圾清理", "\uE74D", "launch_builtin", "junk-cleaner")]),

        ("「清理注册表可以加速电脑」——真的吗？",
         "错！注册表冗余项对性能的影响微乎其微（微软官方确认）。清理注册表反而可能删掉有用的项导致软件故障。各种「注册表清理工具」是伪需求，远离它们。真正加速的方法是清理自启项和卸载不用的软件。",
         [new ActionLink("启动项管理", "\uE774", "launch_builtin", "context-menu-mgr")]),

        ("「笔记本要一直插电用会伤电池」——真的吗？",
         "错！现代笔记本有智能充电管理，充满后会自动切换为电源供电，不会过充。反而经常把电量用到 0% 才充电会加速电池老化。建议：日常插电使用，每月拔电用一次让电池活动活动即可。部分品牌还支持「电池保养模式」限制充到 60~80%。",
         [new ActionLink("电池报告", "\uE867", "launch_builtin", "battery-analyzer")]),

        ("「多核 CPU 一定比少核快」——真的吗？",
         "错！核心数量只是其中一个指标。CPU 性能取决于：①单核性能（影响日常流畅度）②核心数和线程数（影响多任务）③频率（越高越快）④架构代数（13 代 i5 可能比 10 代 i7 快）⑤缓存大小。选 CPU 看天梯图比看核心数靠谱——工具箱里有 CPU 天梯图！",
         [new ActionLink("CPU 天梯图", "\uEEA1", "launch_builtin", "cpu-ranking"), new ActionLink("GPU 天梯图", "\uEEA1", "launch_builtin", "gpu-ranking")])
    ];

    #endregion

    #region Utilities

    private static Color HexToColor(string hex)
    {
        var c = hex.StartsWith('#') ? hex[1..] : hex;
        return Color.FromArgb(255, Convert.ToByte(c[..2], 16), Convert.ToByte(c[2..4], 16), Convert.ToByte(c[4..6], 16));
    }

    #endregion

    #region Helpers

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject obj)
    {
        var parent = VisualTreeHelper.GetParent(obj);
        while (parent is not null)
        {
            yield return parent;
            parent = VisualTreeHelper.GetParent(parent);
        }
    }

    #endregion

    #region P/Invoke

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    #endregion
}

public sealed record ActionLink(string Label, string Glyph, string Action, string Target);

public sealed record GuideStep(string Id, string Icon, string Title, string Description, string ActionHint, string Action, string ButtonText, string SuccessMessage, string NavIcon)
{
    public string Type { get; init; } = "action";
    public string? ShortcutKey { get; init; }
    public string? ShortcutDisplay { get; init; }
}

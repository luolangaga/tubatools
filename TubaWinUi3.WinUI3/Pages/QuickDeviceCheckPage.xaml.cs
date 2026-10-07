using Microsoft.UI.Xaml.Controls.Primitives;
using System.Speech.Synthesis;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.UI;
using DiagnosticsProcess = System.Diagnostics.Process;

namespace TubaWinUi3.Pages;

public sealed partial class QuickDeviceCheckPage : Page
{
    private int _currentStep;
    private const int TotalSteps = 8;

    private Controls.StressTestControl? _stressControl;
    private Controls.KeyboardTestControl? _keyboardTestControl;
    private SpeechSynthesizer? _speechSynth;
    private bool _cameraLaunched;
    private MediaCapture? _mediaCapture;
    private MediaPlayerElement? _cameraPlayer;
    private MediaFrameSourceGroup? _mediaFrameSourceGroup;

    private static readonly string[] StepTitles =
    [
        "恭喜收获新电脑！",
        "硬件信息确认",
        "硬盘通电检查",
        "屏幕坏点检测",
        "外设检查",
        "摄像头检测",
        "音频测试",
        "三烤压力测试"
    ];

    private static readonly string[] StepGlyphs =
    [
        "\uE734", "\uE964", "\uEDA7", "\uE7F4", "\uE92E", "\uE960", "\uEA60", "\uE9D9"
    ];

    private static readonly string[] StepDescriptions =
    [
        "请仔细检查外观，确保没有使用痕迹",
        "核对硬件配置是否与购买一致",
        "检查硬盘通电时间和通电次数",
        "检测屏幕是否有坏点与漏光",
        "测试键盘和硬盘是否正常",
        "确认摄像头能否正常工作",
        "确认扬声器能否正常播放",
        "CPU+GPU+网卡 三烤，验证系统稳定性"
    ];

    public QuickDeviceCheckPage()
    {
        InitializeComponent();
        _currentStep = 0;
        UpdateStepUI();
        Loaded += OnLoaded;

        // 代码构建的画刷不会随主题自动刷新，切换主题后重建当前步骤界面
        // （压力测试 / 摄像头预览进行中不重建，避免打断正在运行的检测）
        ActualThemeChanged += (_, _) =>
        {
            if (_stressControl?.IsRunning != true && !_cameraLaunched)
                UpdateStepContent();
        };
    }

    public void Cleanup()
    {
        _stressControl?.Cleanup();
        _keyboardTestControl?.Cleanup();
        StopCamera();
        _speechSynth?.Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 独立工具窗口中隐藏页面返回按钮：关闭窗口即可退出，避免与标题栏重复
        ToolBackButton.Visibility = BuiltinToolWindow.IsInToolWindow(this)
            ? Visibility.Collapsed
            : Visibility.Visible;

        PlayStepTransition();
    }

    private void ToolBackButton_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateBack();

    private void UpdateStepUI()
    {
        StepIndicatorText.Text = $"第 {_currentStep + 1} 步 / 共 {TotalSteps} 步";
        StepTitleText.Text = StepTitles[_currentStep];
        StepGlyphIcon.Glyph = StepGlyphs[_currentStep];
        StepDescText.Text = StepDescriptions[_currentStep];

        BackButton.Visibility = _currentStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _currentStep == TotalSteps - 1 ? "完成验机" : "下一步";

        for (int i = 0; i < TotalSteps; i++)
        {
            if (StepProgressPanel.Children[i] is Border dot)
            {
                if (i < _currentStep)
                {
                    dot.Background = new SolidColorBrush(ThemeColors.AccentGreen);
                    dot.Width = 8; dot.Height = 8;
                }
                else if (i == _currentStep)
                {
                    dot.Background = new SolidColorBrush(ThemeColors.AccentBlue);
                    dot.Width = 12; dot.Height = 12;
                }
                else
                {
                    dot.Background = new SolidColorBrush(ThemeColors.DimText);
                    dot.Width = 8; dot.Height = 8;
                }
            }
        }

        UpdateStepContent();
    }

    private void UpdateStepContent()
    {
        _keyboardTestControl?.Cleanup();
        _keyboardTestControl = null;
        StepContentPanel.Children.Clear();
        switch (_currentStep)
        {
            case 0: BuildWelcomeStep(); break;
            case 1: BuildHardwareInfoStep(); break;
            case 2: BuildDiskInfoStep(); break;
            case 3: BuildScreenTestStep(); break;
            case 4: BuildPeripheralStep(); break;
            case 5: BuildCameraStep(); break;
            case 6: BuildAudioStep(); break;
            case 7: BuildStressTestStep(); break;
        }
    }

    #region Step 1: Welcome

    private void BuildWelcomeStep()
    {
        var stack = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };

        var emojiBlock = new TextBlock
        {
            Text = "💻",
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var emojiBorder = new Border
        {
            Width = 160,
            Height = 160,
            HorizontalAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            Child = emojiBlock
        };
        stack.Children.Add(emojiBorder);

        stack.Children.Add(new TextBlock
        {
            Text = "🎉 恭喜你收获了一台新电脑！",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var checkCard = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            MaxWidth = 500,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var checkStack = new StackPanel { Spacing = 12 };
        checkStack.Children.Add(new TextBlock
        {
            Text = "📋 外观检查清单",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var checks = new (string Title, string Desc)[]
        {
            ("转轴", "开合屏幕，检查转轴是否有松动或异响"),
            ("外观痕迹", "仔细检查机身是否有划痕、磕碰或使用痕迹"),
            ("接口", "检查各接口是否有插拔痕迹或氧化"),
            ("屏幕表面", "检查屏幕是否有划痕或压痕"),
            ("散热口", "检查散热口是否有灰尘堆积")
        };

        foreach (var (title, desc) in checks)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(new TextBlock
            {
                Text = $"✓ {title}",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeColors.AccentBlue)
            });
            row.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                TextWrapping = TextWrapping.Wrap
            });
            checkStack.Children.Add(row);
        }

        checkCard.Child = checkStack;
        stack.Children.Add(checkCard);

        stack.Children.Add(new TextBlock
        {
            Text = "💡 请在继续之前仔细完成以上检查，如有问题请及时联系商家",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.AccentOrange),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 500
        });

        StepContentPanel.Children.Add(stack);
    }

    #endregion

    #region Step 2: Hardware Info

    private void BuildHardwareInfoStep()
    {
        var stack = new StackPanel { Spacing = 16 };

        var tipCard = BuildTipCard("请核对以下硬件信息是否与购买配置一致，重点关注 CPU、GPU、内存和硬盘型号。");
        stack.Children.Add(tipCard);

        var hwBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            MaxWidth = 700,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var hwStack = new StackPanel { Spacing = 12 };

        var loadingRing = new ProgressRing
        {
            Width = 32,
            Height = 32,
            IsActive = true,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        hwStack.Children.Add(loadingRing);
        hwBorder.Child = hwStack;
        stack.Children.Add(hwBorder);

        _ = Task.Run(async () =>
        {
            try
            {
                var sections = await HardwareInfoService.LoadAsync(false);
                DispatcherQueue.TryEnqueue(() =>
                {
                    hwStack.Children.Clear();

                    for (int s = 0; s < sections.Count; s++)
                    {
                        var section = sections[s];

                        if (s > 0)
                        {
                            hwStack.Children.Add(new Border
                            {
                                Height = 1,
                                Background = new SolidColorBrush(ThemeColors.Separator),
                                Margin = new Thickness(0, 4, 0, 4)
                            });
                        }

                        hwStack.Children.Add(new TextBlock
                        {
                            Text = section.Title,
                            FontSize = 18,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                        });

                        foreach (var item in section.Items)
                        {
                            var row = new Grid
                            {
                                ColumnSpacing = 12,
                                Padding = new Thickness(0, 4, 0, 4)
                            };
                            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                            row.Children.Add(new TextBlock
                            {
                                Text = item.Label,
                                FontSize = 14,
                                Foreground = new SolidColorBrush(ThemeColors.DimText),
                                VerticalAlignment = VerticalAlignment.Center
                            });

                            var valueStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                            Grid.SetColumn(valueStack, 1);

                            valueStack.Children.Add(new TextBlock
                            {
                                Text = item.Value,
                                FontSize = 14,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                TextWrapping = TextWrapping.Wrap,
                                VerticalAlignment = VerticalAlignment.Center
                            });

                            if (item.IsVerified)
                            {
                                valueStack.Children.Add(new Border
                                {
                                    Padding = new Thickness(8, 2, 8, 2),
                                    CornerRadius = new CornerRadius(4),
                                    Background = new SolidColorBrush(Color.FromArgb(38, ThemeColors.AccentGreen.R, ThemeColors.AccentGreen.G, ThemeColors.AccentGreen.B)),
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Child = new TextBlock
                                    {
                                        Text = "真",
                                        FontSize = 12,
                                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                        Foreground = new SolidColorBrush(ThemeColors.AccentGreen)
                                    }
                                });
                            }

                            row.Children.Add(valueStack);
                            hwStack.Children.Add(row);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    hwStack.Children.Clear();
                    hwStack.Children.Add(new TextBlock
                    {
                        Text = $"加载硬件信息失败: {ex.Message}",
                        FontSize = 14,
                        Foreground = new SolidColorBrush(ThemeColors.AccentRed)
                    });
                });
            }
        });

        StepContentPanel.Children.Add(stack);
    }

    #endregion

    #region Step 3: Disk Info

    private void BuildDiskInfoStep()
    {
        var stack = new StackPanel { Spacing = 16 };

        var tipCard = BuildTipCard(
            "新机硬盘通电时间一般应小于 100 小时，通电次数一般应小于 50 次。\n" +
            "如果通电时间过长，可能是翻新机或展示机。");
        stack.Children.Add(tipCard);

        var launchCard = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var launchStack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };

        launchStack.Children.Add(new TextBlock
        {
            Text = "将打开 DiskInfo 查看硬盘通电信息",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var launchBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uEDA7", FontSize = 16 },
                    new TextBlock { Text = "打开 DiskInfo", FontSize = 14 }
                }
            },
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Padding = new Thickness(24, 8, 24, 8)
        };
        launchBtn.Click += async (_, _) =>
        {
            launchBtn.IsEnabled = false;
            launchBtn.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new ProgressRing { Width = 16, Height = 16, IsActive = true },
                    new TextBlock { Text = "正在启动 DiskInfo...", FontSize = 14 }
                }
            };

            await Task.Delay(TimeSpan.FromSeconds(2));

            LaunchDiskInfo();

            launchBtn.IsEnabled = true;
            launchBtn.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uEDA7", FontSize = 16 },
                    new TextBlock { Text = "打开 DiskInfo", FontSize = 14 }
                }
            };
        };
        launchStack.Children.Add(launchBtn);

        launchCard.Child = launchStack;
        stack.Children.Add(launchCard);

        var judgeCard = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            MaxWidth = 500,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var judgeStack = new StackPanel { Spacing = 8 };
        judgeStack.Children.Add(new TextBlock
        {
            Text = "📊 判断标准",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        judgeStack.Children.Add(new TextBlock
        {
            Text = "✅ 通电时间 < 100 小时 → 正常（新机）",
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.AccentGreen)
        });
        judgeStack.Children.Add(new TextBlock
        {
            Text = "⚠️ 通电时间 100~300 小时 → 可能展示机",
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.AccentOrange)
        });
        judgeStack.Children.Add(new TextBlock
        {
            Text = "❌ 通电时间 > 300 小时 → 疑似翻新机",
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.AccentRed)
        });
        judgeCard.Child = judgeStack;
        stack.Children.Add(judgeCard);

        StepContentPanel.Children.Add(stack);
    }

    private void LaunchDiskInfo()
    {
        try
        {
            var toolsRoot = ToolCatalog.ToolsRoot;
            if (string.IsNullOrEmpty(toolsRoot)) return;

            var diskInfoPaths = new[]
            {
                Path.Combine(toolsRoot, "硬盘工具", "DiskInfo", "DiskInfo64.exe"),
                Path.Combine(toolsRoot, "硬盘工具", "DiskInfo", "DiskInfo.exe"),
                Path.Combine(toolsRoot, "硬盘工具", "CrystalDiskInfo", "DiskInfo64.exe"),
                Path.Combine(toolsRoot, "硬盘工具", "CrystalDiskInfo", "DiskInfo.exe"),
            };

            foreach (var path in diskInfoPaths)
            {
                if (File.Exists(path))
                {
                    ToolProcessLauncher.Launch(path, Path.GetDirectoryName(path));
                    SendToast("已打开 DiskInfo", "请查看硬盘通电时间和通电次数");
                    return;
                }
            }

            var allExes = Directory.GetFiles(toolsRoot, "DiskInfo*.exe", SearchOption.AllDirectories);
            if (allExes.Length > 0)
            {
                ToolProcessLauncher.Launch(allExes[0], Path.GetDirectoryName(allExes[0]));
                SendToast("已打开 DiskInfo", "请查看硬盘通电时间和通电次数");
            }
            else
            {
                SendToast("未找到 DiskInfo", "请确保工具箱中已安装 DiskInfo");
            }
        }
        catch (Exception ex)
        {
            SendToast("启动失败", ex.Message);
        }
    }

    #endregion

    #region Step 4: Screen Test

    private void BuildScreenTestStep()
    {
        var stack = new StackPanel { Spacing = 16 };

        var tipCard = BuildTipCard(
            "将进入全屏模式检测屏幕坏点与漏光。\n" +
            "使用 ← → 方向键切换颜色，ESC 退出全屏检测。");
        stack.Children.Add(tipCard);

        var launchCard = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var launchStack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };

        launchStack.Children.Add(new TextBlock
        {
            Text = "全屏检测坏点、漏光、色斑和漏光情况",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var launchBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE7F4", FontSize = 16 },
                    new TextBlock { Text = "开始屏幕检测", FontSize = 14 }
                }
            },
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Padding = new Thickness(24, 8, 24, 8)
        };
        launchBtn.Click += (_, _) => LaunchScreenTest();
        launchStack.Children.Add(launchBtn);

        launchCard.Child = launchStack;
        stack.Children.Add(launchCard);

        StepContentPanel.Children.Add(stack);
    }

    private void LaunchScreenTest()
    {
        try
        {
            var screenTestTool = BuiltinToolRegistry.GetById("screen-test");
            if (screenTestTool != null)
            {
                var context = new BuiltinToolContext { XamlRoot = XamlRoot };
                MainWindow.ActiveToolName = screenTestTool.Name;
                _ = screenTestTool.ExecuteAsync(context);
                SendToast("屏幕检测已启动", "使用方向键切换颜色，ESC 退出");
            }
        }
        catch (Exception ex)
        {
            SendToast("启动失败", ex.Message);
        }
    }

    #endregion

    #region Step 5: Peripheral Check

    private void BuildPeripheralStep()
    {
        var stack = new StackPanel { Spacing = 16 };

        var tipCard = BuildTipCard(
            "请测试键盘每个按键是否正常响应，同时检查硬盘读写是否正常。\n" +
            "按下按键后对应键位会高亮显示。");
        stack.Children.Add(tipCard);

        _keyboardTestControl = new Controls.KeyboardTestControl
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(_keyboardTestControl);

        StepContentPanel.Children.Add(stack);
    }

    #endregion

    #region Step 6: Camera

    private void BuildCameraStep()
    {
        var stack = new StackPanel { Spacing = 16 };

        var tipCard = BuildTipCard("点击下方按钮开启摄像头，确认画面是否正常显示。");
        stack.Children.Add(tipCard);

        var cameraCard = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Width = 480,
            Height = 320,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var cameraGrid = new Grid();
        var placeholder = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        placeholder.Children.Add(new FontIcon
        {
            Glyph = "\uE960",
            FontSize = 28,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        var placeholderText = new TextBlock
        {
            Text = "摄像头预览",
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        placeholder.Children.Add(placeholderText);

        _cameraPlayer = new MediaPlayerElement
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Stretch = Stretch.UniformToFill,
            AutoPlay = true,
            Visibility = Visibility.Collapsed
        };

        cameraGrid.Children.Add(placeholder);
        cameraGrid.Children.Add(_cameraPlayer);

        cameraCard.Child = cameraGrid;
        stack.Children.Add(cameraCard);

        var cameraBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE960", FontSize = 16 },
                    new TextBlock { Text = "开启摄像头", FontSize = 14 }
                }
            },
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Padding = new Thickness(24, 8, 24, 8),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        cameraBtn.Click += async (_, _) =>
        {
            if (!_cameraLaunched)
            {
                try
                {
                    var groups = await MediaFrameSourceGroup.FindAllAsync();
                    if (groups.Count == 0)
                    {
                        placeholderText.Text = "未找到摄像头设备";
                        SendToast("未找到摄像头", "未检测到摄像头设备");
                        return;
                    }

                    _mediaFrameSourceGroup = groups[0];
                    _mediaCapture = new MediaCapture();
                    var settings = new MediaCaptureInitializationSettings
                    {
                        SourceGroup = _mediaFrameSourceGroup,
                        SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                        StreamingCaptureMode = StreamingCaptureMode.Video,
                        MemoryPreference = MediaCaptureMemoryPreference.Cpu
                    };
                    await _mediaCapture.InitializeAsync(settings);

                    var frameSource = _mediaCapture.FrameSources[_mediaFrameSourceGroup.SourceInfos[0].Id];
                    _cameraPlayer!.Source = MediaSource.CreateFromMediaFrameSource(frameSource);

                    _cameraPlayer.Visibility = Visibility.Visible;
                    placeholder.Visibility = Visibility.Collapsed;
                    _cameraLaunched = true;
                    (cameraBtn.Content as StackPanel)!.Children.OfType<TextBlock>().First().Text = "关闭摄像头";
                    SendToast("摄像头已开启", "请确认画面是否正常");
                }
                catch (UnauthorizedAccessException)
                {
                    SendToast("摄像头访问被拒绝", "请在隐私设置中允许访问摄像头");
                }
                catch (Exception ex)
                {
                    SendToast("摄像头启动失败", ex.Message);
                }
            }
            else
            {
                StopCamera();
                _cameraPlayer!.Source = null;
                _cameraPlayer.Visibility = Visibility.Collapsed;
                placeholder.Visibility = Visibility.Visible;
                _cameraLaunched = false;
                (cameraBtn.Content as StackPanel)!.Children.OfType<TextBlock>().First().Text = "开启摄像头";
            }
        };
        stack.Children.Add(cameraBtn);

        stack.Children.Add(new TextBlock
        {
            Text = "💡 如果摄像头无法打开，请检查隐私设置中是否允许访问摄像头",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });

        StepContentPanel.Children.Add(stack);
    }

    private void StopCamera()
    {
        try
        {
            if (_mediaCapture != null)
            {
                Task.Run(() => _mediaCapture.Dispose()).Wait(TimeSpan.FromSeconds(3));
                _mediaCapture = null;
            }
        }
        catch { }
        _cameraLaunched = false;
    }

    #endregion

    #region Step 7: Audio Test

    private void BuildAudioStep()
    {
        var stack = new StackPanel { Spacing = 16 };

        var tipCard = BuildTipCard("点击下方按钮播放测试语音，确认扬声器是否能正常发声。");
        stack.Children.Add(tipCard);

        var audioCard = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var audioStack = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };

        audioStack.Children.Add(new FontIcon
        {
            Glyph = "\uEA60",
            FontSize = 28,
            Foreground = new SolidColorBrush(ThemeColors.AccentBlue)
        });

        audioStack.Children.Add(new TextBlock
        {
            Text = "将使用 Windows 语音合成朗读测试语句",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var playBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE102", FontSize = 16 },
                    new TextBlock { Text = "播放测试语音", FontSize = 14 }
                }
            },
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Padding = new Thickness(24, 8, 24, 8),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        playBtn.Click += (_, _) => PlayTestAudio();
        audioStack.Children.Add(playBtn);

        audioStack.Children.Add(new TextBlock
        {
            Text = "💡 如果听不到语音，请检查音量设置和扬声器连接",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        audioCard.Child = audioStack;
        stack.Children.Add(audioCard);
        StepContentPanel.Children.Add(stack);
    }

    private void PlayTestAudio()
    {
        try
        {
            _speechSynth ??= new SpeechSynthesizer();
            _speechSynth.SpeakAsync("你能否听清这段录音？");
            SendToast("正在播放测试语音", "请确认是否能听清");
        }
        catch (Exception ex)
        {
            SendToast("语音播放失败", ex.Message);
        }
    }

    #endregion

    #region Step 8: Stress Test

    private void BuildStressTestStep()
    {
        _stressControl = new Controls.StressTestControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _stressControl.StressStarted += OnStressStarted;
        _stressControl.StressStopped += OnStressStopped;
        StepContentPanel.Children.Add(_stressControl);
    }

    private DispatcherTimer? _reclaimTimer;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_SHOW = 5;

    private void OnStressStarted(object? sender, EventArgs e)
    {
        StepProgressPanel.Visibility = Visibility.Collapsed;
        StepIndicatorText.Visibility = Visibility.Collapsed;
        StepGlyphIcon.Visibility = Visibility.Collapsed;
        StepTitleText.Visibility = Visibility.Collapsed;
        StepDescText.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        NextButton.Visibility = Visibility.Collapsed;
        ForceToForeground();
        _reclaimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _reclaimTimer.Tick += (_, _) =>
        {
            _reclaimTimer.Stop();
            _reclaimTimer = null;
            if (_stressControl?.IsRunning == true)
            {
                ForceToForeground();
                var t2 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
                t2.Tick += (_, _) =>
                {
                    t2.Stop();
                    if (_stressControl?.IsRunning == true) ForceToForeground();
                };
                t2.Start();
            }
        };
        _reclaimTimer.Start();
    }

    private void ForceToForeground()
    {
        try
        {
            var mainWindow = App.MainWindow;
            if (mainWindow is null) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            mainWindow.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            ShowWindow(hwnd, SW_SHOW);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        catch { }
    }

    private void OnStressStopped(object? sender, EventArgs e)
    {
        StepProgressPanel.Visibility = Visibility.Visible;
        StepIndicatorText.Visibility = Visibility.Visible;
        StepGlyphIcon.Visibility = Visibility.Visible;
        StepTitleText.Visibility = Visibility.Visible;
        StepDescText.Visibility = Visibility.Visible;
        NextButton.Visibility = Visibility.Visible;
        BackButton.Visibility = _currentStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        try { App.MainWindow?.AppWindow.SetPresenter(AppWindowPresenterKind.Default); } catch { }
    }

    #endregion

    #region Navigation

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep >= TotalSteps - 1)
        {
            Cleanup();
            try { App.MainWindow?.AppWindow.SetPresenter(AppWindowPresenterKind.Default); } catch { }
            App.MainWindow?.NavigateBack();
            return;
        }

        _currentStep++;
        UpdateStepUI();
        PlayStepTransition();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep <= 0) return;
        _currentStep--;
        UpdateStepUI();
        PlayStepTransition();
    }

    private void PlayStepTransition()
    {
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(350),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, StepContentPanel);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");

        var slideUp = new DoubleAnimation
        {
            From = 30,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideUp, StepContentPanelTransform);
        Storyboard.SetTargetProperty(slideUp, "Y");

        var sb = new Storyboard();
        sb.Children.Add(fadeIn);
        sb.Children.Add(slideUp);
        StepContentPanel.Opacity = 0;
        sb.Begin();
    }

    #endregion

    #region Helpers

    private Border BuildTipCard(string text)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, ThemeColors.AccentBlue.R, ThemeColors.AccentBlue.G, ThemeColors.AccentBlue.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, ThemeColors.AccentBlue.R, ThemeColors.AccentBlue.G, ThemeColors.AccentBlue.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 600
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        row.Children.Add(new FontIcon
        {
            Glyph = "\uE946",
            FontSize = 18,
            Foreground = new SolidColorBrush(ThemeColors.AccentBlue),
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        card.Child = row;
        return card;
    }

    private static void SendToast(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title).AddText(message).Show();
        }
        catch { }
    }

    #endregion
}

using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class AiAssistantTool : IBuiltinTool
{
    public string Id => "ai-assistant";
    public string Name => "AI 助手";
    public string Description => "智能系统助手，可诊断问题、优化配置、推荐软件并执行操作。";
    public string Glyph => "\uE946";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        var window = new Window();
        var state = new AssistantState();
        var content = BuildDialogContent(state, window);

        var page = new Page { Content = content };
        page.RequestedTheme = ThemeService.CurrentElementTheme;

        window.Content = page;
        window.AppWindow.Title = "AI 助手";
        window.AppWindow.Resize(new SizeInt32(960, 720));

        try
        {
            var mainPos = App.MainWindow?.AppWindow.Position;
            if (mainPos is not null)
            {
                window.AppWindow.Move(new PointInt32(
                    mainPos.Value.X + 40,
                    mainPos.Value.Y + 40));
            }
        }
        catch { }

        window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        ApplyTitleBarTheme(window);
        BackdropService.ApplyBackdrop(window);

        state.Window = window;
        window.Closed += (_, _) =>
        {
            state.Cts.Cancel();
            state.Cts.Dispose();
        };

        window.Activate();

        if (!AiService.IsConfigured)
        {
            AddSystemMessage(state, "AI 服务未配置，请在设置中配置 API 地址、模型名和 API Key 后再使用。");
            state.InputBox.IsEnabled = false;
            state.SendBtn.IsEnabled = false;
        }
        else
        {
            AddSystemMessage(state, "你好！我是图吧助手，可以帮你诊断系统问题、优化配置、推荐软件。\n\n你可以问我：\n- 新电脑怎么验机\n- 电脑卡顿怎么办\n- 内存占用过高怎么优化\n- 推荐硬件检测工具\n- 查看系统配置\n- 最新处理器/显卡性能对比\n- 搜索最新驱动或技术资讯\n\n我会先收集信息，制定方案，确认后再执行操作。\n现在支持联网搜索，可以获取最新的硬件信息和技术资讯！");
        }

        return Task.CompletedTask;
    }

    private static ScrollViewer BuildDialogContent(AssistantState state, Window window)
    {
        var logList = new StackPanel
        {
            Spacing = 8,
            Orientation = Orientation.Vertical
        };

        var logScroll = new ScrollViewer
        {
            Content = logList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            Padding = new Thickness(16, 8, 16, 8)
        };

        state.LogList = logList;
        state.LogScroll = logScroll;

        var newChatBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE710", FontSize = 12 },
                    new TextBlock { Text = "新对话", FontSize = 12 }
                }
            },
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6),
        };
        state.NewChatBtn = newChatBtn;

        var historyBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE81C", FontSize = 12 },
                    new TextBlock { Text = "历史记录", FontSize = 12 }
                }
            },
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6),
        };
        state.HistoryBtn = historyBtn;

        var titleTb = new TextBlock
        {
            Text = "新对话",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        state.TitleText = titleTb;

        var topBar = new Grid
        {
            ColumnSpacing = 8,
            Padding = new Thickness(16, 6, 150, 6),
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"]
        };
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        topBar.Children.Add(newChatBtn); Grid.SetColumn(newChatBtn, 0);
        topBar.Children.Add(titleTb); Grid.SetColumn(titleTb, 1);
        topBar.Children.Add(historyBtn); Grid.SetColumn(historyBtn, 2);

        newChatBtn.Click += (_, _) => NewConversation(state);
        historyBtn.Click += (_, _) => ShowHistory(state);

        var inputBox = new AutoSuggestBox
        {
            PlaceholderText = "输入问题，如：新电脑怎么验机、电脑卡顿怎么办...",
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8),
        };
        state.InputBox = inputBox;

        var sendBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE72A", FontSize = 14 },
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8),
        };
        state.SendBtn = sendBtn;

        var inputBar = new Grid
        {
            ColumnSpacing = 8,
            Padding = new Thickness(16, 0, 16, 12)
        };
        inputBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        inputBar.Children.Add(inputBox);
        Grid.SetColumn(inputBox, 0);
        inputBar.Children.Add(sendBtn);
        Grid.SetColumn(sendBtn, 1);

        var rootGrid = new Grid { RowSpacing = 0 };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        rootGrid.Children.Add(topBar); Grid.SetRow(topBar, 0);
        rootGrid.Children.Add(logScroll); Grid.SetRow(logScroll, 1);
        rootGrid.Children.Add(inputBar); Grid.SetRow(inputBar, 2);

        var scrollViewer = new ScrollViewer
        {
            Content = rootGrid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled
        };

        sendBtn.Click += (_, _) => SendMessage(state);
        inputBox.QuerySubmitted += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.QueryText))
                SendMessage(state);
        };

        return scrollViewer;
    }

    private static async void SendMessage(AssistantState state)
    {
        var text = state.InputBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;
        if (state.IsProcessing) return;

        state.InputBox.Text = "";
        state.IsProcessing = true;
        state.InputBox.IsEnabled = false;
        state.SendBtn.IsEnabled = false;

        AddUserMessage(state, text);

        if (state.ConversationHistory.Count <= 1)
        {
            state.ConversationTitle = text.Length > 30 ? text.Substring(0, 30) + "..." : text;
            state.TitleText.Text = state.ConversationTitle;
        }

        var toolSteps = new List<ToolStep>();
        var dq = state.Window.DispatcherQueue;

        var (streamingBubble, streamingTb) = CreateStreamingBubble();
        state.LogList.Children.Add(streamingBubble);
        var fullContent = new System.Text.StringBuilder();

        try
        {
            await Task.Run(async () =>
            {
                await AiAssistantService.ProcessUserMessageStreamAsync(
                    text,
                    state.ConversationHistory,
                    onTextChunk: chunk =>
                    {
                        dq.TryEnqueue(() =>
                        {
                            fullContent.Append(chunk);
                            streamingTb.Text = fullContent.ToString();
                            SmartScroll(state);
                        });
                    },
                    onToolCall: toolInfo =>
                    {
                        dq.TryEnqueue(() =>
                        {
                            toolSteps.Add(new ToolStep(toolInfo, null));
                            AddToolCallIndicator(state, toolInfo);
                        });
                    },
                    onToolResult: result =>
                    {
                        dq.TryEnqueue(() =>
                        {
                            if (toolSteps.Count > 0)
                            {
                                var last = toolSteps[^1];
                                toolSteps[^1] = new ToolStep(last.CallInfo, result);
                            }
                            AddToolResultIndicator(state, result);
                        });
                    },
                    onActions: actions =>
                    {
                        dq.TryEnqueue(() =>
                        {
                            FinalizeStreamingBubble(state, streamingBubble, fullContent.ToString());

                            var actionContent = "[ACTION]\n" + System.Text.Json.JsonSerializer.Serialize(actions.Select(a => new AiActionExportDto
                            {
                                Kind = a.Kind switch
                                {
                                    AiActionKind.RunCommand => "run_command",
                                    AiActionKind.ModifyConfig => "write_reg",
                                    AiActionKind.LaunchTool => "launch_tool",
                                    AiActionKind.ReadConfig => "read_reg",
                                    _ => "info"
                                },
                                Description = a.Description,
                                Detail = a.Detail,
                                Reason = a.Reason
                            }), TubaCamelCaseContext.Default.ListAiActionExportDto);
                            var card = AiMarkdownRenderer.CreateActionCard(actionContent, onAllResolved: results =>
                            {
                                ContinueAfterActions(state, results);
                            });
                            card.MaxWidth = 720;
                            state.LogList.Children.Add(card);
                            SmartScroll(state);
                        });
                    },
                    onToolRecommendations: _ => { },
                    onError: error =>
                    {
                        dq.TryEnqueue(() =>
                        {
                            FinalizeStreamingBubble(state, streamingBubble, fullContent.ToString());
                            AddErrorMessage(state, error);
                        });
                    },
                    ct: state.Cts.Token);
            }, state.Cts.Token);

            FinalizeStreamingBubble(state, streamingBubble, fullContent.ToString());
        }
        catch (OperationCanceledException)
        {
            AddSystemMessage(state, "已取消");
        }
        catch (Exception ex)
        {
            AddErrorMessage(state, $"发生错误：{ex.Message}");
        }
        finally
        {
            state.IsProcessing = false;
            state.InputBox.IsEnabled = true;
            state.SendBtn.IsEnabled = true;
            SaveCurrentConversation(state);
        }
    }

    private static async void ContinueAfterActions(AssistantState state, List<(AiActionStep action, bool confirmed, string result)> results)
    {
        if (state.IsProcessing) return;
        state.IsProcessing = true;
        state.InputBox.IsEnabled = false;
        state.SendBtn.IsEnabled = false;

        var sb = new System.Text.StringBuilder();
        foreach (var (action, confirmed, result) in results)
        {
            if (confirmed)
            {
                sb.AppendLine($"✓ 已确认执行：{action.Description}");
                sb.AppendLine($"执行结果：\n{result}");
            }
            else
            {
                sb.AppendLine($"✗ 用户拒绝执行：{action.Description}");
            }
            sb.AppendLine();
        }

        state.ConversationHistory.Add(AiChatMessage.User(
            $"[ACTION_CONFIRMED]\n{sb}"));

        var toolSteps = new List<ToolStep>();
        var dq = state.Window.DispatcherQueue;

        var (streamingBubble, streamingTb) = CreateStreamingBubble();
        state.LogList.Children.Add(streamingBubble);
        var fullContent = new System.Text.StringBuilder();

        try
        {
            await Task.Run(async () =>
            {
                await AiAssistantService.ContinueConversationStreamAsync(
                    state.ConversationHistory,
                    onTextChunk: chunk =>
                    {
                        dq.TryEnqueue(() =>
                        {
                            fullContent.Append(chunk);
                            streamingTb.Text = fullContent.ToString();
                            SmartScroll(state);
                        });
                    },
                    onToolCall: toolInfo =>
                    {
                        dq.TryEnqueue(() =>
                        {
                            toolSteps.Add(new ToolStep(toolInfo, null));
                            AddToolCallIndicator(state, toolInfo);
                        });
                    },
                    onToolResult: r =>
                    {
                        dq.TryEnqueue(() =>
                        {
                            if (toolSteps.Count > 0)
                            {
                                var last = toolSteps[^1];
                                toolSteps[^1] = new ToolStep(last.CallInfo, r);
                            }
                            AddToolResultIndicator(state, r);
                        });
                    },
                    onActions: actions =>
                    {
                        dq.TryEnqueue(() =>
                        {
                            FinalizeStreamingBubble(state, streamingBubble, fullContent.ToString());

                            var actionContent = "[ACTION]\n" + System.Text.Json.JsonSerializer.Serialize(actions.Select(a => new AiActionExportDto
                            {
                                Kind = a.Kind switch
                                {
                                    AiActionKind.RunCommand => "run_command",
                                    AiActionKind.ModifyConfig => "write_reg",
                                    AiActionKind.LaunchTool => "launch_tool",
                                    AiActionKind.ReadConfig => "read_reg",
                                    _ => "info"
                                },
                                Description = a.Description,
                                Detail = a.Detail,
                                Reason = a.Reason
                            }), TubaCamelCaseContext.Default.ListAiActionExportDto);
                            var card = AiMarkdownRenderer.CreateActionCard(actionContent, onAllResolved: results =>
                            {
                                ContinueAfterActions(state, results);
                            });
                            card.MaxWidth = 720;
                            state.LogList.Children.Add(card);
                            SmartScroll(state);
                        });
                    },
                    onToolRecommendations: _ => { },
                    onError: error =>
                    {
                        dq.TryEnqueue(() =>
                        {
                            FinalizeStreamingBubble(state, streamingBubble, fullContent.ToString());
                            AddErrorMessage(state, error);
                        });
                    },
                    ct: state.Cts.Token);
            }, state.Cts.Token);

            FinalizeStreamingBubble(state, streamingBubble, fullContent.ToString());
        }
        catch (OperationCanceledException)
        {
            AddSystemMessage(state, "已取消");
        }
        catch (Exception ex)
        {
            AddErrorMessage(state, $"发生错误：{ex.Message}");
        }
        finally
        {
            state.IsProcessing = false;
            state.InputBox.IsEnabled = true;
            state.SendBtn.IsEnabled = true;
            SaveCurrentConversation(state);
        }
    }

    private static (Border bubble, TextBlock streamingTb) CreateStreamingBubble()
    {
        var streamingTb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            IsTextSelectionEnabled = true
        };

        var cursor = new Border
        {
            Width = 2,
            Height = 16,
            Background = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
        headerRow.Children.Add(streamingTb);
        headerRow.Children.Add(cursor);

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(headerRow);

        var bubble = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(12, 12, 12, 4),
            Padding = new Thickness(16, 10, 16, 10),
            MaxWidth = 720,
            Child = stack
        };

        return (bubble, streamingTb);
    }

    private static void FinalizeStreamingBubble(AssistantState state, Border bubble, string fullContent)
    {
        var idx = state.LogList.Children.IndexOf(bubble);
        if (idx < 0) return;

        state.LogList.Children.RemoveAt(idx);

        if (string.IsNullOrWhiteSpace(fullContent)) return;

        var rendered = AiMarkdownRenderer.Render(fullContent);
        rendered.MaxWidth = 720;

        var border = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(12, 12, 12, 4),
            Padding = new Thickness(16, 10, 16, 10),
            Child = rendered
        };

        state.LogList.Children.Insert(idx, border);
    }

    private static void AddToolCallIndicator(AssistantState state, string toolInfo)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        stack.Children.Add(new FontIcon
        {
            Glyph = "\uE74C",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        });

        var isSearch = toolInfo.Contains("web_search", StringComparison.OrdinalIgnoreCase);
        var displayText = isSearch ? $"搜索：{ExtractQueryFromToolInfo(toolInfo)}" : $"调用工具：{toolInfo}";

        stack.Children.Add(new TextBlock
        {
            Text = displayText,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Child = stack
        };

        state.LogList.Children.Add(border);
        SmartScroll(state);
    }

    private static void AddToolResultIndicator(AssistantState state, string result)
    {
        var truncated = result.Length > 300 ? result.Substring(0, 300) + "..." : result;

        var tb = new TextBlock
        {
            Text = truncated,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            MaxHeight = 80,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas")
        };

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(16, 0, 0, 0),
            MaxWidth = 700,
            Child = new ScrollViewer
            {
                Content = tb,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 80
            }
        };

        state.LogList.Children.Add(border);
        SmartScroll(state);
    }

    private static string ExtractQueryFromToolInfo(string toolInfo)
    {
        var idx = toolInfo.IndexOf("query=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return toolInfo;
        var start = idx + "query=".Length;
        var end = toolInfo.IndexOf('|', start);
        if (end < 0) end = toolInfo.Length;
        return toolInfo.Substring(start, end - start).Trim();
    }

    private static void AddSystemMessage(AssistantState state, string text)
    {
        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            }
        };

        state.LogList.Children.Add(border);
    }

    private static void AddUserMessage(AssistantState state, string text)
    {
        var border = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(12, 12, 4, 12),
            Padding = new Thickness(16, 10, 16, 10),
            MaxWidth = 500,
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
            }
        };

        state.LogList.Children.Add(border);
        SmartScroll(state);
    }

    private static void NewConversation(AssistantState state)
    {
        SaveCurrentConversation(state);
        state.ConversationHistory.Clear();
        state.ConversationId = Guid.NewGuid().ToString("N")[..12];
        state.ConversationTitle = "新对话";
        state.TitleText.Text = "新对话";
        state.LogList.Children.Clear();
        AddSystemMessage(state, "新对话已开始。请输入你的问题。");
    }

    private static void SaveCurrentConversation(AssistantState state)
    {
        if (state.ConversationHistory.Count == 0) return;
        try
        {
            AiAssistantService.SaveConversation(
                state.ConversationId,
                state.ConversationTitle,
                state.ConversationHistory);
        }
        catch { }
    }

    private static void ShowHistory(AssistantState state)
    {
        SaveCurrentConversation(state);

        var flyout = new MenuFlyout();

        var conversations = AiAssistantService.ListConversations();

        if (conversations.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = "暂无历史记录",
                IsEnabled = false
            });
        }
        else
        {
            foreach (var conv in conversations.Take(20))
            {
                var item = new MenuFlyoutItem
                {
                    Text = $"{conv.Title}  ({conv.CreatedAt:MM/dd HH:mm})",
                    Tag = conv
                };
                item.Click += (_, _) =>
                {
                    LoadConversation(state, conv);
                };
                flyout.Items.Add(item);
            }
        }

        flyout.ShowAt(state.HistoryBtn);
    }

    private static void LoadConversation(AssistantState state, ConversationMeta meta)
    {
        var messages = AiAssistantService.LoadConversation(meta.Id);

        state.ConversationHistory.Clear();
        state.ConversationHistory.AddRange(messages);
        state.ConversationId = meta.Id;
        state.ConversationTitle = meta.Title;
        state.TitleText.Text = meta.Title;

        state.LogList.Children.Clear();

        foreach (var msg in messages)
        {
            if (msg.Role == "system") continue;
            if (msg.Role == "user")
            {
                if (msg.Content.StartsWith("[ACTION_CONFIRMED]", StringComparison.OrdinalIgnoreCase)) continue;
                if (msg.Content.StartsWith("[TOOL_RESULT]", StringComparison.OrdinalIgnoreCase)) continue;
                AddUserMessage(state, msg.Content);
            }
            else if (msg.Role == "assistant")
            {
                var cleanContent = msg.Content;
                if (string.IsNullOrWhiteSpace(cleanContent)) continue;

                var rendered = AiMarkdownRenderer.Render(cleanContent);
                rendered.MaxWidth = 720;

                var border = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                    CornerRadius = new CornerRadius(12, 12, 12, 4),
                    Padding = new Thickness(16, 10, 16, 10),
                    Child = rendered
                };
                state.LogList.Children.Add(border);
            }
        }
    }

    private static void AddErrorMessage(AssistantState state, string text)
    {
        var border = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(40, 196, 43, 28)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10),
            MaxWidth = 600,
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28))
            }
        };

        state.LogList.Children.Add(border);
    }

    private static void SmartScroll(AssistantState state)
    {
        state.Window.DispatcherQueue.TryEnqueue(() =>
        {
            var sv = state.LogScroll;
            if (sv.ScrollableHeight <= 0) return;
            var distFromBottom = sv.ScrollableHeight - sv.VerticalOffset;
            if (distFromBottom < 80)
            {
                sv.ChangeView(null, sv.ScrollableHeight, null);
            }
        });
    }

    private static void ApplyTitleBarTheme(Window window)
    {
        var tb = window.AppWindow.TitleBar;
        var isDark = ThemeService.CurrentTheme == AppTheme.Dark ||
                     (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        if (isDark)
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
            tb.InactiveBackgroundColor = Color.FromArgb(255, 32, 32, 32);
        }
        else
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
            tb.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.InactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
        }

        tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
        tb.ButtonInactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
    }
}

internal sealed record ToolStep(string CallInfo, string? Result);

internal sealed class AssistantState
{
    public Window Window = null!;
    public StackPanel LogList = null!;
    public ScrollViewer LogScroll = null!;
    public AutoSuggestBox InputBox = null!;
    public Button SendBtn = null!;
    public Button NewChatBtn = null!;
    public Button HistoryBtn = null!;
    public TextBlock TitleText = null!;
    public List<AiChatMessage> ConversationHistory = [];
    public string ConversationId = Guid.NewGuid().ToString("N")[..12];
    public string ConversationTitle = "新对话";
    public bool IsProcessing;
    public CancellationTokenSource Cts = new();
}

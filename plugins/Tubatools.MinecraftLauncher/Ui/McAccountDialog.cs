using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.PluginSdk;
using Tubatools.MinecraftLauncher.Services;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>账户对话框：微软正版登录、选择已有账户、新建离线账户、删除账户。</summary>
internal sealed class McAccountDialog
{
    private readonly McPalette _palette;
    private readonly McAccountStore _store;
    private readonly XamlRoot _root;
    private readonly TextBox _name = new()
    {
        Header = "新账户名",
        PlaceholderText = "3–16 位字母、数字或下划线"
    };
    private readonly TextBlock _error = new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed
    };
    private readonly StackPanel _list = new() { Spacing = 4 };
    private McAccount? _selected;
    private bool _changed;

    public bool MicrosoftLoginRequested { get; private set; }

    public McAccountDialog(PluginExecutionContext host, McPalette palette, McAccountStore store, XamlRoot root)
    {
        _palette = palette;
        _store = store;
        _root = root;
        _error.Foreground = palette.Danger;
        _selected = store.Selected;
    }

    public async Task<bool> ShowAsync()
    {
        var content = new StackPanel { Spacing = 12, MinWidth = 380 };
        content.Children.Add(McUi.Text("使用微软正版账户，或添加一个离线账户。微软登录会打开浏览器并显示设备代码。",
            12, FontWeights.Normal, _palette.Secondary, true));
        BuildList();
        var listScroll = new ScrollViewer
        {
            MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Enabled,
            Content = _list
        };
        content.Children.Add(listScroll);

        var addRow = new Grid { ColumnSpacing = 6 };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addRow.Children.Add(_name);
        var random = McUi.Button("随机名字", _palette, height: 38);
        random.VerticalAlignment = VerticalAlignment.Bottom;
        random.Click += (_, _) =>
        {
            string[] names = ["Steve", "Alex", "Herobrine", "Creeper", "Enderman", "Notch"];
            _name.Text = names[System.Random.Shared.Next(names.Length)] + System.Random.Shared.Next(10, 99);
        };
        Grid.SetColumn(random, 1);
        addRow.Children.Add(random);
        content.Children.Add(addRow);
        content.Children.Add(_error);

        var dialog = new ContentDialog
        {
            XamlRoot = _root,
            RequestedTheme = _root.Content is FrameworkElement element ? element.ActualTheme : ElementTheme.Default,
            Title = "账户",
            Content = content,
            PrimaryButtonText = "使用离线账户",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        var microsoft = McUi.Button("微软正版登录", _palette, accent: true, height: 44,
            glyph: "\uE8D7", fontSize: 14);
        microsoft.HorizontalAlignment = HorizontalAlignment.Stretch;
        microsoft.Click += (_, _) =>
        {
            MicrosoftLoginRequested = true;
            dialog.Hide();
        };
        content.Children.Insert(0, microsoft);
        content.Children.Insert(1, McUi.Text("需要已购买 Minecraft: Java Edition 的微软账户；登录成功后自动保存。",
            11.5, FontWeights.Normal, _palette.Tertiary, true));
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var name = _name.Text.Trim();
                if (name.Length > 0)
                {
                    _store.Add(name);
                    _changed = true;
                    return;
                }

                if (_selected is null)
                {
                    args.Cancel = true;
                    ShowError("请输入用户名，或在上面选择一个已有账户。");
                    return;
                }

                _store.Select(_selected);
                _changed = true;
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                ShowError(ex.Message);
            }
        };

        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            return false;
        }

        return _changed;
    }

    private void BuildList()
    {
        _list.Children.Clear();
        if (_store.Accounts.Count == 0)
        {
            _list.Children.Add(McUi.Text("还没有账户。点击上方「微软正版登录」，或输入用户名添加离线账户。", 12,
                FontWeights.Normal, _palette.Tertiary, true));
            return;
        }

        foreach (var account in _store.Accounts)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var radio = new RadioButton
            {
                GroupName = "mc-accounts",
                IsChecked = string.Equals(account.Name, _store.Selected?.Name, StringComparison.OrdinalIgnoreCase),
                Content = new StackPanel
                {
                    Spacing = 0,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{account.Name}  ·  {account.TypeLabel}",
                            FontSize = 13.5,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = _palette.Text,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock
                        {
                            Text = account.Uuid,
                            FontSize = 10.5,
                            Foreground = _palette.Tertiary
                        }
                    }
                }
            };
            radio.Checked += (_, _) =>
            {
                _selected = account;
                _name.Text = "";
            };
            row.Children.Add(radio);

            var delete = McUi.IconButton("\uE74D", "删除该账户", _palette, 26);
            delete.Click += (_, _) =>
            {
                _store.Remove(account);
                _changed = true;
                if (string.Equals(_selected?.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
                    _selected = _store.Selected;
                BuildList();
            };
            Grid.SetColumn(delete, 1);
            row.Children.Add(delete);
            _list.Children.Add(row);
        }
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }
}

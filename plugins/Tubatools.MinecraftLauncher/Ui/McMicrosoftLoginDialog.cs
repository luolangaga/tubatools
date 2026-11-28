using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tubatools.MinecraftLauncher.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>微软设备代码登录窗口：显示代码、自动打开浏览器，并轮询直到登录完成。</summary>
internal sealed class McMicrosoftLoginDialog
{
    private readonly McPalette _palette;
    private readonly XamlRoot _root;
    private readonly string? _customClientId;
    private readonly TextBlock _code = new()
    {
        FontFamily = new FontFamily("Consolas"),
        FontSize = 28,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        IsTextSelectionEnabled = true,
        CharacterSpacing = 180
    };
    private readonly TextBlock _status = new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _error = new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
        Visibility = Visibility.Collapsed
    };
    private readonly ProgressRing _ring = new() { IsActive = true, Width = 20, Height = 20 };
    private readonly Button _open;
    private readonly Button _copy;
    private readonly Button _retry;
    private ContentDialog? _dialog;
    private McDeviceCode? _deviceCode;
    private McMicrosoftSession? _session;

    public McMicrosoftLoginDialog(McPalette palette, XamlRoot root, string? customClientId = null)
    {
        _palette = palette;
        _root = root;
        _customClientId = customClientId;
        _open = McUi.Button("打开登录页面", palette, accent: true, height: 40, glyph: "\uE774");
        _open.IsEnabled = false;
        _open.Click += (_, _) => OpenBrowser();
        _copy = McUi.Button("复制代码", palette, height: 40, glyph: "\uE8C8");
        _copy.IsEnabled = false;
        _copy.Click += (_, _) => CopyCode();
        _retry = McUi.Button("重新获取代码", palette, height: 40, glyph: "\uE72C");
        _retry.Visibility = Visibility.Collapsed;
        _retry.Click += async (_, _) => await StartAsync();
    }

    public async Task<McMicrosoftSession?> ShowAsync()
    {
        var content = new StackPanel { Spacing = 10, MinWidth = 420 };
        content.Children.Add(McUi.Text("在浏览器中登录微软账户，然后输入下面的代码。登录成功后窗口会自动关闭。",
            12.5, FontWeights.Normal, _palette.Secondary, true));
        content.Children.Add(McUi.Text(string.IsNullOrWhiteSpace(_customClientId)
                ? "当前使用内置的已获准公共客户端；浏览器授权页可能显示 Prism Launcher，这是该公共客户端的注册名称，不是本工具箱。"
                : "当前使用自定义 Azure 应用；授权页会显示该应用的注册名称。该应用必须先通过 Minecraft API 审核，否则最后一步会失败。",
            10.5, FontWeights.Normal, _palette.Tertiary, true));
        var codeCard = new Border
        {
            Background = new SolidColorBrush(McPalette.Color(0x28, 0xFF, 0xFF, 0xFF)),
            BorderBrush = _palette.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 10, 14, 10),
            Child = _code
        };
        content.Children.Add(codeCard);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_open);
        buttons.Children.Add(_copy);
        buttons.Children.Add(_retry);
        content.Children.Add(buttons);
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        statusRow.Children.Add(_ring);
        statusRow.Children.Add(_status);
        content.Children.Add(statusRow);
        content.Children.Add(_error);
        content.Children.Add(McUi.Text("仅使用微软官方设备代码登录；令牌使用 Windows DPAPI 加密后保存在本机。",
            11, FontWeights.Normal, _palette.Tertiary, true));

        _dialog = new ContentDialog
        {
            XamlRoot = _root,
            RequestedTheme = _root.Content is FrameworkElement element ? element.ActualTheme : ElementTheme.Default,
            Title = "微软正版登录",
            Content = content,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.None
        };
        _dialog.Closed += (_, _) => _cts?.Cancel();
        _dialog.Opened += async (_, _) => await StartAsync();
        await _dialog.ShowAsync();
        return _session;
    }

    private CancellationTokenSource? _cts;

    private async Task StartAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _error.Visibility = Visibility.Collapsed;
        _retry.Visibility = Visibility.Collapsed;
        _ring.IsActive = true;
        _status.Text = "正在获取设备代码…";
        try
        {
            var code = await McMicrosoftAuth.StartDeviceCodeAsync(McHttp.Client, token,
                McMicrosoftAuth.ResolveClientIds(_customClientId));
            _deviceCode = code;
            _code.Text = code.UserCode;
            _copy.IsEnabled = true;
            _open.IsEnabled = true;
            CopyCode();
            _status.Text = "已复制代码并打开浏览器，请粘贴代码完成登录。";
            OpenBrowser();
            var progress = new Progress<string>(message => _status.Text = message);
            var session = await McMicrosoftAuth.CompleteLoginAsync(McHttp.Client, code, progress, token);
            if (token.IsCancellationRequested) return;
            _session = session;
            _status.Text = "登录成功，正在保存账户…";
            _ring.IsActive = false;
            _dialog?.Hide();
        }
        catch (OperationCanceledException)
        {
            // 用户关闭窗口。
        }
        catch (Exception ex)
        {
            _ring.IsActive = false;
            _error.Text = ex.Message;
            _error.Visibility = Visibility.Visible;
            _retry.Visibility = Visibility.Visible;
            _status.Text = "登录未完成，可以重试。";
        }
    }

    private void OpenBrowser()
    {
        if (_deviceCode is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_deviceCode.VerificationUri) { UseShellExecute = true });
        }
        catch
        {
            _status.Text = "无法自动打开浏览器，请手动访问 " + _deviceCode.VerificationUri;
        }
    }

    private void CopyCode()
    {
        if (string.IsNullOrWhiteSpace(_code.Text)) return;
        try
        {
            var package = new DataPackage();
            package.SetText(_code.Text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            _status.Text = "代码已复制，请在浏览器中粘贴。";
        }
        catch
        {
            // 剪贴板被占用时不影响登录。
        }
    }
}

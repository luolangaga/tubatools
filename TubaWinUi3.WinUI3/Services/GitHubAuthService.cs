using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class GitHubUserInfo
{
    public required string Login { get; init; }
    public string? AvatarUrl { get; init; }
    public string? Name { get; init; }
    public string? Bio { get; init; }
}

public static class GitHubAuthService
{
    private const string ClientId = "Ov23ligpMXigE9Bo4KDD";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    static GitHubAuthService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-GitHubAuth");
    }

    public static string? GetToken() => AppSettings.Get("GitHubToken");

    public static bool IsLoggedIn => !string.IsNullOrWhiteSpace(GetToken());

    public static void SetToken(string token)
    {
        AppSettings.Set("GitHubToken", token);
        _cachedUser = null;
    }

    public static void Logout()
    {
        AppSettings.Remove("GitHubToken");
        _cachedUser = null;
    }

    private static GitHubUserInfo? _cachedUser;

    public static async Task<GitHubUserInfo?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        if (_cachedUser is not null) return _cachedUser;
        var token = GetToken();
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.Add("Authorization", $"Bearer {token}");
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _cachedUser = new GitHubUserInfo
            {
                Login = root.GetProperty("login").GetString() ?? "",
                AvatarUrl = root.TryGetProperty("avatar_url", out var av) ? av.GetString() : null,
                Name = root.TryGetProperty("name", out var n) ? n.GetString() : null,
                Bio = root.TryGetProperty("bio", out var b) ? b.GetString() : null
            };
            return _cachedUser;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string?> StartDeviceFlowAsync(XamlRoot xamlRoot, CancellationToken ct = default)
    {
        var dialog = new ContentDialog
        {
            Title = "登录 GitHub",
            CloseButtonText = "取消",
            XamlRoot = xamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        dialog.Resources["ContentDialogMaxWidth"] = 480;

        var stack = new StackPanel { Spacing = 16 };

        var statusIcon = new FontIcon
        {
            Glyph = "\uE77B",
            FontSize = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        };

        var statusText = new TextBlock
        {
            Text = "正在获取验证码...",
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        var codeBorder = new Border
        {
            Padding = new Thickness(20, 14, 20, 14),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        var codeText = new TextBlock
        {
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        codeBorder.Child = codeText;

        var copyButton = new Button
        {
            Content = "复制验证码并打开浏览器",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(20, 8, 20, 8),
            Visibility = Visibility.Collapsed
        };

        var tipText = new TextBlock
        {
            Text = "请在浏览器中输入验证码完成授权",
            FontSize = 13,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var progress = new ProgressBar
        {
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed
        };

        stack.Children.Add(statusIcon);
        stack.Children.Add(statusText);
        stack.Children.Add(codeBorder);
        stack.Children.Add(copyButton);
        stack.Children.Add(tipText);
        stack.Children.Add(progress);

        dialog.Content = stack;

        string? deviceCode = null;
        string? userCode = null;
        string? verificationUri = null;
        int intervalSeconds = 5;

        var pendingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var loginTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var deviceCodeReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = ClientId,
                        ["scope"] = "public_repo"
                    })
                };
                req.Headers.Add("Accept", "application/json");
                using var resp = await _http.SendAsync(req, pendingCts.Token);
                var body = await resp.Content.ReadAsStringAsync(pendingCts.Token);

                var parsed = ParseFormOrJson(body);
                deviceCode = parsed.GetValueOrDefault("device_code");
                userCode = parsed.GetValueOrDefault("user_code");
                verificationUri = parsed.GetValueOrDefault("verification_uri");
                if (int.TryParse(parsed.GetValueOrDefault("interval"), out var intv))
                    intervalSeconds = intv;
                else
                    intervalSeconds = 5;

                dialog.DispatcherQueue.TryEnqueue(() =>
                {
                    statusText.Text = "请在浏览器中输入以下验证码：";
                    codeText.Text = userCode;
                    codeBorder.Visibility = Visibility.Visible;
                    copyButton.Visibility = Visibility.Visible;
                    tipText.Text = $"打开 {verificationUri} 并输入验证码";
                    tipText.Visibility = Visibility.Visible;
                    progress.Visibility = Visibility.Visible;
                    statusIcon.Glyph = "\uE8F1";
                });

                deviceCodeReady.TrySetResult(true);
            }
            catch (Exception ex)
            {
                dialog.DispatcherQueue.TryEnqueue(() =>
                {
                    statusText.Text = $"获取验证码失败：{ex.Message}";
                    statusIcon.Glyph = "\uE783";
                    progress.Visibility = Visibility.Collapsed;
                });
                deviceCodeReady.TrySetResult(false);
                loginTcs.TrySetResult(null);
            }
        }, pendingCts.Token);

        copyButton.Click += (s, e) =>
        {
            try
            {
                // 统一走 ClipboardService：占用重试，失败只留痕（文案照旧提示"已复制"）
                if (userCode is not null) ClipboardService.TrySetText(userCode);
                if (verificationUri is not null)
                {
                    Process.Start(new ProcessStartInfo(verificationUri) { UseShellExecute = true });
                }
                copyButton.Content = "已复制，等待授权...";
            }
            catch { }
        };

        _ = Task.Run(async () =>
        {
            var ready = await deviceCodeReady.Task;
            if (!ready || deviceCode is null)
                return;

            while (!pendingCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds + 1), pendingCts.Token);
                }
                catch (OperationCanceledException) { break; }

                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
                    {
                        Content = new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["client_id"] = ClientId,
                            ["device_code"] = deviceCode,
                            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                        })
                    };
                    req.Headers.Add("Accept", "application/json");
                    using var resp = await _http.SendAsync(req, pendingCts.Token);
                    var body = await resp.Content.ReadAsStringAsync(pendingCts.Token);
                    var parsed = ParseFormOrJson(body);

                    if (parsed.TryGetValue("error", out var error))
                    {
                        if (error == "authorization_pending") continue;
                        if (error == "slow_down")
                        {
                            intervalSeconds += 5;
                            continue;
                        }
                        if (error == "expired_token")
                        {
                            dialog.DispatcherQueue.TryEnqueue(() =>
                            {
                                statusText.Text = "验证码已过期，请重新登录";
                                statusIcon.Glyph = "\uE783";
                                progress.Visibility = Visibility.Collapsed;
                            });
                            loginTcs.TrySetResult(null);
                            return;
                        }
                        if (error == "access_denied")
                        {
                            dialog.DispatcherQueue.TryEnqueue(() =>
                            {
                                statusText.Text = "授权被拒绝";
                                statusIcon.Glyph = "\uE783";
                                progress.Visibility = Visibility.Collapsed;
                            });
                            loginTcs.TrySetResult(null);
                            return;
                        }
                        continue;
                    }

                    if (parsed.TryGetValue("access_token", out var token))
                    {
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            SetToken(token);
                            loginTcs.TrySetResult(token);

                            dialog.DispatcherQueue.TryEnqueue(() =>
                            {
                                try
                                {
                                    statusText.Text = "登录成功！";
                                    statusIcon.Glyph = "\uEC61";
                                    statusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 74, 222, 128));
                                    progress.Visibility = Visibility.Collapsed;
                                    codeBorder.Visibility = Visibility.Collapsed;
                                    copyButton.Visibility = Visibility.Collapsed;
                                    tipText.Visibility = Visibility.Collapsed;
                                    dialog.Hide();
                                }
                                catch { }
                            });
                            return;
                        }
                    }

                    if (!parsed.ContainsKey("error") && !parsed.ContainsKey("access_token"))
                    {
                        dialog.DispatcherQueue.TryEnqueue(() =>
                        {
                            statusText.Text = $"意外响应，请重试";
                            statusIcon.Glyph = "\uE783";
                            progress.Visibility = Visibility.Collapsed;
                        });
                        loginTcs.TrySetResult(null);
                        return;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }

            loginTcs.TrySetResult(null);
        }, pendingCts.Token);

        dialog.CloseButtonClick += (s, e) =>
        {
            pendingCts.Cancel();
            loginTcs.TrySetResult(null);
        };

        await dialog.ShowAsync();

        pendingCts.Cancel();

        var result = await loginTcs.Task;
        return result;
    }

    public static async Task<bool> EnsureAuthenticatedAsync(XamlRoot xamlRoot, CancellationToken ct = default)
    {
        if (IsLoggedIn)
        {
            var user = await GetCurrentUserAsync(ct);
            if (user is not null) return true;
            Logout();
        }

        var token = await StartDeviceFlowAsync(xamlRoot, ct);
        return !string.IsNullOrWhiteSpace(token);
    }

    public static HttpClient CreateAuthenticatedClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Community");
        var token = GetToken();
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return client;
    }

    private static Dictionary<string, string> ParseFormOrJson(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        body = body.Trim();
        if (body.StartsWith('{'))
        {
            try
            {
                var doc = JsonDocument.Parse(body);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var val = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.GetRawText();
                    result[prop.Name] = val;
                }
                return result;
            }
            catch { }
        }

        foreach (var pair in body.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
                result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
        }

        return result;
    }
}

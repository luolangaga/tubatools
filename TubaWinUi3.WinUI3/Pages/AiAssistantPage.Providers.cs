using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Controls;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Pages;

public sealed partial class AiAssistantPage
{
    private bool _providersPending, _providerDialogOpen;
    private async void Providers_Click(object sender, RoutedEventArgs e)
    {
        if (_providerDialogOpen || _closed) return;
        _providerDialogOpen = true;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime?.Token ?? CancellationToken.None);
        try
        {
            var providers = AgentProviderStore.Read().ToList();
            var choices = new ComboBox { Header = L("ProviderChoose", "编辑或添加提供商"), HorizontalAlignment = HorizontalAlignment.Stretch };
            var name = new TextBox { Header = L("ProviderName", "名称"), MaxLength = 120 };
            var protocol = new ComboBox { Header = L("ProviderProtocol", "接口类型"), HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "OpenAI Chat Completions", "OpenAI Responses", "Anthropic Messages", "Google Gemini" }, SelectedIndex = 0 };
            var endpoint = new TextBox { Header = L("ProviderEndpoint", "API 根地址"), PlaceholderText = "https://api.example.com/v1" };
            var key = new PasswordBox { Header = L("ProviderKey", "API 密钥（本地服务可留空）"), MaxLength = 8192 };
            var models = new TextBox { Header = L("ProviderModels", "模型 ID（每行一个）"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 180 };
            var images = new CheckBox { Content = L("ProviderImages", "这些模型支持图片输入（需提供商实际支持）") };
            AutomationProperties.SetAutomationId(images, "AgentProviderImages");
            var discover = new Button { Content = L("ProviderDiscover", "从服务读取模型") };
            var remove = new Button { Content = L("ProviderRemove", "删除此提供商"), IsEnabled = false };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            foreach (var pair in new (UIElement Element, string Id)[] { (choices,"Select"),(name,"Name"),(protocol,"Protocol"),(endpoint,"Endpoint"),(key,"Key"),(models,"Models"),(discover,"Discover"),(remove,"Remove") }) AutomationProperties.SetAutomationId(pair.Element, "AgentProvider" + pair.Id);
            var form = new StackPanel { Spacing = 12, MinWidth = 430 };
            form.Children.Add(new TextBlock { Text = L("ProviderHint", "支持自定义 API 地址与多个模型。密钥使用 Windows 用户加密保存；保存后模型会加入助手列表。"), TextWrapping = TextWrapping.Wrap });
            foreach (var element in new FrameworkElement[] { choices, name, protocol, endpoint, key, models, images }) form.Children.Add(element);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(discover); actions.Children.Add(remove); form.Children.Add(actions); form.Children.Add(status);
            var dialog = new FluentContentDialog
            {
                XamlRoot = XamlRoot, Title = L("Providers", "AI 提供商"), Content = new ScrollViewer { Content = form, MaxHeight = 620 },
                PrimaryButtonText = L("ProviderSave", "保存并应用"), CloseButtonText = L("Close", "关闭"), DefaultButton = ContentDialogButton.None
            };
            void RefreshChoices(int index = 0) { choices.ItemsSource = new[] { L("ProviderNew", "新增提供商") }.Concat(providers.Select(p => p.Name)).ToArray(); choices.SelectedIndex = index; }
            choices.SelectionChanged += (_, _) =>
            {
                var selected = choices.SelectedIndex - 1;
                var p = selected >= 0 && selected < providers.Count ? providers[selected] : null;
                name.Text = p?.Name ?? ""; endpoint.Text = p?.BaseUrl ?? ""; key.Password = p?.ApiKey ?? ""; models.Text = string.Join("\n", p?.Models ?? []);
                images.IsChecked = p?.SupportsImages == true;
                protocol.SelectedIndex = p is null ? 0 : Array.IndexOf(AgentProviderStore.Protocols, p.Protocol); remove.IsEnabled = p is not null; status.Text = "";
            };
            discover.Click += async (_, _) =>
            {
                discover.IsEnabled = false; choices.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
                var typedUrl = endpoint.Text; var typedKey = key.Password; var typedProtocol = protocol.SelectedIndex;
                status.Text = L("ProviderDiscovering", "正在读取模型列表…");
                try
                {
                    var found = await AgentProviderStore.DiscoverModelsAsync(typedUrl, AgentProviderStore.Protocols[Math.Max(0, typedProtocol)], typedKey, lifetime.Token);
                    if (lifetime.IsCancellationRequested) return;
                    if (endpoint.Text == typedUrl && key.Password == typedKey && protocol.SelectedIndex == typedProtocol) models.Text = string.Join("\n", found);
                    status.Text = string.Format(L("ProviderFound", "读取到 {0} 个模型。请保留需要使用的模型，也可手动补充。"), found.Count);
                }
                catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) status.Text = L("ProviderDiscoverFailed", "读取超时，请重试或手动填写模型 ID。"); }
                catch (Exception ex) { status.Text = ex is ArgumentException ? ex.Message : L("ProviderDiscoverFailed", "读取模型失败，请检查 API 地址、密钥和网络，或手动填写模型 ID。"); }
                finally { discover.IsEnabled = true; choices.IsEnabled = true; dialog.IsPrimaryButtonEnabled = true; }
            };
            remove.Click += (_, _) =>
            {
                var selected = choices.SelectedIndex - 1; if (selected < 0 || selected >= providers.Count) return;
                try { var updated = providers.ToList(); updated.RemoveAt(selected); AgentProviderStore.Save(updated); providers = updated; _providersPending = true; RefreshChoices(); }
                catch { status.Text = L("ProviderSaveFailed", "提供商配置保存失败，请检查数据目录。修改尚未应用。"); }
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    var selected = choices.SelectedIndex - 1;
                    var id = selected >= 0 ? providers[selected].Id : "tuba-custom-" + Guid.NewGuid().ToString("N");
                    var item = new AgentProviderDefinition(id, name.Text.Trim(), AgentProviderStore.Protocols[Math.Max(0, protocol.SelectedIndex)], endpoint.Text.Trim().TrimEnd('/'), key.Password,
                        models.Text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray(), images.IsChecked == true);
                    AgentProviderStore.Validate(item); var updated = providers.ToList(); if (selected >= 0) updated[selected] = item; else updated.Add(item);
                    AgentProviderStore.Save(updated); providers = updated; _providersPending = true;
                }
                catch (Exception ex) { args.Cancel = true; status.Text = ex is ArgumentException ? ex.Message : L("ProviderSaveFailed", "提供商配置保存失败，请检查数据目录。修改尚未应用。"); }
            };
            RefreshChoices(providers.Count > 0 ? 1 : 0); await dialog.ShowAsync(); lifetime.Cancel();
            if (_providersPending) await ApplyProvidersAsync();
        }
        catch (Exception) { if (!_closed) SetNotice(_active, L("ProviderOpenFailed", "无法读取提供商配置，请检查数据目录和 Windows 用户。"), true); }
        finally { _providerDialogOpen = false; }
    }
    private async Task ApplyProvidersAsync()
    {
        if (!_providersPending || _closed || _preparing) return;
        if (_sessions.Values.Append(_active).Any(s => s.Run is not null))
        {
            SetNotice(_active, L("ProviderDeferred", "提供商配置已保存，所有当前任务结束后自动应用。"), attention: true); return;
        }
        _providersPending = false;
        // Keep per-session drafts and messages; only restart this page's dedicated component.
        _client?.Dispose(); _client = new(); _models = []; ModelBox.ItemsSource = null;
        _lifetime ??= new(); await PrepareAsync();
    }
}

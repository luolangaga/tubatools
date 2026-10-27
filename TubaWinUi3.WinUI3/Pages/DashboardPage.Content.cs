using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class DashboardPage
{
    private async void ManageButton_Click(object sender, RoutedEventArgs e) => await ManageContentAsync(null);

    private async Task ManageContentAsync(HomeDashboardCard? selected)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            _catalog ??= await ToolCatalog.GetAllToolsAsync();
            if (XamlRoot is null) return;
            var options = new List<DashboardEditorOption>();
            void Add(string target, string category, bool external = false)
            {
                if (options.Any(o => o.Target == target)) return;
                var choice = Describe(target);
                bool hidden = target.StartsWith("builtin:") && !ToolVisibilityService.IsBuiltinEnabled(target[8..]);
                options.Add(new()
                {
                    Target = target, Name = choice.Name, Description = choice.Description, Category = category,
                    CategoryLabel = (external ? S("ExternalTools", "外部工具") : S("BuiltinTools", "内置工具")) + " · " + category +
                        (hidden ? " · " + S("HiddenInSettings", "已在设置隐藏") : ""),
                    External = external, FallbackGlyph = choice.Glyph, LoadIcon = () => GetEditorIconAsync(target)
                });
            }
            Add("hardware", S("System", "系统")); Add("qq", S("Community", "社区"));
            foreach (var tool in BuiltinToolRegistry.AllTools)
                if (HomeDashboardContent.IsVisible(new() { Target = "builtin:" + tool.Id }, RuntimeHelper.IsPackagedContext))
                    Add("builtin:" + tool.Id, LocalizationService.GetCategoryDisplayName(tool.Category));
            foreach (var tool in _catalog.Where(t => !t.IsBuiltinLink)) Add("tool:" + tool.RelativePath, tool.CategoryDisplay, true);
            // Keep configured entries even if their executable was moved or the tool is unavailable in this edition.
            foreach (var card in _cards) Add(card.Target, S("Configured", "已配置"), card.Target.StartsWith("tool:"));
            if (selected is not null) Add(selected.Target, S("Configured", "已配置"), selected.Target.StartsWith("tool:"));
            var draft = _cards.Select(c => c with { }).ToList();
            if (selected is not null && draft.All(c => c.Target != selected.Target)) draft.Add(selected with { });
            var root = XamlRoot;
            double width = Math.Max(240, root.Size.Width - 80);
            bool compactDialog = width < 720 || root.Size.Height < 560;
            double height = Math.Max(160, root.Size.Height - (compactDialog ? 136 : 192));
            var editor = new DashboardContentEditor(options.OrderBy(o => o.External).ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                draft, selected, width, height);
            var dialog = new ContentDialog
            {
                Title = S("EditFeatured", "编辑大卡片"),
                Content = editor,
                PrimaryButtonText = S("Save", "保存"), CloseButtonText = S("Cancel", "取消"), DefaultButton = ContentDialogButton.None,
                XamlRoot = XamlRoot, RequestedTheme = ThemeService.CurrentElementTheme
            };
            dialog.Resources["ContentDialogMaxWidth"] = width + 48;
            dialog.Resources["ContentDialogMaxHeight"] = Math.Max(240, root.Size.Height - 24);
            dialog.Resources["ContentDialogPadding"] = new Thickness(compactDialog ? 12 : 24);
            // Floating editor uses native in-app acrylic; Mica remains the window backdrop.
            dialog.Style = (Style)Resources["DashboardEditorMaterialStyle"];
            void ResizeEditor(XamlRoot sender, XamlRootChangedEventArgs args)
            {
                double nextWidth = Math.Max(240, sender.Size.Width - 80);
                bool nextCompact = nextWidth < 720 || sender.Size.Height < 560;
                double nextHeight = Math.Max(160, sender.Size.Height - (nextCompact ? 136 : 192));
                if (Math.Abs(editor.Width - nextWidth) < .5 && Math.Abs(editor.Height - nextHeight) < .5) return;
                editor.Resize(nextWidth, nextHeight);
                dialog.Resources["ContentDialogMaxWidth"] = nextWidth + 48;
                dialog.Resources["ContentDialogMaxHeight"] = Math.Max(240, sender.Size.Height - 24);
                dialog.Resources["ContentDialogPadding"] = new Thickness(nextCompact ? 12 : 24);
            }
            root.Changed += ResizeEditor;
            ContentDialogResult result;
            try { result = await dialog.ShowAsync(); }
            finally { root.Changed -= ResizeEditor; }
            if (result != ContentDialogResult.Primary) return;
            _cards = editor.GetCards();
            AppSettings.Set(HomeDashboardContent.SettingsKey, JsonSerializer.Serialize(_cards));
            RenderCards();
        }
        catch (Exception ex) { ShowStatus(S("EditFailed", "无法编辑首页内容"), ex.Message, InfoBarSeverity.Error); }
        finally { _dialogOpen = false; }
    }

    private async Task<ImageSource?> GetEditorIconAsync(string target)
    {
        if (_logos.TryGetValue(target, out var source)) return source;
        if (target is "hardware" or "qq") source = new SvgImageSource(new Uri($"ms-appx:///Assets/HomeTiles/{target}.svg"));
        else if (target.StartsWith("builtin:")) source = BuiltinIconService.Get(target[8..]);
        else
        {
            var tool = _catalog?.FirstOrDefault(t => "tool:" + t.RelativePath == target);
            if (tool is not null)
            {
                var path = tool.IconPath ?? await ToolIconService.ExtractIconToCacheAsync(tool.EffectivePath);
                if (path is not null) source = new BitmapImage(new Uri(path));
            }
        }
        if (source is not null) _logos[target] = source;
        return source;
    }
}

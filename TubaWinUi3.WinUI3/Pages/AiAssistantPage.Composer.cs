using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TubaWinUi3.Pages;

public sealed partial class AiAssistantPage
{
    private IntPtr _dropWindow;
    private readonly SemaphoreSlim _imports = new(1, 1);
    private string _attachmentSignature = "";
    private int _pendingImports;
    private bool _pasting;

    private void ConfigureComposerMenu()
    {
        var menu = new MenuFlyout();
        MenuFlyoutItem Item(string key, string title, string glyph, Action action)
        {
            var item = new MenuFlyoutItem { Text = L(key, title), Icon = new FontIcon { Glyph = glyph } };
            AutomationProperties.SetAutomationId(item, "AgentComposer" + key);
            item.Click += (_, _) => { try { action(); } catch (Exception ex) { ImportError(_active, ex); } };
            menu.Items.Add(item); return item;
        }
        var undo = Item("Undo", "撤销", "\uE7A7", () => PromptBox.Undo());
        var redo = Item("Redo", "重做", "\uE7A6", () => PromptBox.Redo());
        menu.Items.Add(new MenuFlyoutSeparator());
        var cut = Item("Cut", "剪切", "\uE8C6", () => PromptBox.CutSelectionToClipboard());
        var copy = Item("Copy", "复制", "\uE8C8", () => PromptBox.CopySelectionToClipboard());
        var paste = Item("Paste", "粘贴", "\uE77F", BeginPaste);
        var all = Item("SelectAll", "全选", "\uE8B3", () => PromptBox.SelectAll());
        menu.Opening += (_, _) =>
        {
            undo.IsEnabled = PromptBox.CanUndo; redo.IsEnabled = PromptBox.CanRedo;
            cut.IsEnabled = copy.IsEnabled = PromptBox.SelectionLength > 0; all.IsEnabled = PromptBox.Text.Length > 0;
            // TextBox.CanPasteClipboardContent only considers text. Our paste command also
            // accepts Explorer's file list and screenshot bitmaps.
            try
            {
                var data = Clipboard.GetContent();
                paste.IsEnabled = !_pasting && (data.Contains(StandardDataFormats.Text) || data.Contains(StandardDataFormats.StorageItems) || data.Contains(StandardDataFormats.Bitmap));
            }
            catch { paste.IsEnabled = true; }
        };
        PromptBox.ContextFlyout = menu;
    }

    private void AttachDropHandler()
    {
        DetachDropHandler();
        if (_owner is null) return;
        _dropWindow = WinRT.Interop.WindowNative.GetWindowHandle(_owner);
        Win32DropHelper.EnsureInstalled(_dropWindow);
        Win32DropHelper.EnablePasteMessages(_dropWindow);
        Win32DropHelper.WindowFilesDropped += Window_FilesDropped;
        Win32DropHelper.WindowPasteRequested += Window_PasteRequested;
    }
    private void DetachDropHandler()
    {
        Win32DropHelper.WindowFilesDropped -= Window_FilesDropped;
        Win32DropHelper.WindowPasteRequested -= Window_PasteRequested;
        _dropWindow = IntPtr.Zero;
    }
    private void Window_PasteRequested(Win32DropHelper.WindowPaste paste)
    {
        if (paste.Handled || !_visible || _closed || paste.Window != _dropWindow || PromptBox.FocusState == FocusState.Unfocused) return;
        paste.Handled = true; BeginPaste();
    }
    private void Window_FilesDropped(Win32DropHelper.WindowDrop drop)
    {
        if (drop.Handled || !_visible || _closed || drop.Window != _dropWindow) return;
        drop.Handled = true;
        var state = _active;
        DispatcherQueue.TryEnqueue(async () => await ImportPathsAsync(state, drop.Files));
    }
    private void Composer_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Bitmap)
            ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.Handled = true;
        e.DragUIOverride.Caption = L("DropAttachments", "添加图片或文件");
    }
    private async void Composer_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true; var defer = e.GetDeferral(); var state = _active;
        try { await ImportDataAsync(state, e.DataView, false, PromptBox.SelectionStart, PromptBox.SelectionLength, state.Draft); }
        catch (Exception ex) { ImportError(state, ex); }
        finally { defer.Complete(); }
    }
    private void Prompt_Paste(object sender, TextControlPasteEventArgs e) { e.Handled = true; BeginPaste(); }
    private async void BeginPaste()
    {
        if (_pasting || _closed) return;
        _pasting = true;
        var state = _active; var start = PromptBox.SelectionStart; var length = PromptBox.SelectionLength; var draft = PromptBox.Text;
        try
        {
            // Read on the UI thread while the editor owns focus. Do not use the Text property
            // setter: SelectedText preserves selection replacement, caret position and undo.
            var data = Clipboard.GetContent();
            await ImportDataAsync(state, data, true, start, length, draft);
        }
        catch (Exception ex) { ImportError(state, ex); }
        finally { _pasting = false; }
    }
    private async Task ImportDataAsync(SessionState state, DataPackageView data, bool textAllowed, int start, int length, string draft)
    {
        if (data.Contains(StandardDataFormats.StorageItems))
        {
            var items = await data.GetStorageItemsAsync();
            await ImportPathsAsync(state, items.Select(i => i.Path).ToArray()); return;
        }
        if (data.Contains(StandardDataFormats.Bitmap))
        {
            ++_pendingImports; UpdateButtons();
            try
            {
                using var source = await (await data.GetBitmapAsync()).OpenReadAsync();
                var path = NewImagePath();
                try { await SavePngAsync(source, path); await ImportPathsAsync(state, [path], clipboardImage: true); }
                finally { if (File.Exists(path)) File.Delete(path); }
            }
            finally { --_pendingImports; UpdateButtons(); }
            return;
        }
        if (!textAllowed || !data.Contains(StandardDataFormats.Text)) return;
        // WinUI's native editor normalizes line breaks to CR. Normalize before computing
        // the caret offset, otherwise CRLF clipboard text moves the caret too far.
        var text = (await data.GetTextAsync()).Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r');
        if (_closed) return;
        // A delayed clipboard response belongs to the original session. Never overwrite a
        // newer draft or insert into a different conversation after a switch.
        if (ReferenceEquals(state, _active) && PromptBox.Text == draft)
        {
            PromptBox.Select(start, length); PromptBox.SelectedText = text;
            PromptBox.Select(start + text.Length, 0); PromptBox.Focus(FocusState.Programmatic);
        }
        else
        {
            var at = state.Draft == draft ? Math.Min(start, state.Draft.Length) : state.Draft.Length;
            var remove = state.Draft == draft ? Math.Min(length, state.Draft.Length - at) : 0;
            state.Draft = state.Draft.Remove(at, remove).Insert(at, text);
            if (ReferenceEquals(state, _active)) { PromptBox.Text = state.Draft; PromptBox.Select(at + text.Length, 0); }
            CacheDraft(state); RefreshHistoryView();
        }
    }
    private async void AttachFiles_Click(object sender, RoutedEventArgs e)
    {
        var state = _active;
        try { await ImportPathsAsync(state, Win32Dialogs.PickOpenMultiple(L("AllFiles", "所有文件") + "\0*.*\0\0", L("AttachFiles", "引用本地文件"))); }
        catch (Exception ex) { ImportError(state, ex); }
    }
    private static string NewImagePath()
    {
        var root = Path.Combine(AiComponentService.Root, "Attachments"); Directory.CreateDirectory(root);
        return Path.Combine(root, Guid.NewGuid().ToString("N") + ".png");
    }
    private static async Task SavePngAsync(IRandomAccessStream source, string path)
    {
        // Clipboard bitmaps can be uncompressed: a normal 4K screenshot is about 32 MB.
        // Bound source allocation separately; the 20 MB limit applies to the encoded PNG.
        if (source.Size > 160 * 1024 * 1024) throw new IOException(L("ImageTooLarge", "单张图片不能超过 20 MB。"));
        var decoder = await BitmapDecoder.CreateAsync(source);
        if ((ulong)decoder.PixelWidth * decoder.PixelHeight > 40_000_000) throw new IOException(L("ImagePixels", "图片不能超过 4000 万像素。"));
        using var bitmap = await decoder.GetSoftwareBitmapAsync();
        using var output = File.Open(path, FileMode.CreateNew).AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap); await encoder.FlushAsync();
        if (output.Size > 20 * 1024 * 1024) throw new IOException(L("ImageTooLarge", "单张图片不能超过 20 MB。"));
    }
    private async Task ImportPathsAsync(SessionState state, IReadOnlyList<string> paths, bool clipboardImage = false)
    {
        ++_pendingImports; UpdateButtons(); await _imports.WaitAsync();
        try
        {
            var errors = new List<string>();
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_closed) return;
                try
                {
                    if (state.Attachments.Any(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
                    if (state.Attachments.Count >= 10) throw new IOException(L("AttachmentLimit", "每条消息最多添加 10 个附件。"));
                    if (!File.Exists(path)) throw new IOException(L("AttachmentMissing", "文件不存在或无法读取（暂不支持文件夹）。"));
                    var mime = AgentAttachment.ImageMime(path); var finalPath = Path.GetFullPath(path);
                    if (mime.Length > 0)
                    {
                        // Snapshot pasted/dropped pictures so clipboard/temp files can disappear
                        // without breaking a draft or the component's persisted history.
                        finalPath = NewImagePath();
                        try
                        {
                            using var source = File.OpenRead(path).AsRandomAccessStream();
                            await SavePngAsync(source, finalPath);
                        }
                        catch { if (File.Exists(finalPath)) File.Delete(finalPath); throw; }
                        if (state.Attachments.Where(a => a.IsImage).Sum(a => new FileInfo(a.Path).Length) + new FileInfo(finalPath).Length > 20 * 1024 * 1024)
                        { File.Delete(finalPath); throw new IOException(L("ImageTotalLimit", "每条消息的图片总大小不能超过 20 MB。")); }
                        mime = "image/png";
                    }
                    state.Attachments.Add(new(finalPath, clipboardImage ? L("ClipboardImage", "剪贴板图片") + ".png" : Path.GetFileName(path), mime));
                }
                catch (Exception ex) { errors.Add(Path.GetFileName(path) + ": " + ex.Message); }
            }
            CacheDraft(state); RefreshHistoryView();
            if (errors.Count > 0) SetNotice(state, string.Join("\n", errors), true);
        }
        finally { _imports.Release(); --_pendingImports; UpdateButtons(); }
    }
    private void ImportError(SessionState state, Exception ex)
    {
        if (!_closed) SetNotice(state, L("PasteFailed", "无法读取剪贴板或附件，请重新复制后重试。") + " " + ex.Message, true);
    }
    private void ClearAttachments_Click(object sender, RoutedEventArgs e)
    {
        foreach (var attachment in _active.Attachments) DiscardDraftImage(attachment);
        _active.Attachments.Clear(); CacheDraft(_active); RefreshHistoryView(); UpdateButtons();
    }
    private static void DiscardDraftImage(AgentAttachment attachment)
    {
        // Only private snapshots are owned here; never delete a file copied from Explorer.
        if (!attachment.IsImage || !string.Equals(Path.GetDirectoryName(attachment.Path), Path.Combine(AiComponentService.Root, "Attachments"), StringComparison.OrdinalIgnoreCase)) return;
        try { File.Delete(attachment.Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private void UpdateAttachments()
    {
        var signature = _active.Key + string.Join('|', _active.Attachments.Select(a => a.Path));
        if (signature == _attachmentSignature) return;
        _attachmentSignature = signature; AttachmentItems.Children.Clear();
        AttachmentSummary.Visibility = _active.Attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var state = _active;
        foreach (var attachment in state.Attachments)
        {
            var panel = AttachmentPreview(attachment);
            var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 10 }, Padding = new Thickness(4), MinWidth = 24, MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Right };
            AutomationProperties.SetName(remove, L("RemoveAttachment", "移除附件") + " " + attachment.Name);
            AutomationProperties.SetAutomationId(remove, "AgentRemoveAttachment" + state.Attachments.IndexOf(attachment));
            remove.Click += (_, _) => { state.Attachments.Remove(attachment); DiscardDraftImage(attachment); CacheDraft(state); RefreshHistoryView(); UpdateButtons(); };
            panel.Children.Add(remove); AttachmentItems.Children.Add(new Border { Child = panel, Padding = new Thickness(8), BorderThickness = new Thickness(1), BorderBrush = Composer.BorderBrush, CornerRadius = new CornerRadius(8) });
        }
    }
    private static StackPanel AttachmentPreview(AgentAttachment attachment)
    {
        var panel = new StackPanel { Width = 128, Spacing = 4 };
        if (attachment.IsImage && File.Exists(attachment.Path))
            panel.Children.Add(new Image { Source = new BitmapImage(new Uri(attachment.Uri)) { DecodePixelWidth = 256 }, Height = 72, Stretch = Stretch.UniformToFill });
        else panel.Children.Add(new FontIcon { Glyph = "\uE8A5", FontSize = 24, Height = 32 });
        panel.Children.Add(new TextBlock { Text = attachment.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        ToolTipService.SetToolTip(panel, attachment.Name + "\n" + attachment.Path);
        return panel;
    }
}

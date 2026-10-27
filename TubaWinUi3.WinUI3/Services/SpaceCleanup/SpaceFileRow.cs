using FluentCleaner.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace TubaWinUi3.Services.SpaceCleanup;

/// <summary>UI projection, created on the dispatcher. Children are materialized only when expanded.</summary>
public sealed partial class SpaceFileRow(SpaceNode node) : INotifyPropertyChanged
{
    public SpaceNode Node { get; } = node;
    public string Name => Node.Name + (Node.IsLink ? "（链接）" : "");
    public string Path => Node.FullPath;
    public string Size => ScanResult.FormatBytes(Node.Bytes);
    public double Usage { get; private set; }
    public string Percent => $"{Usage:F1}%";
    public string AccessibilityName => $"{Name}，{Size}，占当前目录 {Percent}";
    public string Kind => Node.IsDirectory ? $"{Node.FileCount:N0} 个文件" : System.IO.Path.GetExtension(Node.Name).TrimStart('.').ToUpperInvariant();
    public ImageSource IconSource => SpaceFileIcons.Get(Node);
    public SolidColorBrush Accent { get; private set; } = new();
    public ObservableCollection<SpaceFileRow> Children { get; } = [];
    // A file must have no child source. Supplying an empty collection to every recycled
    // TreeViewItem can leave a folder's expand glyph on a file container.
    public ObservableCollection<SpaceFileRow>? ChildItems => Node.IsDirectory && !Node.IsLink ? Children : null;
    public bool ChildrenLoaded { get; set; }
    public bool CanExpand => Node.IsDirectory && !Node.IsLink && (ChildrenLoaded ? Children.Count > 0 : Node.Children.Count > 0);
    public double ChevronOpacity => CanExpand ? 1 : 0;
    private bool _expanded;
    public bool Expanded { get => _expanded && CanExpand; set => _expanded = value && CanExpand; }
    public bool HasUnrealizedChildren => !ChildrenLoaded && CanExpand;
    private double _lastHue = double.NaN;
    private bool _lastDark;
    private long _lastBytes = -1, _lastFiles = -1;
    private int _lastChildren = -1;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(long total, bool dark, bool byType, SpaceNode? viewRoot)
    {
        double usage = total > 0 ? Math.Clamp(Node.Bytes * 100.0 / total, 0, 100) : 0;
        double hue = SpaceColorPalette.Hue(Node, byType, viewRoot);
        int children = Node.IsDirectory ? Node.Children.Count : 0;
        bool colorChanged = hue != _lastHue || dark != _lastDark;
        if (!colorChanged && Usage == usage && _lastBytes == Node.Bytes && _lastFiles == Node.FileCount && _lastChildren == children) return;
        Usage = usage; _lastBytes = Node.Bytes; _lastFiles = Node.FileCount; _lastChildren = children;
        if (colorChanged)
        {
            var color = SKColor.FromHsl((float)hue, 62, dark ? 65 : 45);
            Accent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, color.Red, color.Green, color.Blue));
            _lastHue = hue; _lastDark = dark;
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
    public void NotifyChildrenLoaded() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    public override string ToString() => AccessibilityName;
}

public static class SpaceFileIcons
{
    private static readonly Dictionary<string, ImageSource> Cache = [];
    public static string Kind(SpaceNode node) => node.IsDirectory ? "folder" : System.IO.Path.GetExtension(node.Name).ToLowerInvariant() switch
    {
        ".zip" or ".7z" or ".rar" or ".iso" or ".cab" => "archive",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" => "image",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "media",
        ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" => "audio",
        ".exe" or ".msi" or ".appx" or ".msix" => "app", ".sys" or ".dll" or ".drv" => "system",
        ".pdf" or ".docx" or ".doc" or ".xlsx" or ".pptx" or ".txt" or ".md" => "document",
        ".cs" or ".cpp" or ".h" or ".js" or ".ts" or ".py" or ".json" or ".xml" or ".html" => "code",
        _ => "file"
    };
    public static ImageSource Get(SpaceNode node)
    {
        string kind = Kind(node);
        if (!Cache.TryGetValue(kind, out var image)) Cache[kind] = image = new SvgImageSource(new Uri($"ms-appx:///Assets/SpaceFileIcons/{kind}.svg"));
        return image;
    }
}

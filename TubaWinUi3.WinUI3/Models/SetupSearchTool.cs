using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Services;

namespace TubaWinUi3.Models;

public sealed record SetupSearchCandidate(string Key, string Name, string Category, string? BuiltinId = null, string? IconPath = null, ToolItem? ExternalTool = null);

/// <summary>同名或跨分类的工具共用一项选择，保存时覆盖它们的全部稳定键。</summary>
public sealed class SetupSearchTool : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Keys { get; init; }
    public string? BuiltinId { get; init; }
    public string? IconPath { get; init; }
    public ToolItem? ExternalTool { get; init; }
    public ImageSource? Icon => BuiltinId is not null ? BuiltinIconService.Get(BuiltinId)
        : (ExternalTool?.IconPath ?? IconPath) is { Length: > 0 } path ? new BitmapImage(new Uri(path)) : null;
    public string IconGlyph => ExternalTool?.IconGlyph ?? "\uE8B7";
    public Visibility IconVisibility => Icon is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void ObserveIconChanges()
    {
        if (ExternalTool is not null) ExternalTool.PropertyChanged += OnToolPropertyChanged;
    }

    internal void StopIconUpdates()
    {
        if (ExternalTool is not null) ExternalTool.PropertyChanged -= OnToolPropertyChanged;
    }

    private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ToolItem.IconPath)) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GlyphVisibility)));
    }
}

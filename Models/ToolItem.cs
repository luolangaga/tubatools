using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TubaWinUi3.Models;

public sealed class ToolItem : INotifyPropertyChanged
{
    public required string Name { get; init; }

    public required string Category { get; init; }

    public required string Path { get; init; }

    public required string RelativePath { get; init; }

    public required string Extension { get; init; }

    private string? _iconPath;
    public string? IconPath
    {
        get => _iconPath;
        set => SetField(ref _iconPath, value);
    }

    private string? _iconGlyph;
    public string? IconGlyph
    {
        get => _iconGlyph;
        set => SetField(ref _iconGlyph, value);
    }

    public string? Description { get; init; }

    public string? Publisher { get; init; }

    public string? Version { get; init; }

    public string? DatabaseSource { get; init; }

    public string? DownloadUrl { get; init; }

    public string? DownloadFilter { get; init; }

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public string Folder => System.IO.Path.GetDirectoryName(RelativePath) ?? Category;

    public bool NeedsDownload => !string.IsNullOrWhiteSpace(DownloadUrl);

    public string? PrimaryArch { get; init; }

    public IReadOnlyList<ArchVariant> AlternateVersions { get; init; } = [];

    public bool HasAlternateVersions => AlternateVersions.Count > 0;

    public string LaunchButtonText
    {
        get
        {
            if (NeedsDownload)
                return "下载";
            if (PrimaryArch is not null)
                return $"打开（{PrimaryArch}）";
            return "打开";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ArchVariant
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Arch { get; init; }
}

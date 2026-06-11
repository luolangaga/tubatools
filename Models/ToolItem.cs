using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TubaWinUi3.Services;

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

    public string? RemoteUrl { get; init; }

    public string? DownloadFilter { get; init; }

    public string? WingetId { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string TagsText => Tags.Count > 0 ? string.Join("  ", Tags) : "";

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public string Folder => System.IO.Path.GetDirectoryName(RelativePath) ?? Category;

    public bool NeedsDownload => !string.IsNullOrWhiteSpace(DownloadUrl) || !string.IsNullOrWhiteSpace(WingetId) || !string.IsNullOrWhiteSpace(RemoteUrl);

    public bool NeedsRemoteDownload => !string.IsNullOrWhiteSpace(RemoteUrl) && !File.Exists(EffectivePath);

    public bool HasUpdateSource => !string.IsNullOrWhiteSpace(RemoteUrl) || !string.IsNullOrWhiteSpace(DownloadUrl);

    public bool NeedsWingetInstall => !string.IsNullOrWhiteSpace(WingetId);

    private bool _isWingetInstalled;
    public bool IsWingetInstalled
    {
        get => _isWingetInstalled;
        set
        {
            if (SetField(ref _isWingetInstalled, value))
            {
                OnPropertyChanged(nameof(LaunchButtonText));
                OnPropertyChanged(nameof(IsWingetInstalling));
                OnPropertyChanged(nameof(CanLaunch));
            }
        }
    }

    private bool _isWingetInstalling;
    public bool IsWingetInstalling
    {
        get => _isWingetInstalling;
        set
        {
            if (SetField(ref _isWingetInstalling, value))
            {
                OnPropertyChanged(nameof(LaunchButtonText));
                OnPropertyChanged(nameof(CanLaunch));
            }
        }
    }

    private int _wingetInstallProgress;
    public int WingetInstallProgress
    {
        get => _wingetInstallProgress;
        set => SetField(ref _wingetInstallProgress, value);
    }

    private string _wingetInstallStatus = "";
    public string WingetInstallStatus
    {
        get => _wingetInstallStatus;
        set => SetField(ref _wingetInstallStatus, value);
    }

    public bool CanLaunch => !IsWingetInstalling;

    public string? PrimaryArch { get; init; }

    public IReadOnlyList<ArchVariant> AlternateVersions { get; init; } = [];

    public bool HasAlternateVersions => AlternateVersions.Count > 0;

    public ObservableCollection<ArchOption> ArchOptions { get; } = [];

    private ArchOption? _selectedArch;
    public ArchOption? SelectedArch
    {
        get => _selectedArch;
        set
        {
            if (SetField(ref _selectedArch, value))
            {
                OnPropertyChanged(nameof(EffectivePath));
                OnPropertyChanged(nameof(EffectiveWorkingDir));
                OnPropertyChanged(nameof(LaunchButtonText));
            }
        }
    }

    public string EffectivePath => SelectedArch?.Path ?? Path;

    public string EffectiveWorkingDir =>
        System.IO.Path.GetDirectoryName(EffectivePath) ?? ToolCatalog.ToolsRoot;

    public string LaunchButtonText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DownloadUrl))
                return "下载";
            if (!string.IsNullOrWhiteSpace(WingetId))
            {
                if (IsWingetInstalling) return "安装中...";
                return IsWingetInstalled ? "打开" : "下载";
            }
            if (!string.IsNullOrWhiteSpace(RemoteUrl) && !File.Exists(EffectivePath))
                return "下载";
            return "打开";
        }
    }

    public void InitArchOptions()
    {
        ArchOptions.Clear();
        var primary = new ArchOption { Name = Name, Path = Path, Arch = PrimaryArch ?? "" };
        ArchOptions.Add(primary);
        foreach (var v in AlternateVersions)
        {
            ArchOptions.Add(new ArchOption { Name = v.Name, Path = v.Path, Arch = v.Arch });
        }
        var isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        var isX64 = Environment.Is64BitOperatingSystem && !isArm64;
        var preferred = ArchOptions.FirstOrDefault(a =>
            a.Arch.Equals("ARM64", StringComparison.OrdinalIgnoreCase) && isArm64)
            ?? ArchOptions.FirstOrDefault(a =>
                a.Arch.Equals("x64", StringComparison.OrdinalIgnoreCase) && isX64)
            ?? ArchOptions.FirstOrDefault(a =>
                a.Arch.Equals("x86", StringComparison.OrdinalIgnoreCase) && !Environment.Is64BitOperatingSystem)
            ?? primary;
        SelectedArch = preferred;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ArchVariant
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Arch { get; init; }
}

public sealed class ArchOption
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Arch { get; init; }

    public string DisplayText => string.IsNullOrEmpty(Arch) ? "默认" : Arch;

    public override string ToString() => DisplayText;
}

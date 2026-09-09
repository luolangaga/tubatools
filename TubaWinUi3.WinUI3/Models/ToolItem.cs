using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using TubaWinUi3.Services;

namespace TubaWinUi3.Models;

public sealed class ToolItem : INotifyPropertyChanged
{
    private IReadOnlyList<string> _categories = [];

    public required string Name { get; init; }

    public required string Category { get; init; }

    public string? PrimaryCategory { get; init; }

    public IReadOnlyList<string> Categories
    {
        get => _categories;
        init => _categories = value;
    }

    public bool IsLinked { get; init; }

    public bool IsBuiltinLink { get; init; }

    public string? BuiltinToolId { get; init; }

    public string? BuiltinKindText { get; init; }

    public string CategoriesDisplay => _categories.Count <= 1 ? "" : string.Join(" · ", _categories.Where(c => c != Category));

    public IReadOnlyList<string> OtherCategories => _categories.Where(c => !c.Equals(Category, StringComparison.OrdinalIgnoreCase)).ToList();

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

    public string? WingetId { get; init; }

    public string? RemoteUrl { get; init; }

    public string? TutorialUrl { get; init; }

    /// <summary>tools.json 的 order 字段：卡片排序主键（null = 未收录的自定义工具，排在后面）。</summary>
    public int? SortOrder { get; init; }

    public bool HasTutorial => !string.IsNullOrWhiteSpace(TutorialUrl);

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string TagsText => Tags.Count > 0 ? string.Join("  ", Tags) : "";

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public string Folder => System.IO.Path.GetDirectoryName(RelativePath) ?? Category;

    public bool NeedsDownload => !IsBuiltinLink && !File.Exists(EffectivePath) && (!string.IsNullOrWhiteSpace(DownloadUrl) || !string.IsNullOrWhiteSpace(WingetId));

    public bool HasUpdateSource => !string.IsNullOrWhiteSpace(DownloadUrl);

    public bool NeedsWingetInstall => !string.IsNullOrWhiteSpace(WingetId);

    /// <summary>内置工具（有注册 Id）也能发桌面快捷方式：以 --open-builtin 启动自身直达工具。</summary>
    public bool CanSendToDesktop => !IsBuiltinLink || !string.IsNullOrWhiteSpace(BuiltinToolId);

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

    public bool CanLaunch => IsBuiltinLink || !IsWingetInstalling;

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
            if (_suppressArchSelection) return;
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
            if (IsBuiltinLink) return "打开";
            if (!string.IsNullOrWhiteSpace(DownloadUrl) && !File.Exists(EffectivePath))
                return "下载";
            if (!string.IsNullOrWhiteSpace(WingetId))
            {
                if (IsWingetInstalling) return "安装中...";
                return IsWingetInstalled ? "打开" : "下载";
            }
            return "打开";
        }
    }

    public void SetCategories(IReadOnlyList<string> categories)
    {
        _categories = categories;
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(CategoriesDisplay));
        OnPropertyChanged(nameof(OtherCategories));
    }

    private bool _suppressArchSelection;

    public void InitArchOptions()
    {
        _suppressArchSelection = true;
        ArchOptions.Clear();
        var primary = new ArchOption { Name = Name, Path = Path, Arch = PrimaryArch ?? "" };
        ArchOptions.Add(primary);
        foreach (var v in AlternateVersions)
        {
            ArchOptions.Add(new ArchOption { Name = v.Name, Path = v.Path, Arch = v.Arch });
        }
        _suppressArchSelection = false;
        SelectedArch = ToolCatalog.PickPreferredArchOption(ArchOptions, primary);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static DispatcherQueue? _uiDispatcher;

    public static void SetUIDispatcher(DispatcherQueue dispatcher) => _uiDispatcher = dispatcher;

    private void RaisePropertyChanged(string propertyName)
    {
        var handler = PropertyChanged;
        if (handler is null) return;

        if (_uiDispatcher is not null && !_uiDispatcher.HasThreadAccess)
        {
            _uiDispatcher.TryEnqueue(() => handler.Invoke(this, new PropertyChangedEventArgs(propertyName)));
        }
        else
        {
            handler.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(propertyName!);
        return true;
    }

    private void OnPropertyChanged(string propertyName) => RaisePropertyChanged(propertyName);
}

public sealed class ArchVariant
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Arch { get; init; }
}

public sealed class ArchOption : IEquatable<ArchOption>
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Arch { get; init; }

    public string DisplayText => string.IsNullOrEmpty(Arch) ? "默认" : Arch;

    public override string ToString() => DisplayText;

    public bool Equals(ArchOption? other) =>
        other is not null && Path.Equals(other.Path, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ArchOption);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Path);
}

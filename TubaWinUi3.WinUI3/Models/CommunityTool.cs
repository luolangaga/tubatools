using System.ComponentModel;
using Microsoft.UI.Xaml;
using TubaWinUi3.Services;

namespace TubaWinUi3.Models;

public sealed class CommunityTool : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public required string Category { get; init; }
    public string? Publisher { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public string? Icon { get; init; }
    public string? DownloadUrl { get; init; }
    public string? DownloadFilter { get; init; }
    public string? LaunchTarget { get; init; }
    public IReadOnlyList<CommunityArchVariant>? ArchVariants { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
    public string? Homepage { get; init; }
    public string? RepoPath { get; init; }
    public string? File { get; init; }
    public string? FileSha { get; set; }

    public string TagsText => string.Join("  ", Tags);

    /// <summary>卡片上的分类显示名（本地化；Category 本身是数据键，不可翻译）。</summary>
    public string CategoryDisplay => LocalizationService.GetCategoryDisplayName(Category);

    private CommunityToolInstallStatus _installStatus;
    public CommunityToolInstallStatus InstallStatus
    {
        get => _installStatus;
        set { _installStatus = value; NotifyDerived(); }
    }

    public bool CanInstall => InstallStatus is CommunityToolInstallStatus.NotInstalled or CommunityToolInstallStatus.UpdateAvailable;
    public bool CanLaunch => InstallStatus == CommunityToolInstallStatus.Installed;
    public bool CanUninstall => InstallStatus != CommunityToolInstallStatus.NotInstalled;

    public string InstallStatusText => InstallStatus switch
    {
        CommunityToolInstallStatus.NotInstalled => "未安装",
        CommunityToolInstallStatus.Installed => "已安装",
        CommunityToolInstallStatus.UpdateAvailable => "可更新",
        _ => "未知"
    };

    private bool _isBusy;
    public bool IsBusy => _isBusy;

    private string _busyText = "";
    public string BusyText => _busyText;

    /// <summary>由页面对接下载队列设置：忙碌时药丸与主按钮显示进度文本（下载中 45% / 安装中…）。</summary>
    public void SetBusy(bool busy, string busyText = "")
    {
        _isBusy = busy;
        _busyText = busy ? busyText : "";
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(BusyText));
        NotifyDerived();
    }

    /// <summary>卡片药丸文本：忙碌时显示进度，否则显示安装状态。</summary>
    public string StatusText => IsBusy && !string.IsNullOrWhiteSpace(BusyText) ? BusyText : InstallStatusText;

    public string LaunchButtonText => IsBusy
        ? (string.IsNullOrWhiteSpace(BusyText) ? "处理中..." : BusyText)
        : InstallStatus switch
        {
            CommunityToolInstallStatus.Installed => "打开",
            CommunityToolInstallStatus.UpdateAvailable => "更新",
            _ => "下载"
        };

    public bool CanPrimaryAct => !IsBusy && (CanInstall || CanLaunch);

    private string? _iconPath;
    public string? IconPath
    {
        get => _iconPath;
        set { _iconPath = value; OnPropertyChanged(nameof(IconPath)); }
    }

    private bool _isAuthor;
    public bool IsAuthor
    {
        get => _isAuthor;
        set { if (_isAuthor != value) { _isAuthor = value; NotifyDerived(); } }
    }

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (_isFavorite != value) { _isFavorite = value; OnPropertyChanged(nameof(IsFavorite)); } }
    }

    public Visibility UninstallButtonVisibility => !IsBusy && CanUninstall ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>申请下架（远程 PR，仅作者）。</summary>
    public Visibility RemoveRequestButtonVisibility => IsAuthor ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>收藏星（仅已安装的工具可收藏：收藏键是本地工具路径）。</summary>
    public Visibility FavoriteButtonVisibility => CanUninstall ? Visibility.Visible : Visibility.Collapsed;

    public string? IconGlyph
    {
        get
        {
            if (Category.Contains("处理器")) return "\uEEA1";
            if (Category.Contains("显卡")) return "\uF211";
            if (Category.Contains("显示器")) return "\uE7F4";
            if (Category.Contains("硬盘")) return "\uEDA2";
            if (Category.Contains("内存")) return "\uEEA0";
            if (Category.Contains("外设")) return "\uE962";
            if (Category.Contains("游戏")) return "\uE7FC";
            if (Category.Contains("声卡")) return "\uE7F5";
            if (Category.Contains("网卡")) return "\uEDA3";
            if (Category.Contains("综合")) return "\uEC4E";
            if (Category.Contains("系统")) return "\uE977";
            return "\uE8B7";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(InstallStatus));
        OnPropertyChanged(nameof(InstallStatusText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(CanPrimaryAct));
        OnPropertyChanged(nameof(LaunchButtonText));
        OnPropertyChanged(nameof(UninstallButtonVisibility));
        OnPropertyChanged(nameof(RemoveRequestButtonVisibility));
        OnPropertyChanged(nameof(FavoriteButtonVisibility));
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CommunityArchVariant
{
    public required string File { get; init; }
    public required string Arch { get; init; }
}

public enum CommunityToolInstallStatus
{
    NotInstalled,
    Installed,
    UpdateAvailable
}

/// <summary>提交社区工具的表单数据（plugin.json 的唯一数据来源，提交上传与预览共用）。</summary>
public sealed record CommunityPluginDraft(
    string Name,
    string Description,
    string Category,
    IReadOnlyList<string> Tags,
    string? ZipFilePath,
    string? LaunchTarget,
    string? Publisher,
    string? Homepage,
    string? Version,
    string? IconFilePath,
    string? DownloadUrl,
    string? DownloadFilter,
    IReadOnlyList<ImportArchVariant> ArchVariants);

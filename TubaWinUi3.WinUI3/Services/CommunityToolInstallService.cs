using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// 社区工具的本地安装管线：队列安装（下载 → 原子安装 → 写 tools.json/安装记录）、
/// 状态与更新检测、卸载、启动。页面只与这里打交道，不再自己拼下载/解压逻辑。
/// </summary>
public static class CommunityToolInstallService
{
    /// <summary>状态判定（纯函数，可单测）：目录无文件=未安装；本地记录 sha 与远端 sha 都有且不同=可更新。</summary>
    internal static CommunityToolInstallStatus ResolveStatus(bool dirInstalled, string? localSha, string? remoteSha)
    {
        if (!dirInstalled)
            return CommunityToolInstallStatus.NotInstalled;

        if (!string.IsNullOrWhiteSpace(localSha) &&
            !string.IsNullOrWhiteSpace(remoteSha) &&
            !string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase))
            return CommunityToolInstallStatus.UpdateAvailable;

        return CommunityToolInstallStatus.Installed;
    }

    public static string GetToolDirectory(CommunityTool tool) =>
        Path.Combine(ToolCatalog.ToolsRoot, tool.Category, tool.Id);

    public static bool IsInstalledOnDisk(CommunityTool tool)
    {
        try
        {
            var toolDir = GetToolDirectory(tool);
            return Directory.Exists(toolDir) &&
                   Directory.EnumerateFileSystemEntries(toolDir).Any();
        }
        catch
        {
            return false;
        }
    }

    public static CommunityToolInstallStatus CheckStatus(CommunityTool tool)
    {
        var record = CommunityToolRegistry.TryGet(tool.Id);
        return ResolveStatus(IsInstalledOnDisk(tool), record?.Sha, tool.FileSha);
    }

    /// <summary>清理工具目录已不存在的安装记录（惰性维护，页面加载时调用一次）。</summary>
    public static int PruneStaleRecords() => CommunityToolRegistry.PruneStale();

    /// <summary>把安装加入全局下载队列（含重复守卫：同一工具的进行中任务直接复用）。</summary>
    public static DownloadItem EnqueueInstall(CommunityTool tool)
    {
        var existing = FindActiveItem(tool);
        if (existing is not null)
            return existing;

        var toolsRoot = ToolCatalog.ToolsRoot
            ?? throw new InvalidOperationException("无法找到工具目录");
        var toolDir = Path.Combine(toolsRoot, tool.Category, tool.Id);

        var sources = CommunityToolService.GetAllDownloadUrls(tool);
        if (sources.Count == 0)
            throw new InvalidOperationException("该工具没有提供下载源");

        var isFileBased = !string.IsNullOrWhiteSpace(tool.File);
        var communityFile = tool.File;
        var downloadUrl = tool.DownloadUrl ?? string.Empty;
        var downloadFilter = tool.DownloadFilter;

        if (!isFileBased && downloadUrl.Length == 0)
            throw new InvalidOperationException("该工具没有提供下载源");

        Func<CancellationToken, Task<ResolvedDownloadUrl>> resolver = async ct =>
        {
            if (isFileBased)
            {
                // 文件型：GitCode 镜像直链（索引命中时带 sha，确定性地址）；失败由 fallbackUrl 兜底 GitHub
                var (_, url) = sources[0];
                return new ResolvedDownloadUrl(url, communityFile!);
            }

            var info = await ToolDownloaderService.ResolveDownloadUrlAsync(downloadUrl, downloadFilter, ct)
                ?? throw new InvalidOperationException("无法解析下载链接（gh: 展开失败或该链接类型不支持），请稍后重试或联系工具作者");
            return new ResolvedDownloadUrl(info.DownloadUrl, info.FileName, info.Size);
        };

        var request = new CommunityToolInstallRequest(
            tool.Id, tool.Name, tool.Category,
            tool.Description, tool.Publisher, tool.Tags,
            tool.LaunchTarget, tool.Version, tool.Author,
            tool.RepoPath, tool.File, tool.FileSha);

        return DownloadQueueService.EnqueueWithResolver(
            displayName: tool.Name,
            urlResolver: resolver,
            destinationPath: toolDir,
            postProcessor: new CommunityToolInstallProcessor(request),
            description: tool.Description,
            glyph: tool.IconGlyph,
            tag: tool,
            fallbackUrl: isFileBased && sources.Count > 1 ? sources[1].Url : null);
    }

    /// <summary>该工具当前在队列中的进行中任务（排队/解析/下载/暂停/安装），没有则返回 null。</summary>
    public static DownloadItem? FindActiveItem(CommunityTool tool)
    {
        try
        {
            foreach (var item in DownloadQueueService.Queue)
            {
                if (item.Tag is not CommunityTool queued ||
                    !string.Equals(queued.Id, tool.Id, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (item.State is DownloadItemState.Queued
                    or DownloadItemState.Resolving
                    or DownloadItemState.Downloading
                    or DownloadItemState.Paused
                    or DownloadItemState.Processing)
                    return item;
            }
        }
        catch { }

        return null;
    }

    public static void CancelInstall(CommunityTool tool)
    {
        var item = FindActiveItem(tool);
        if (item is not null)
            DownloadQueueService.Cancel(item.Id);
    }

    /// <summary>
    /// 卸载：删除工具目录（重试 + 清只读）、收藏、tools.json 条目与安装记录，并刷新分类。
    /// 目录被占用时抛 IOException（调用方展示原因）。
    /// </summary>
    public static async Task UninstallAsync(CommunityTool tool)
    {
        var toolsRoot = ToolCatalog.ToolsRoot;
        var toolDir = Path.Combine(toolsRoot, tool.Category, tool.Id);
        var launchPath = ResolveLaunchPath(tool);

        if (Directory.Exists(toolDir))
        {
            var deleted = await Task.Run(() => DeleteDirectoryWithRetry(toolDir));
            if (!deleted)
                throw new IOException("工具目录被占用或只读，请关闭正在运行的工具后重试。");

            // 分类目录空了就一并移除（与首页删除工具的行为一致）
            try
            {
                var categoryDir = Path.GetDirectoryName(toolDir);
                if (!string.IsNullOrWhiteSpace(categoryDir) &&
                    Directory.Exists(categoryDir) &&
                    !Directory.EnumerateFileSystemEntries(categoryDir).Any())
                {
                    Directory.Delete(categoryDir, false);
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(launchPath))
            FavoritesService.RemoveFavorite(launchPath);

        // RemoveMetadataAsync 按路径所在目录名匹配条目；目录已删除也安全（只取名字）
        await ToolMetadataService.RemoveMetadataAsync(Path.Combine(toolDir, tool.Id + ".exe"));
        CommunityToolRegistry.Remove(tool.Id);

        ToolCatalog.RefreshToolsRoot();
        if (App.MainWindow is MainWindow mainWindow)
            mainWindow.DispatcherQueue.TryEnqueue(mainWindow.RefreshToolCategories);
    }

    /// <summary>工具在目录缓存中的完整工具项（含 launchTarget/架构选择/图标），未收录或未安装时返回 null。</summary>
    public static ToolItem? GetCatalogItem(CommunityTool tool)
    {
        try
        {
            var toolDir = GetToolDirectory(tool);
            var prefix = toolDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return ToolCatalog.GetTools(tool.Category)
                .FirstOrDefault(item => item.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>解析工具主程序路径：优先用目录缓存的完整工具项（launchTarget/架构选择），回退磁盘扫描。</summary>
    public static string? ResolveLaunchPath(CommunityTool tool)
    {
        var item = GetCatalogItem(tool);
        if (item is not null && File.Exists(item.EffectivePath))
            return item.EffectivePath;

        return FindLaunchPathOnDisk(tool);
    }

    private static string? FindLaunchPathOnDisk(CommunityTool tool)
    {
        try
        {
            var toolDir = GetToolDirectory(tool);
            if (!Directory.Exists(toolDir)) return null;

            var launchTarget = tool.LaunchTarget;
            if (!string.IsNullOrWhiteSpace(launchTarget))
            {
                var direct = Path.Combine(toolDir, launchTarget);
                if (File.Exists(direct)) return direct;

                var found = Directory.GetFiles(toolDir, launchTarget, SearchOption.AllDirectories);
                if (found.Length > 0) return found[0];
            }

            var exes = Directory.GetFiles(toolDir, "*.exe", SearchOption.AllDirectories);
            return exes.Length > 0 ? exes[0] : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>启动已安装的社区工具（与常规工具同一条启动路径：管理员/工作目录/启动历史）。</summary>
    public static void Launch(CommunityTool tool, bool runAsAdmin)
    {
        var path = ResolveLaunchPath(tool)
            ?? throw new InvalidOperationException("找不到工具主程序，可能已被移动或删除，请重新安装。");

        ToolProcessLauncher.Launch(path, Path.GetDirectoryName(path), runAsAdmin);
        LaunchHistoryService.RecordLaunch(path);
    }

    private static bool DeleteDirectoryWithRetry(string dir)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                ZipExtractHelper.TryClearReadOnlyAttributes(dir);
                Directory.Delete(dir, true);
                return true;
            }
            catch
            {
                if (attempt < 3) Thread.Sleep(300);
            }
        }
        return !Directory.Exists(dir);
    }
}

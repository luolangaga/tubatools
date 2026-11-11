using Microsoft.UI.Dispatching;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public sealed record ToolUpdateEntry(
    string ToolName,
    string Match,
    int LocalVersion,
    int RemoteVersion,
    string RepoPath,
    string Category,
    string ToolDir);

public static class ToolUpdateService
{
    private static DispatcherQueue? _dispatcherQueue;

    public static void Initialize(DispatcherQueue dq)
    {
        _dispatcherQueue = dq;
    }

    public static async Task<List<ToolUpdateEntry>?> CheckForToolUpdatesAsync(CancellationToken ct = default)
    {
        var remoteVersions = await ToolMetadataService.FetchRemoteToolsJsonAsync(ct);
        if (remoteVersions is null) return null;

        var updates = new List<ToolUpdateEntry>();
        var usedMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = ToolCatalog.GetCategories();

        foreach (var category in categories)
        {
            var tools = ToolCatalog.GetTools(category);
            foreach (var tool in tools)
            {
                if (string.IsNullOrWhiteSpace(tool.DownloadUrl)) continue;

                var toolDir = Path.GetDirectoryName(tool.Path);
                if (string.IsNullOrEmpty(toolDir) || !Directory.Exists(toolDir)) continue;

                var localVersion = ToolMetadataService.GetToolVersionByDir(toolDir);
                if (!localVersion.HasValue) continue;

                var remoteMatch = FindRemoteMatch(remoteVersions, tool);
                if (remoteMatch is null) continue;

                if (usedMatches.Contains(remoteMatch.Match)) continue;

                if (remoteMatch.Version > localVersion.Value)
                {
                    var repoPath = ResolveRepoPath(tool.DownloadUrl);
                    if (repoPath is null) continue;

                    usedMatches.Add(remoteMatch.Match);
                    updates.Add(new ToolUpdateEntry(
                        tool.Name,
                        remoteMatch.Match,
                        localVersion.Value,
                        remoteMatch.Version,
                        repoPath,
                        category,
                        toolDir));
                }
            }
        }

        return updates;
    }

    private static string? ResolveRepoPath(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl)) return null;
        if (downloadUrl.StartsWith("gc:", StringComparison.OrdinalIgnoreCase))
            return "TubaWinUi3.WinUI3/" + downloadUrl[3..];
        if (downloadUrl.StartsWith("gh:", StringComparison.OrdinalIgnoreCase))
            return "TubaWinUi3.WinUI3/" + downloadUrl[3..];
        return null;
    }

    private static RemoteToolVersion? FindRemoteMatch(
        IReadOnlyList<RemoteToolVersion> remoteVersions, ToolItem tool)
    {
        var fileName = Path.GetFileNameWithoutExtension(tool.Path);
        var dirName = tool.Folder ?? tool.Name;
        var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, tool.Path);

        RemoteToolVersion? bestMatch = null;

        foreach (var rv in remoteVersions)
        {
            if (string.IsNullOrWhiteSpace(rv.Match)) continue;

            if (fileName.Contains(rv.Match, StringComparison.CurrentCultureIgnoreCase) ||
                relativePath.Contains(rv.Match, StringComparison.CurrentCultureIgnoreCase) ||
                dirName.Contains(rv.Match, StringComparison.CurrentCultureIgnoreCase))
            {
                if (bestMatch is null || rv.Match.Length > bestMatch.Match.Length)
                    bestMatch = rv;
            }
        }

        return bestMatch;
    }

    public static void EnqueueToolUpdates(List<ToolUpdateEntry> updates)
    {
        foreach (var update in updates)
        {
            var updateCopy = update;
            var processor = new ToolUpdatePostProcessor(updateCopy);

            DownloadQueueService.EnqueueMultiFile(
                displayName: $"更新 {update.ToolName} (v{update.RemoteVersion})",
                multiFileResolver: ct => BuildFileListAsync(updateCopy, ct),
                destinationPath: update.ToolDir,
                postProcessor: processor,
                description: $"v{update.LocalVersion} → v{update.RemoteVersion}",
                glyph: "\uE895");
        }
    }

    private static async Task<List<ResolvedDownloadUrl>> BuildFileListAsync(ToolUpdateEntry update, CancellationToken ct)
        => await BuildFileListAsync(update, ToolDownloaderService.ListGitCodeDirAsync,
            ToolDownloaderService.ListGitHubDirAsync, ct);

    internal static async Task<List<ResolvedDownloadUrl>> BuildFileListAsync(ToolUpdateEntry update,
        Func<string, CancellationToken, Task<List<(string RelPath, string Sha, string FileName)>?>> listGitCode,
        Func<string, CancellationToken, Task<List<(string RelPath, string Sha, string FileName)>?>> listGitHub,
        CancellationToken ct)
    {
        var remoteFiles = await listGitCode(update.RepoPath, ct);
        var useGitHub = false;
        if (remoteFiles is null || remoteFiles.Count == 0)
        {
            ct.ThrowIfCancellationRequested();
            remoteFiles = await listGitHub(update.RepoPath, ct);
            useGitHub = true;
            if (remoteFiles is null || remoteFiles.Count == 0)
                throw new InvalidOperationException("无法获取远程文件列表");
        }

        var result = new List<ResolvedDownloadUrl>();
        foreach (var (relPath, sha, fileName) in remoteFiles)
        {
            var localPath = Path.Combine(update.ToolDir, relPath);
            if (File.Exists(localPath))
            {
                try
                {
                    var localContent = await File.ReadAllBytesAsync(localPath, ct);
                    if (ToolDownloaderService.ComputeBlobSha(localContent) == sha)
                        continue;
                }
                catch { }
            }

            // GitHub 的 blob SHA 不能用于 GitCode 镜像（镜像可能尚未同步）。
            var blobUrl = useGitHub
                ? $"https://raw.githubusercontent.com/luolangaga/tubatool/master/{string.Join('/', (update.RepoPath + "/" + relPath).Split('/').Select(Uri.EscapeDataString))}"
                : $"https://raw.gitcode.com/luolangaga/tubatool/blobs/{sha}/{Uri.EscapeDataString(fileName)}";
            result.Add(new ResolvedDownloadUrl(blobUrl, relPath));
        }

        return result;
    }

    private sealed class ToolUpdatePostProcessor : IDownloadPostProcessor
    {
        private readonly ToolUpdateEntry _update;
        public string DisplayName => $"更新完成 {_update.ToolName}";

        public ToolUpdatePostProcessor(ToolUpdateEntry update) => _update = update;

        public Task ExecuteAsync(string downloadedFilePath, string destinationPath,
            IProgress<string>? statusProgress, CancellationToken ct)
        {
            ToolMetadataService.UpdateToolVersion(_update.ToolDir, _update.RemoteVersion);
            ToolCatalog.InvalidateTagsCache();
            ToolMetadataService.InvalidateCache();
            statusProgress?.Report("更新完成");
            return Task.CompletedTask;
        }
    }
}

using System.Net.Http;
using System.Text;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public enum CommunityDataSource
{
    GitCode,
    GitHub
}

public static class CommunityToolService
{
    private const string UpstreamOwner = "luolangaga";
    private const string UpstreamRepo = "tubatoolsPlugin";
    private const string PluginsPath = "plugins";
    private const string GitCodeOwner = "luolangaga";
    private const string GitCodeRepo = "tubatoolsPlugin";
    private const string GitCodeRawBase = $"https://gitcode.com/{GitCodeOwner}/{GitCodeRepo}/-/raw/main";
    private const string GitCodeApiBase = $"https://api.gitcode.com/api/v5/repos/{GitCodeOwner}/{GitCodeRepo}";
    private const string GitHubApiBase = $"https://api.github.com/repos/{UpstreamOwner}/{UpstreamRepo}";
    private const string PluginIndexFile = "plugins-index.json";

    public static CommunityDataSource CurrentSource { get; set; } = CommunityDataSource.GitCode;

    private static string ApiBase => CurrentSource == CommunityDataSource.GitCode ? GitCodeApiBase : GitHubApiBase;

    private static List<CommunityTool>? _cache;
    private static DateTimeOffset _cacheTime;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private static readonly Dictionary<string, CommunityTool> _detailCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _shaMapCache = new(StringComparer.OrdinalIgnoreCase);

    public static void InvalidateCache()
    {
        _cache = null;
        _cacheTime = DateTimeOffset.MinValue;
        _detailCache.Clear();
        _shaMapCache.Clear();
    }

    public static async Task<List<CommunityTool>> GetPluginsAsync(int page = 1, int perPage = 30, CancellationToken ct = default)
    {
        if (_cache is not null && (DateTimeOffset.UtcNow - _cacheTime) < CacheDuration)
            return _cache;

        var tools = new List<CommunityTool>();

        try
        {
            // 优先读上游生成的 plugins-index.json（1 次请求拿全量工具+文件 sha），
            // 未生成/失败时回退到目录树遍历
            var indexTools = await TryGetPluginsFromIndexAsync(ct);
            if (indexTools is not null)
            {
                tools = indexTools;
            }
            else if (CurrentSource == CommunityDataSource.GitCode)
            {
                tools = await GetPluginsFromGitCodeAsync(ct);
            }
            else
            {
                tools = await GetPluginsFromGitHubAsync(ct);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 失败不写缓存：否则接下来的 10 分钟都会把失败当"空社区"返回，页面无法重试
            throw new InvalidOperationException($"加载社区工具失败：{ex.Message}", ex);
        }

        _cache = tools;
        _cacheTime = DateTimeOffset.UtcNow;
        return tools;
    }

    private static async Task<List<CommunityTool>> GetPluginsFromGitCodeAsync(CancellationToken ct)
    {
        var tools = new List<CommunityTool>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1 次请求：contents 一次列出全部分类目录，且每个目录带 tree sha
        var categoriesJson = await GetStringAsync($"{GitCodeApiBase}/contents/{PluginsPath}", ct);
        var catDoc = JsonDocument.Parse(categoriesJson);

        foreach (var catItem in catDoc.RootElement.EnumerateArray())
        {
            if (catItem.GetProperty("type").GetString() != "dir") continue;
            var category = catItem.GetProperty("name").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(category)) continue;

            // 每分类 1 次 recursive 树请求：一次拿全该分类的工具列表 + 所有文件 sha。
            // 请求数与旧 contents 方案相同，但顺带填充 _shaMapCache，
            // 之后详情加载只需 1 次 blob 请求、下载可直连 blob sha。
            var treeSha = catItem.TryGetProperty("sha", out var s) ? s.GetString() ?? "" : "";
            var entries = await EnumerateCategoryEntriesAsync(category, treeSha, ct);

            foreach (var (path, sha, type) in entries)
            {
                if (type == "blob" && !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(sha))
                {
                    _shaMapCache[$"{PluginsPath}/{category}/{path}"] = sha;
                }

                // 只取分类下第一层目录作为工具（跳过更深层子目录）
                if (type != "tree" || path.Contains('/')) continue;

                var key = $"{category}/{path}";
                if (!seen.Add(key)) continue;

                tools.Add(new CommunityTool
                {
                    Id = path,
                    Name = path,
                    Category = category,
                    RepoPath = $"{PluginsPath}/{category}/{path}",
                    Tags = [],
                });
            }
        }

        return tools;
    }

    /// <summary>
    /// 枚举一个分类下的条目（路径相对该分类目录）。
    /// 优先用 recursive 树接口一次拿全（含文件 sha）；无 tree sha 时回退旧 contents 列目录。
    /// </summary>
    private static async Task<List<(string Path, string Sha, string Type)>> EnumerateCategoryEntriesAsync(
        string category, string? treeSha, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(treeSha))
            return await EnumerateTreeRecursiveAsync(treeSha, ct);

        var entries = new List<(string Path, string Sha, string Type)>();
        var encCategory = Uri.EscapeDataString(category);
        var json = await GetStringAsync(
            $"{GitCodeApiBase}/contents/{PluginsPath}/{encCategory}", ct);
        var doc = JsonDocument.Parse(json);

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.GetProperty("type").GetString() != "dir") continue;
            var name = item.GetProperty("name").GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(name))
                entries.Add((name, "", "tree"));
        }
        return entries;
    }

    private static async Task<List<CommunityTool>> GetPluginsFromGitHubAsync(CancellationToken ct)
    {
        var tools = new List<CommunityTool>();

        // 获取最新 commit 的 tree sha（1 次请求）
        var treeSha = await GetLatestTreeShaAsync(ct);
        if (treeSha is null) return tools;

        // 获取 plugins 子树的 sha（1 次请求）
        var pluginsTreeSha = await GetSubTreeShaAsync(treeSha, PluginsPath, ct);
        if (pluginsTreeSha is null) return tools;

        // 用 recursive=1 一次获取整棵 plugins 子树（1 次请求，替代原来的 N+1+T 次请求）
        var allEntries = await EnumerateTreeRecursiveAsync(pluginsTreeSha, ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in allEntries)
        {
            // 缓存所有文件的 SHA，供 LoadToolDetailAsync 使用
            if (entry.Type == "blob" && !string.IsNullOrWhiteSpace(entry.Path) && !string.IsNullOrWhiteSpace(entry.Sha))
            {
                var fullPath = $"{PluginsPath}/{entry.Path}";
                _shaMapCache[fullPath] = entry.Sha;
            }

            if (entry.Type != "tree") continue;

            var parts = entry.Path.Split('/', 2);
            if (parts.Length != 2) continue;

            var category = parts[0];
            var toolDir = parts[1];

            // 跳过更深层的子目录（如 category/tool/subdir）
            if (toolDir.Contains('/')) continue;

            var key = $"{category}/{toolDir}";
            if (!seen.Add(key)) continue;

            tools.Add(new CommunityTool
            {
                Id = toolDir,
                Name = toolDir,
                Category = category,
                RepoPath = $"{PluginsPath}/{category}/{toolDir}",
                Tags = [],
            });
        }

        return tools;
    }

    /// <summary>
    /// 尝试从上游生成的 plugins-index.json 加载全量工具列表（1 次请求）。
    /// 索引条目 = plugin.json 全量元信息 + repoPath，且 blobs 映射含全部文件 sha，
    /// 因而列表即详情：点卡片/下载不再需要任何额外请求。失败返回 null 由调用方回退。
    /// </summary>
    private static async Task<List<CommunityTool>?> TryGetPluginsFromIndexAsync(CancellationToken ct)
    {
        try
        {
            var json = await GetPluginIndexContentAsync(ct);
            if (json is null) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tools", out var toolsEl) || toolsEl.ValueKind != JsonValueKind.Array)
                return null;

            if (root.TryGetProperty("blobs", out var blobsEl) && blobsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in blobsEl.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String)
                        _shaMapCache[p.Name] = p.Value.GetString()!;
                }
            }

            var tools = new List<CommunityTool>();
            foreach (var item in toolsEl.EnumerateArray())
            {
                var repoPath = item.TryGetProperty("repoPath", out var rp) ? rp.GetString() : null;
                if (string.IsNullOrWhiteSpace(repoPath)) continue;
                var parts = repoPath.Split('/');
                if (parts.Length < 3) continue;

                var tool = ParsePluginJson(item.GetRawText(), parts[^2], parts[^1], _shaMapCache);
                if (tool is not null) tools.Add(tool);
            }

            // 索引条目即全量详情：直接填详情缓存，LoadToolDetailAsync 零请求命中
            foreach (var t in tools)
            {
                _detailCache[$"{CurrentSource}:{t.RepoPath}"] = t;
            }
            return tools;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetPluginIndexContentAsync(CancellationToken ct)
    {
        if (CurrentSource == CommunityDataSource.GitCode)
        {
            // GitCode contents API 单文件直接返回 base64 content，1 次请求
            var json = await GetStringAsync($"{GitCodeApiBase}/contents/{PluginIndexFile}", ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("content", out var c)) return null;
            var base64 = (c.GetString() ?? "").Replace("\n", "").Replace("\r", "");
            if (base64.Length == 0) return null;
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }

        return await GetStringAsync(
            $"https://raw.githubusercontent.com/{UpstreamOwner}/{UpstreamRepo}/main/{PluginIndexFile}", ct);
    }

    /// <summary>
    /// 用 recursive=1 获取整棵子树的所有条目，一次请求替代多层遍历。
    /// </summary>
    private static async Task<List<(string Path, string Sha, string Type)>> EnumerateTreeRecursiveAsync(string treeSha, CancellationToken ct)
    {
        var result = new List<(string Path, string Sha, string Type)>();
        var json = await GetStringAsync($"{ApiBase}/git/trees/{treeSha}?recursive=1", ct);
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("tree", out var tree)) return result;

        foreach (var item in tree.EnumerateArray())
        {
            var path = item.GetProperty("path").GetString() ?? "";
            var sha = item.GetProperty("sha").GetString() ?? "";
            var type = item.GetProperty("type").GetString() ?? "";
            result.Add((path, sha, type));
        }
        return result;
    }

    /// <summary>
    /// 按需加载单个工具的 plugin.json 详情。点进卡片时调用，替代初始加载时批量请求所有 plugin.json。
    /// </summary>
    public static async Task<CommunityTool?> LoadToolDetailAsync(CommunityTool summary, CancellationToken ct = default)
    {
        if (summary is null) return null;

        var cacheKey = $"{CurrentSource}:{summary.RepoPath}";
        if (_detailCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var tool = await LoadToolDetailCoreAsync(summary, ct);

            if (tool is not null)
            {
                _detailCache[cacheKey] = tool;
            }
            return tool;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    private static async Task<CommunityTool?> LoadToolDetailCoreAsync(CommunityTool summary, CancellationToken ct)
    {
        var repoPath = summary.RepoPath!;
        var pluginJsonPath = $"{repoPath}/plugin.json";

        if (!_shaMapCache.TryGetValue(pluginJsonPath, out var pluginJsonSha))
        {
            // sha 映射未命中（未经过列表加载 / 缓存已失效）：
            // 用 1 次 contents 请求补齐该工具目录的文件 sha，而不是重拉整棵递归树
            var dirShas = await GetDirFileShasAsync(repoPath, ct);
            if (dirShas is null) return null;
            foreach (var (path, sha) in dirShas)
            {
                _shaMapCache[path] = sha;
            }
            if (!_shaMapCache.TryGetValue(pluginJsonPath, out pluginJsonSha))
                return null;
        }

        var pluginJson = await DownloadBlobAsync(pluginJsonSha, ct);
        if (pluginJson is null) return null;

        var parts = repoPath.Split('/');
        var category = parts.Length >= 2 ? parts[^2] : summary.Category;
        var toolDir = parts.Length >= 1 ? parts[^1] : summary.Id;

        return ParsePluginJson(pluginJson, category, toolDir, _shaMapCache);
    }

    /// <summary>
    /// 列出一个目录下的文件 sha（contents 单次请求，路径与树接口一致）。失败返回 null。
    /// </summary>
    private static async Task<Dictionary<string, string>?> GetDirFileShasAsync(string repoPath, CancellationToken ct)
    {
        try
        {
            var encPath = string.Join("/", repoPath.Split('/').Select(Uri.EscapeDataString));
            var json = await GetStringAsync($"{ApiBase}/contents/{encPath}", ct);
            var doc = JsonDocument.Parse(json);

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                var sha = item.TryGetProperty("sha", out var s) ? s.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(sha))
                    map[path] = sha;
            }
            return map;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    private static string BuildGitCodeRawUrl(string relativePath)
    {
        var segments = relativePath.Split('/');
        var encoded = segments.Select(s => Uri.EscapeDataString(s));
        return $"{GitCodeRawBase}/{string.Join('/', encoded)}";
    }

    public static List<(string Name, string Url)> GetAllDownloadUrls(CommunityTool tool)
    {
        var urls = new List<(string Name, string Url)>();

        var communityFile = tool.File;
        if (!string.IsNullOrWhiteSpace(communityFile) && !string.IsNullOrWhiteSpace(tool.RepoPath))
        {
            if (!string.IsNullOrWhiteSpace(tool.FileSha))
                urls.Add(("GitCode 镜像",
                    $"https://raw.gitcode.com/{GitCodeOwner}/{GitCodeRepo}/blobs/{tool.FileSha}/{Uri.EscapeDataString(communityFile)}"));
            else
                urls.Add(("GitCode 镜像",
                    BuildGitCodeRawUrl($"{tool.RepoPath}/{communityFile}")));

            urls.Add(("GitHub 直连",
                $"https://raw.githubusercontent.com/{UpstreamOwner}/{UpstreamRepo}/main/{tool.RepoPath}/{communityFile}"));
        }
        else if (!string.IsNullOrWhiteSpace(tool.DownloadUrl))
        {
            urls.Add(("默认源", tool.DownloadUrl));
        }

        return urls;
    }

    public const long MaxUploadSizeBytes = 50 * 1024 * 1024;

    public static async Task<string> SubmitPluginAsync(
        CommunityPluginDraft draft,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        var token = GitHubAuthService.GetToken();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("请先登录 GitHub");

        var user = await GitHubAuthService.GetCurrentUserAsync(ct);
        if (user is null)
            throw new InvalidOperationException("无法获取用户信息");

        var toolId = GenerateToolId(draft.Name);
        var jsonText = BuildPluginJson(draft, user.Login);

        progress?.Report("正在 Fork 仓库...");

        var forkOwner = await EnsureForkAsync(token, ct);

        progress?.Report("正在创建分支...");

        var branchName = $"plugin/{toolId}";
        var mainSha = await GetRefShaAsync(forkOwner, UpstreamRepo, "heads/main", token, ct);
        if (mainSha is null)
            throw new InvalidOperationException("无法获取主分支信息");

        var branchExists = await CheckRefExistsAsync(forkOwner, UpstreamRepo, $"heads/{branchName}", token, ct);
        if (branchExists)
            branchName = $"plugin/{toolId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        await CreateRefAsync(forkOwner, UpstreamRepo, $"refs/heads/{branchName}", mainSha, token, ct);

        progress?.Report("正在上传文件...");

        if (!string.IsNullOrWhiteSpace(draft.ZipFilePath) && File.Exists(draft.ZipFilePath))
        {
            var zipFileName = Path.GetFileName(draft.ZipFilePath);
            var zipRepoPath = $"{PluginsPath}/{draft.Category}/{toolId}/{zipFileName}";
            await CreateBinaryFileAsync(forkOwner, UpstreamRepo, zipRepoPath, branchName, draft.ZipFilePath, token, ct);
        }

        if (!string.IsNullOrWhiteSpace(draft.IconFilePath) && File.Exists(draft.IconFilePath))
        {
            var iconFileName = Path.GetFileName(draft.IconFilePath);
            var iconRepoPath = $"{PluginsPath}/{draft.Category}/{toolId}/{iconFileName}";
            await CreateBinaryFileAsync(forkOwner, UpstreamRepo, iconRepoPath, branchName, draft.IconFilePath, token, ct);
        }

        progress?.Report("正在提交插件信息...");

        var pluginRepoPath = $"{PluginsPath}/{draft.Category}/{toolId}/plugin.json";
        await CreateFileAsync(forkOwner, UpstreamRepo, pluginRepoPath, branchName, jsonText, token, ct);

        progress?.Report("正在创建 Pull Request...");

        var prUrl = await CreatePullRequestAsync(
            branchName, forkOwner, toolId, draft.Name, draft.Description, draft.Category, user.Login, token, ct);

        progress?.Report("提交成功！");

        InvalidateCache();
        return prUrl;
    }

    /// <summary>plugin.json 的唯一构建入口：提交上传与界面预览共用同一份内容。</summary>
    public static string BuildPluginJson(CommunityPluginDraft draft, string author)
    {
        var plugin = new Dictionary<string, object?>
        {
            ["id"] = GenerateToolId(draft.Name),
            ["name"] = draft.Name,
            ["version"] = string.IsNullOrWhiteSpace(draft.Version) ? "1.0" : draft.Version,
            ["description"] = draft.Description,
            ["category"] = draft.Category,
            ["tags"] = draft.Tags,
            ["launchTarget"] = string.IsNullOrWhiteSpace(draft.LaunchTarget) ? null : draft.LaunchTarget,
            ["author"] = author,
            ["submittedAt"] = DateTimeOffset.UtcNow.ToString("o")
        };

        if (!string.IsNullOrWhiteSpace(draft.Publisher)) plugin["publisher"] = draft.Publisher;
        if (!string.IsNullOrWhiteSpace(draft.Homepage)) plugin["homepage"] = draft.Homepage;
        if (!string.IsNullOrWhiteSpace(draft.IconFilePath)) plugin["icon"] = Path.GetFileName(draft.IconFilePath);
        if (!string.IsNullOrWhiteSpace(draft.ZipFilePath)) plugin["file"] = Path.GetFileName(draft.ZipFilePath);
        if (!string.IsNullOrWhiteSpace(draft.DownloadUrl)) plugin["downloadUrl"] = draft.DownloadUrl;
        if (!string.IsNullOrWhiteSpace(draft.DownloadFilter)) plugin["downloadFilter"] = draft.DownloadFilter;

        if (draft.ArchVariants.Count > 0)
        {
            plugin["archVariants"] = draft.ArchVariants
                .Where(v => !string.IsNullOrWhiteSpace(v.EntryPath) && !string.IsNullOrWhiteSpace(v.Arch))
                .Select(v => new Dictionary<string, object?>
                {
                    ["file"] = v.EntryPath.Replace('\\', '/').TrimStart('/'),
                    ["arch"] = v.Arch
                })
                .ToList();
        }

        return JsonSerializer.Serialize(plugin, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    /// <summary>标签输入解析（中英文逗号/分号分隔）。</summary>
    public static List<string> ParseTagList(string tags) =>
        tags.Split(',', '，', ';', '；')
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

    public static async Task<string> DeletePluginAsync(CommunityTool tool, IProgress<string>? progress, CancellationToken ct = default)
    {
        var token = GitHubAuthService.GetToken();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("请先登录 GitHub");

        var user = await GitHubAuthService.GetCurrentUserAsync(ct);
        if (user is null)
            throw new InvalidOperationException("无法获取用户信息");

        if (string.IsNullOrWhiteSpace(tool.Author) ||
            !string.Equals(tool.Author, user.Login, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只能删除自己提交的工具");

        if (string.IsNullOrWhiteSpace(tool.RepoPath))
            throw new InvalidOperationException("无法定位工具在仓库中的路径");

        progress?.Report("正在 Fork 仓库...");

        var forkOwner = await EnsureForkAsync(token, ct);

        progress?.Report("正在同步 Fork...");

        await SyncForkWithUpstreamAsync(forkOwner, token, ct);

        progress?.Report("正在创建分支...");

        var branchName = $"delete/{tool.Id}";
        var mainSha = await GetRefShaAsync(forkOwner, UpstreamRepo, "heads/main", token, ct);
        if (mainSha is null)
            throw new InvalidOperationException("无法获取主分支信息");

        var branchExists = await CheckRefExistsAsync(forkOwner, UpstreamRepo, $"heads/{branchName}", token, ct);
        if (branchExists)
            branchName = $"delete/{tool.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        await CreateRefAsync(forkOwner, UpstreamRepo, $"refs/heads/{branchName}", mainSha, token, ct);

        progress?.Report("正在删除文件...");

        var filesToDelete = await ListDirectoryFilesAsync(forkOwner, UpstreamRepo, tool.RepoPath, branchName, token, ct);
        foreach (var filePath in filesToDelete)
        {
            await DeleteFileAsync(forkOwner, UpstreamRepo, filePath, branchName, $"chore: remove {Path.GetFileName(filePath)}", token, ct);
        }

        progress?.Report("正在创建 Pull Request...");

        var prUrl = await CreateDeletePullRequestAsync(
            branchName, forkOwner, tool.Id, tool.Name, tool.Category, user.Login, token, ct);

        progress?.Report("删除请求已提交！");

        InvalidateCache();
        return prUrl;
    }

    private static async Task SyncForkWithUpstreamAsync(string forkOwner, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();

        var upstreamMainSha = await GetRefShaAsync(UpstreamOwner, UpstreamRepo, "heads/main", token, ct);
        if (upstreamMainSha is null) return;

        var body = JsonSerializer.Serialize(new { sha = upstreamMainSha, force = true });
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            await client.PatchAsync(
                $"https://api.github.com/repos/{forkOwner}/{UpstreamRepo}/git/refs/heads/main", content, ct);
        }
        catch { }
    }

    private static async Task<List<string>> ListDirectoryFilesAsync(string owner, string repo, string dirPath, string branch, string token, CancellationToken ct)
    {
        var files = new List<string>();
        using var client = GitHubAuthService.CreateAuthenticatedClient();

        try
        {
            var json = await client.GetStringAsync(
                $"https://api.github.com/repos/{owner}/{repo}/contents/{dirPath}?ref={branch}", ct);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var type = item.GetProperty("type").GetString() ?? "";
                    var path = item.GetProperty("path").GetString() ?? "";

                    if (type == "file")
                    {
                        files.Add(path);
                    }
                    else if (type == "dir")
                    {
                        var subFiles = await ListDirectoryFilesAsync(owner, repo, path, branch, token, ct);
                        files.AddRange(subFiles);
                    }
                }
            }
        }
        catch { }

        return files;
    }

    private static async Task DeleteFileAsync(string owner, string repo, string filePath, string branch, string commitMessage, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();

        var getResp = await client.GetAsync(
            $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}?ref={branch}", ct);
        if (!getResp.IsSuccessStatusCode) return;

        var getJson = await getResp.Content.ReadAsStringAsync(ct);
        var getDoc = JsonDocument.Parse(getJson);
        var sha = getDoc.RootElement.GetProperty("sha").GetString();

        var body = JsonSerializer.Serialize(new
        {
            message = commitMessage,
            sha,
            branch
        });
        var request = new HttpRequestMessage(HttpMethod.Delete,
            $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var resp = await client.SendAsync(request, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"删除文件 {filePath} 失败：{(int)resp.StatusCode}\n{err}");
        }
    }

    private static async Task<string> CreateDeletePullRequestAsync(
        string branch, string forkOwner, string toolId, string toolName,
        string category, string author, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        var body = JsonSerializer.Serialize(new
        {
            title = $"[删除工具] {toolName}",
            head = $"{forkOwner}:{branch}",
            @base = "main",
            body = $"## 删除社区工具\n\n" +
                   $"- **名称**：{toolName}\n" +
                   $"- **分类**：{category}\n" +
                   $"- **请求者**：@{author}\n\n" +
                   $"工具提交者请求删除此工具。"
        });
        var httpContent = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(
            $"https://api.github.com/repos/{UpstreamOwner}/{UpstreamRepo}/pulls", httpContent, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"创建删除 PR 失败：{(int)resp.StatusCode}\n{err}");
        }

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(respJson);
        return doc.RootElement.GetProperty("html_url").GetString() ?? "";
    }

    public static string GenerateToolId(string name)
    {
        var id = name.ToLowerInvariant();
        id = System.Text.RegularExpressions.Regex.Replace(id, @"[^a-z0-9\u4e00-\u9fff\-]", "-");
        id = System.Text.RegularExpressions.Regex.Replace(id, @"-+", "-");
        id = id.Trim('-');
        if (id.Length > 50) id = id[..50];
        return string.IsNullOrWhiteSpace(id) ? $"tool-{Guid.NewGuid():N}"[..16] : id;
    }

    private static async Task<string> EnsureForkAsync(string token, CancellationToken ct)
    {
        var user = await GitHubAuthService.GetCurrentUserAsync(ct);
        var forkOwner = user?.Login ?? throw new InvalidOperationException("无法获取用户名");

        using var client = GitHubAuthService.CreateAuthenticatedClient();

        try
        {
            var checkResp = await client.GetAsync(
                $"https://api.github.com/repos/{forkOwner}/{UpstreamRepo}", ct);
            if (checkResp.IsSuccessStatusCode) return forkOwner;
        }
        catch { }

        var forkResp = await client.PostAsync(
            $"https://api.github.com/repos/{UpstreamOwner}/{UpstreamRepo}/forks",
            new StringContent("{}", Encoding.UTF8, "application/json"), ct);

        if (!forkResp.IsSuccessStatusCode)
        {
            var errBody = await forkResp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Fork 失败：{(int)forkResp.StatusCode} {forkResp.StatusCode}\n{errBody}");
        }

        await Task.Delay(3000, ct);
        return forkOwner;
    }

    private static async Task<string?> GetLatestTreeShaAsync(CancellationToken ct)
    {
        using var client = CreateApiClient();
        var json = await client.GetStringAsync($"{ApiBase}/git/ref/heads/main", ct);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("object").GetProperty("sha").GetString();
    }

    private static async Task<string?> GetSubTreeShaAsync(string treeSha, string path, CancellationToken ct)
    {
        using var client = CreateApiClient();
        var json = await client.GetStringAsync($"{ApiBase}/git/trees/{treeSha}", ct);
        var doc = JsonDocument.Parse(json);
        var tree = doc.RootElement.GetProperty("tree");

        foreach (var item in tree.EnumerateArray())
        {
            var itemPath = item.GetProperty("path").GetString();
            if (string.Equals(itemPath, path, StringComparison.OrdinalIgnoreCase))
            {
                if (item.GetProperty("type").GetString() == "tree")
                    return item.GetProperty("sha").GetString();
                return null;
            }
        }
        return null;
    }

    private static async Task<string?> DownloadBlobAsync(string sha, CancellationToken ct)
    {
        using var client = CreateApiClient();
        var json = await client.GetStringAsync($"{ApiBase}/git/blobs/{sha}", ct);
        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("content", out var contentEl))
        {
            var base64 = contentEl.GetString() ?? "";
            base64 = base64.Replace("\n", "").Replace("\r", "");
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }
        return null;
    }

    private static CommunityTool? ParsePluginJson(string json, string category, string toolDir,
        Dictionary<string, string>? shaMap = null)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : toolDir;
            var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : toolDir;
            var cat = root.TryGetProperty("category", out var catEl) ? catEl.GetString() : category;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return null;

            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var tagsEl))
            {
                foreach (var tag in tagsEl.EnumerateArray())
                {
                    var t = tag.GetString();
                    if (!string.IsNullOrWhiteSpace(t)) tags.Add(t);
                }
            }

            var archVariants = new List<CommunityArchVariant>();
            if (root.TryGetProperty("archVariants", out var archEl))
            {
                foreach (var av in archEl.EnumerateArray())
                {
                    archVariants.Add(new CommunityArchVariant
                    {
                        File = av.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "",
                        Arch = av.TryGetProperty("arch", out var a) ? a.GetString() ?? "" : ""
                    });
                }
            }

            var tool = new CommunityTool
            {
                Id = id,
                Name = name,
                Version = root.TryGetProperty("version", out var v) ? v.GetString() : null,
                Description = root.TryGetProperty("description", out var d) ? d.GetString() : null,
                Category = cat ?? category,
                Publisher = root.TryGetProperty("publisher", out var p) ? p.GetString() : null,
                Tags = tags,
                Icon = root.TryGetProperty("icon", out var ic) ? ic.GetString() : null,
                DownloadUrl = root.TryGetProperty("downloadUrl", out var du) ? du.GetString() : null,
                DownloadFilter = root.TryGetProperty("downloadFilter", out var df) ? df.GetString() : null,
                LaunchTarget = root.TryGetProperty("launchTarget", out var lt) ? lt.GetString() : null,
                ArchVariants = archVariants.Count > 0 ? archVariants : null,
                Author = root.TryGetProperty("author", out var au) ? au.GetString() : null,
                SubmittedAt = root.TryGetProperty("submittedAt", out var sa) ? DateTimeOffset.TryParse(sa.GetString(), out var dt) ? dt : null : null,
                Homepage = root.TryGetProperty("homepage", out var hp) ? hp.GetString() : null,
                File = root.TryGetProperty("file", out var fl) ? fl.GetString() : null,
                RepoPath = $"{PluginsPath}/{category}/{toolDir}"
            };

            if (shaMap is not null && !string.IsNullOrWhiteSpace(tool.File) && !string.IsNullOrWhiteSpace(tool.RepoPath))
            {
                var fileKey = $"{tool.RepoPath}/{tool.File}";
                if (shaMap.TryGetValue(fileKey, out var fileSha))
                    tool.FileSha = fileSha;
            }

            if (!string.IsNullOrWhiteSpace(tool.Icon) && !string.IsNullOrWhiteSpace(tool.RepoPath))
            {
                if (shaMap is not null)
                {
                    var iconKey = $"{tool.RepoPath}/{tool.Icon}";
                    if (shaMap.TryGetValue(iconKey, out var iconSha))
                        tool.IconPath = $"https://raw.gitcode.com/{GitCodeOwner}/{GitCodeRepo}/blobs/{iconSha}/{Uri.EscapeDataString(tool.Icon)}";
                    else
                        tool.IconPath = BuildGitCodeRawUrl($"{tool.RepoPath}/{tool.Icon}");
                }
                else
                {
                    tool.IconPath = $"https://raw.githubusercontent.com/{UpstreamOwner}/{UpstreamRepo}/main/{tool.RepoPath}/{tool.Icon}";
                }
            }

            return tool;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetRefShaAsync(string owner, string repo, string refPath, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        try
        {
            var json = await client.GetStringAsync(
                $"https://api.github.com/repos/{owner}/{repo}/git/ref/{refPath}", ct);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("object").GetProperty("sha").GetString();
        }
        catch { return null; }
    }

    private static async Task<bool> CheckRefExistsAsync(string owner, string repo, string refPath, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        try
        {
            var resp = await client.GetAsync(
                $"https://api.github.com/repos/{owner}/{repo}/git/ref/{refPath}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task CreateRefAsync(string owner, string repo, string refName, string sha, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        var body = JsonSerializer.Serialize(new { @ref = refName, sha });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(
            $"https://api.github.com/repos/{owner}/{repo}/git/refs", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"创建分支失败：{(int)resp.StatusCode}\n{err}");
        }
    }

    private static async Task CreateFileAsync(string owner, string repo, string path, string branch, string content, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        var base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        var body = JsonSerializer.Serialize(new
        {
            message = $"feat: add plugin - {path.Split('/')[^2]}",
            content = base64Content,
            branch
        });
        var httpContent = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PutAsync(
            $"https://api.github.com/repos/{owner}/{repo}/contents/{path}", httpContent, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"提交文件失败：{(int)resp.StatusCode}\n{err}");
        }
    }

    private static async Task CreateBinaryFileAsync(string owner, string repo, string path, string branch, string localFilePath, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        var bytes = await File.ReadAllBytesAsync(localFilePath, ct);
        var base64Content = Convert.ToBase64String(bytes);
        var fileName = Path.GetFileName(localFilePath);
        var body = JsonSerializer.Serialize(new
        {
            message = $"feat: upload {fileName}",
            content = base64Content,
            branch
        });
        var httpContent = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PutAsync(
            $"https://api.github.com/repos/{owner}/{repo}/contents/{path}", httpContent, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"上传 {fileName} 失败：{(int)resp.StatusCode}\n{err}");
        }
    }

    private static async Task<string> CreatePullRequestAsync(
        string branch, string forkOwner, string toolId, string toolName,
        string description, string category, string author, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        var body = JsonSerializer.Serialize(new
        {
            title = $"[社区工具] {toolName}",
            head = $"{forkOwner}:{branch}",
            @base = "main",
            body = $"## 新增社区工具\n\n" +
                   $"- **名称**：{toolName}\n" +
                   $"- **分类**：{category}\n" +
                   $"- **描述**：{description}\n" +
                   $"- **提交者**：@{author}\n"
        });
        var httpContent = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(
            $"https://api.github.com/repos/{UpstreamOwner}/{UpstreamRepo}/pulls", httpContent, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"创建 PR 失败：{(int)resp.StatusCode}\n{err}");
        }

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(respJson);
        return doc.RootElement.GetProperty("html_url").GetString() ?? "";
    }

    private static async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var client = CreateApiClient();
        return await client.GetStringAsync(url, ct);
    }

    private static HttpClient CreateApiClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Community");
        return client;
    }
}

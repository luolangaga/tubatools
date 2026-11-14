using System.Text.Json;
using System.Text.Json.Serialization;
using TubaWinUi3.Storage;

namespace TubaWinUi3.Services.AiAgent;

public enum AgentWorkspaceNameIssue { None, Empty, TooLong, Invalid, Duplicate }
public enum AgentWorkspaceDirectoryIssue { None, Invalid, Duplicate, Overlapping, NotDirectory }

public sealed record AgentWorkspaceInfo(string Id, string Name, string Path, bool IsDefault);

/// <summary>
/// 多个 AI 工作区的注册表。默认工作区沿用升级前的目录（AiAssistant/Workspace）；
/// 新建工作区默认放在 AiAssistant/Workspaces/&lt;名称&gt;-&lt;id&gt;，也可由用户指定任意绝对目录。
/// 每个工作区各自拥有独立会话、画布和插件产物；AI 组件、提供商配置和已安装插件仍是全局共享。
/// </summary>
public static class AgentWorkspaceStore
{
    public const string DefaultId = "default";
    public const string DefaultName = "默认工作区";
    public const int MaxNameLength = 24;

    public static string Root => Path.Combine(AiComponentService.Root, "Workspaces");
    public static string DefaultPath => Path.Combine(AiComponentService.Root, "Workspace");

    private static string RegistryPath => Path.Combine(Root, "workspaces.json");
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class Entry
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Folder { get; set; }
        /// <summary>用户自选的绝对目录；为空时使用 <see cref="Root"/> 下的 Folder。</summary>
        public string? Path { get; set; }
        public string? Created { get; set; }
    }

    private sealed class Registry
    {
        public string? Current { get; set; }
        public List<Entry> Workspaces { get; set; } = [];
    }

    private sealed record Cache(string Path, long Stamp, Registry Registry);
    private static Cache? _cache;

    /// <summary>默认工作区始终排在第一位；损坏、重复或越界的条目直接从列表剔除。</summary>
    public static IReadOnlyList<AgentWorkspaceInfo> List()
    {
        var registry = Read();
        var result = new List<AgentWorkspaceInfo> { new(DefaultId, DefaultName, DefaultPath, true) };
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DefaultId };
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Key(DefaultPath) };
        foreach (var entry in registry.Workspaces)
        {
            var id = (entry.Id ?? "").Trim();
            var name = (entry.Name ?? "").Trim();
            if (id.Length == 0 || !ids.Add(id) || name.Length == 0) continue;
            if (ResolveEntry(entry) is not { } path) continue;
            if (!paths.Add(Key(path))) continue;
            result.Add(new(id, name, path, false));
        }
        return result;
    }

    private static string? ResolveEntry(Entry entry)
    {
        var custom = (entry.Path ?? "").Trim();
        if (custom.Length == 0) return ResolveFolder((entry.Folder ?? "").Trim());
        try
        {
            var path = NormalizeDirectory(custom);
            return path is not null && !IsReserved(path) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>当前工作区；注册表缺失、损坏或指向已剔除的条目时回退默认工作区。</summary>
    public static AgentWorkspaceInfo ResolveCurrent()
    {
        try
        {
            var list = List();
            var current = Read().Current;
            return list.FirstOrDefault(w => string.Equals(w.Id, current, StringComparison.OrdinalIgnoreCase)) ?? list[0];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return new(DefaultId, DefaultName, DefaultPath, true);
        }
    }

    public static AgentWorkspaceNameIssue ValidateName(string? name, string? exceptId = null)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return AgentWorkspaceNameIssue.Empty;
        if (trimmed.Length > MaxNameLength) return AgentWorkspaceNameIssue.TooLong;
        if (trimmed is "." or "..") return AgentWorkspaceNameIssue.Invalid;
        if (trimmed.Any(char.IsControl) || trimmed.EndsWith('.') ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || trimmed.IndexOfAny(['/', '\\']) >= 0)
            return AgentWorkspaceNameIssue.Invalid;
        if (List().Any(w => !w.IsDefault && !string.Equals(w.Id, exceptId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(w.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            return AgentWorkspaceNameIssue.Duplicate;
        return AgentWorkspaceNameIssue.None;
    }

    /// <summary>校验用户自选的工作区目录；空白表示使用默认位置，返回 None。</summary>
    public static AgentWorkspaceDirectoryIssue ValidateDirectory(string? directory, string? exceptId = null)
    {
        var text = (directory ?? "").Trim();
        if (text.Length == 0) return AgentWorkspaceDirectoryIssue.None;
        string path;
        try { path = NormalizeDirectory(text)!; }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or PathTooLongException)
        {
            return AgentWorkspaceDirectoryIssue.Invalid;
        }
        if (path is null || IsReserved(path) || IsRoot(path)) return AgentWorkspaceDirectoryIssue.Invalid;
        var root = Path.GetPathRoot(path) ?? "";
        foreach (var part in path[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return AgentWorkspaceDirectoryIssue.Invalid;
        try
        {
            if (File.Exists(path)) return AgentWorkspaceDirectoryIssue.NotDirectory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        foreach (var workspace in List())
        {
            if (string.Equals(workspace.Id, exceptId, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsSame(path, workspace.Path)) return AgentWorkspaceDirectoryIssue.Duplicate;
            if (Overlaps(path, workspace.Path)) return AgentWorkspaceDirectoryIssue.Overlapping;
        }
        return AgentWorkspaceDirectoryIssue.None;
    }

    public static AgentWorkspaceInfo Create(string name, string? directory = null)
    {
        if (ValidateName(name) != AgentWorkspaceNameIssue.None) throw new ArgumentException("工作区名称无效。", nameof(name));
        var trimmed = name.Trim();
        string? custom = null;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            try { custom = NormalizeDirectory(directory); }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or PathTooLongException)
            {
                throw new ArgumentException(DirectoryIssueMessage(AgentWorkspaceDirectoryIssue.Invalid), nameof(directory));
            }
        }
        var registry = Read();
        var id = NewId(registry);
        var folder = custom is null ? FolderFor(trimmed, id) : null;
        var path = custom ?? Path.Combine(Root, folder!);
        var issue = ValidateDirectory(path);
        if (issue != AgentWorkspaceDirectoryIssue.None)
            throw new ArgumentException(DirectoryIssueMessage(issue), custom is null ? "name" : nameof(directory));

        var existed = Directory.Exists(path);
        Directory.CreateDirectory(path);
        var entry = new Entry { Id = id, Name = trimmed, Folder = folder, Path = custom, Created = DateTimeOffset.UtcNow.ToString("O") };
        registry.Workspaces.Add(entry);
        try { Save(registry); }
        catch
        {
            registry.Workspaces.Remove(entry);
            try
            {
                if (!existed && Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
            }
            catch { /* 目录已被占用时保留，下一次切换到该工作区会复用或报错。 */ }
            throw;
        }
        return new(id, trimmed, path, false);
    }

    public static void Select(AgentWorkspaceInfo workspace)
    {
        var registry = Read();
        if (!string.Equals(workspace.Id, DefaultId, StringComparison.OrdinalIgnoreCase) &&
            !registry.Workspaces.Any(e => string.Equals((e.Id ?? "").Trim(), workspace.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("工作区不存在。");
        registry.Current = workspace.Id;
        Save(registry);
    }

    public static AgentWorkspaceInfo Rename(AgentWorkspaceInfo workspace, string name)
    {
        if (workspace.IsDefault) throw new InvalidOperationException("默认工作区不能重命名。");
        if (ValidateName(name, workspace.Id) != AgentWorkspaceNameIssue.None) throw new ArgumentException("工作区名称无效。", nameof(name));
        var registry = Read();
        var entry = registry.Workspaces.FirstOrDefault(e =>
            string.Equals((e.Id ?? "").Trim(), workspace.Id, StringComparison.OrdinalIgnoreCase));
        if (entry is null) throw new InvalidOperationException("工作区不存在。");
        entry.Name = name.Trim();
        Save(registry);
        return workspace with { Name = entry.Name };
    }

    private static Registry Read()
    {
        var path = RegistryPath;
        // 目录不可用时按空注册表处理，界面仍能显示并使用默认工作区。
        long stamp = -1;
        try
        {
            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                stamp = file.Length * 31 + file.LastWriteTimeUtc.Ticks;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        if (_cache is { } cached && cached.Path == path && cached.Stamp == stamp) return cached.Registry;
        Registry registry;
        try
        {
            var text = stamp >= 0 ? AtomicFile.ReadText(path, ValidateRegistry) : null;
            registry = text is null ? new() : JsonSerializer.Deserialize<Registry>(text, Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            registry = new();
        }
        _cache = new(path, stamp, registry);
        return registry;
    }

    private static void ValidateRegistry(string text)
    {
        if (JsonSerializer.Deserialize<Registry>(text, Json) is null) throw new JsonException("工作区索引为空。");
    }

    private static void Save(Registry registry)
    {
        Directory.CreateDirectory(Root);
        AtomicFile.WriteText(RegistryPath, JsonSerializer.Serialize(registry, Json));
        _cache = null;
    }

    private static string NewId(Registry registry)
    {
        var ids = registry.Workspaces.Select(e => (e.Id ?? "").Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            if (!ids.Contains(id)) return id;
        }
        throw new IOException("无法分配工作区标识。");
    }

    /// <summary>目录名保持可读，同时拒绝路径分隔符与穿越。</summary>
    private static string FolderFor(string name, string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (safe.Length > 20) safe = safe[..20].TrimEnd(' ', '.');
        if (safe.Length == 0) safe = "workspace";
        return safe + "-" + id;
    }

    private static string? NormalizeDirectory(string? directory)
    {
        var text = (directory ?? "").Trim();
        if (text.Length == 0) return null;
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1].Trim();
        if (!Path.IsPathFullyQualified(text)) throw new ArgumentException("工作区目录必须是绝对路径。", nameof(directory));
        return Path.GetFullPath(text);
    }

    private static string Key(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsSame(string left, string right) =>
        string.Equals(Key(left), Key(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsReserved(string path) => IsSame(path, Root) || IsSame(path, DefaultPath);

    private static bool IsRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        return root is not null && IsSame(path, root);
    }

    /// <summary>两个目录相同或互为祖先/子目录时视为重叠，避免工作区互相包含。</summary>
    private static bool Overlaps(string left, string right)
    {
        var a = Key(left);
        var b = Key(right);
        if (a.Length == 0 || b.Length == 0) return false;
        var separator = Path.DirectorySeparatorChar;
        return a.StartsWith(b + separator, StringComparison.OrdinalIgnoreCase) ||
               b.StartsWith(a + separator, StringComparison.OrdinalIgnoreCase);
    }

    private static string DirectoryIssueMessage(AgentWorkspaceDirectoryIssue issue) => issue switch
    {
        AgentWorkspaceDirectoryIssue.Duplicate => "已有工作区使用这个目录。",
        AgentWorkspaceDirectoryIssue.Overlapping => "该目录与现有工作区目录重叠，请选择其他位置。",
        AgentWorkspaceDirectoryIssue.NotDirectory => "所选路径是文件，请选择文件夹。",
        AgentWorkspaceDirectoryIssue.Invalid => "工作区目录无效，请选择普通文件夹（不能是盘符根目录或保留位置）。",
        _ => ""
    };

    private static string? ResolveFolder(string folder)
    {
        if (folder.Length == 0 || folder is "." or "..") return null;
        if (folder.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0) return null;
        try
        {
            var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(root, folder));
            return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return null; }
    }
}

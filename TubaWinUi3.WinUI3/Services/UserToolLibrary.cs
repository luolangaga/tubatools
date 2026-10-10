using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services;

/// <summary>
/// 用户工具与运行时状态不属于发布目录。所有读改写串行化并原子落盘；
/// 旧工具只迁移登记，不搬动文件，保留收藏/历史/桌面快捷方式的目标路径。
/// </summary>
internal static class UserToolLibrary
{
    private static readonly object Gate = new();
    internal static string? RootOverride;
    internal static string DataDirectory => RootOverride ??
        (ToolMetadataService.MetadataRootOverride is { } metadata
            ? Path.Combine(Path.GetDirectoryName(metadata)!, "UserData")
            : ConfigManager.GetDataDir());
    internal static string ToolsRoot => Path.Combine(DataDirectory, "UserTools");
    internal static string CatalogPath => Path.Combine(DataDirectory, "user-tools.json");
    internal static string StatePath => Path.Combine(DataDirectory, "tool-state.json");

    internal static JsonObject ReadObject(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new IOException($"工具数据格式无效：{path}");
    }

    internal static void WriteObject(string path, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static JsonArray Tools(JsonObject root)
    {
        if (root["tools"] is JsonArray tools) return tools;
        if (root["tools"] is not null) throw new IOException("用户工具列表格式无效。");
        var created = new JsonArray();
        root["tools"] = created;
        return created;
    }

    internal static string StoreDirectory(string directory)
    {
        if (IsWithin(directory, ToolsRoot))
            return "{UserToolsRoot}\\" + Path.GetRelativePath(ToolsRoot, directory);
        return PathResolver.MakeRelative(directory);
    }

    internal static string ResolveDirectory(string stored) => Path.GetFullPath(PathResolver.MakeAbsolute(
        stored.Replace("{UserToolsRoot}", ToolsRoot, StringComparison.OrdinalIgnoreCase)));

    internal static bool IsWithin(string path, string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool MatchesDirectory(JsonObject entry, string directory) =>
        entry["directory"]?.GetValue<string>() is { Length: > 0 } stored &&
        Path.GetFullPath(ResolveDirectory(stored)).Equals(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<JsonObject> Load(JsonObject official, IEnumerable<string> legacyFiles)
    {
        lock (Gate)
        {
            var catalog = ReadObject(CatalogPath);
            var state = ReadObject(StatePath);
            MigrateLegacy(official, catalog, state, legacyFiles);
            var result = new List<JsonObject>();
            var states = Tools(state).OfType<JsonObject>().ToList();
            foreach (var original in Tools(official).OfType<JsonObject>())
            {
                var entry = (JsonObject)original.DeepClone();
                var key = "official:" + entry["match"]?.GetValue<string>();
                ApplyState(entry, states.FirstOrDefault(s => Equal(s["id"], key)));
                if (entry["hidden"]?.GetValue<bool>() != true) result.Add(entry);
            }
            foreach (var original in Tools(catalog).OfType<JsonObject>())
            {
                var entry = (JsonObject)original.DeepClone();
                ApplyState(entry, states.FirstOrDefault(s => Equal(s["id"], entry["id"]?.GetValue<string>())));
                if (entry["hidden"]?.GetValue<bool>() != true) result.Add(entry);
            }
            return result;
        }
    }

    private static bool Equal(JsonNode? node, string? value) =>
        string.Equals(node?.GetValue<string>(), value, StringComparison.OrdinalIgnoreCase);

    private static void ApplyState(JsonObject entry, JsonObject? state)
    {
        if (state is null) return;
        foreach (var key in new[] { "order", "version", "hidden" })
            if (state.ContainsKey(key)) entry[key] = state[key]?.DeepClone();
    }

    internal static string Upsert(JsonObject entry, string directory, string? sourceId = null)
    {
        lock (Gate)
        {
            var root = ReadObject(CatalogPath);
            var tools = Tools(root);
            var existing = tools.OfType<JsonObject>().FirstOrDefault(t => MatchesDirectory(t, directory));
            var merged = existing is null ? new JsonObject() : (JsonObject)existing.DeepClone();
            foreach (var field in entry) merged[field.Key] = field.Value?.DeepClone();
            var id = existing?["id"]?.GetValue<string>() ??
                (sourceId is null ? "custom:" + Guid.NewGuid().ToString("N") : "community:" + sourceId);
            merged["id"] = id;
            merged["source"] = sourceId is null ? "custom" : "community";
            merged["directory"] = StoreDirectory(directory);
            if (existing is not null) tools.Remove(existing);
            tools.Add(merged);
            WriteObject(CatalogPath, root);
            return id;
        }
    }

    internal static IReadOnlyList<JsonObject> GetEntries()
    {
        lock (Gate)
            return Tools(ReadObject(CatalogPath)).OfType<JsonObject>()
                .Select(t => (JsonObject)t.DeepClone()).ToList();
    }

    /// <summary>配置备份同时携带旧 Tools 下的用户工具；仅在备份副本里改路径。</summary>
    internal static (JsonObject Catalog, IReadOnlyDictionary<string, string> Files,
        IReadOnlyDictionary<string, string> Directories) CreateExportSnapshot()
    {
        lock (Gate)
        {
            var catalog = ReadObject(CatalogPath);
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Tools(catalog).OfType<JsonObject>())
            {
                if (entry["directory"]?.GetValue<string>() is not { } stored) continue;
                var directory = ResolveDirectory(stored);
                if (!Directory.Exists(directory) || IsWithin(directory, ToolsRoot)) continue;
                var id = entry["id"]?.GetValue<string>() ?? stored;
                var folder = "Legacy/" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)));
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                    files["UserTools/" + folder + "/" + Path.GetRelativePath(directory, file).Replace('\\', '/')] = file;
                var destination = "{UserToolsRoot}\\" + folder.Replace('/', '\\');
                directories[directory] = destination;
                entry["directory"] = destination;
            }
            return (catalog, files, directories);
        }
    }

    /// <summary>备份中搬入 UserTools 的旧工具，收藏/历史/排序也使用备份里的新路径。</summary>
    internal static string RewriteExportReferences(string json, IReadOnlyDictionary<string, string> directories)
    {
        if (directories.Count == 0) return json;
        JsonNode? Rewrite(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var key in obj.Select(p => p.Key).ToList()) obj[key] = Rewrite(obj[key]);
            else if (node is JsonArray array)
                for (var i = 0; i < array.Count; i++) array[i] = Rewrite(array[i]);
            else if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                // settings.json 的部分值是序列化后的路径数组。
                if (text.StartsWith('['))
                {
                    try { return JsonValue.Create(RewriteExportReferences(text, directories)); }
                    catch (JsonException) { }
                }
                if (!Path.IsPathRooted(text) && !text.StartsWith('{')) return node;
                try
                {
                    var path = Path.GetFullPath(PathResolver.MakeAbsolute(text));
                    foreach (var (source, destination) in directories)
                    {
                        if (path.Equals(source, StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(destination);
                        if (IsWithin(path, source))
                            return JsonValue.Create(destination + "\\" + Path.GetRelativePath(source, path));
                    }
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
            }
            return node;
        }
        return Rewrite(JsonNode.Parse(json))!.ToJsonString();
    }

    internal static void SetStates(IEnumerable<(string Id, string Field, JsonNode? Value)> changes)
    {
        lock (Gate)
        {
            var root = ReadObject(StatePath);
            var tools = Tools(root);
            foreach (var (id, field, value) in changes)
            {
                var entry = tools.OfType<JsonObject>().FirstOrDefault(t => Equal(t["id"], id));
                if (entry is null)
                {
                    entry = new JsonObject { ["id"] = id };
                    tools.Add(entry);
                }
                entry[field] = value?.DeepClone();
            }
            WriteObject(StatePath, root);
        }
    }

    internal static bool Remove(string directory)
    {
        lock (Gate)
        {
            var root = ReadObject(CatalogPath);
            var tools = Tools(root);
            var entries = tools.OfType<JsonObject>().Where(t => MatchesDirectory(t, directory)).ToList();
            if (entries.Count == 0) return false;
            foreach (var entry in entries) tools.Remove(entry);
            WriteObject(CatalogPath, root);
            return true;
        }
    }

    private static void MigrateLegacy(JsonObject official, JsonObject catalog, JsonObject state,
        IEnumerable<string> files)
    {
        var userTools = Tools(catalog);
        var states = Tools(state);
        var migrations = catalog["migrations"] as JsonArray ?? new JsonArray();
        catalog["migrations"] = migrations;
        var known = Tools(official).OfType<JsonObject>().Where(t => t["match"] is not null)
            .ToDictionary(t => t["match"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var path in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try
            {
                var bytes = File.ReadAllBytes(path);
                var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
                if (migrations.Any(m => Equal(m, fingerprint))) continue;
                var legacy = JsonNode.Parse(bytes) as JsonObject ?? throw new IOException("旧工具库格式无效。");
                foreach (var item in Tools(legacy).OfType<JsonObject>())
                {
                    var match = item["match"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(match)) continue;
                    if (known.TryGetValue(match, out var current))
                    {
                        // 首次过渡尽可能保留旧顺序；不把整条旧定义覆盖到新版官方定义上。
                        var id = "official:" + match;
                        if (!states.OfType<JsonObject>().Any(s => Equal(s["id"], id)))
                        {
                            var migrated = new JsonObject { ["id"] = id };
                            if (item["order"] is not null && !JsonNode.DeepEquals(item["order"], current["order"]))
                                migrated["order"] = item["order"]!.DeepClone();
                            if (item["version"] is JsonValue oldVersion && oldVersion.TryGetValue<int>(out var old) &&
                                old > (current["version"]?.GetValue<int>() ?? 0)) migrated["version"] = old;
                            if (migrated.Count > 1) states.Add(migrated);
                        }
                        continue;
                    }
                    // 仅凭旧 match 无法知道真实分类；按目录名精确定位，避免模糊匹配收错工具。
                    if (!Directory.Exists(ToolCatalog.ToolsRoot)) continue;
                    foreach (var category in Directory.GetDirectories(ToolCatalog.ToolsRoot))
                    foreach (var directory in Directory.GetDirectories(category))
                    {
                        if (!Path.GetFileName(directory).Equals(match, StringComparison.OrdinalIgnoreCase)) continue;
                        if (userTools.OfType<JsonObject>().Any(t => MatchesDirectory(t, directory))) continue;
                        var entry = (JsonObject)item.DeepClone();
                        entry["id"] = "legacy:" + Guid.NewGuid().ToString("N");
                        entry["source"] = "legacy";
                        entry["directory"] = StoreDirectory(directory);
                        entry["category"] = Path.GetFileName(category);
                        entry.Remove("categories");
                        userTools.Add(entry);
                    }
                }
                migrations.Add(fingerprint);
                changed = true;
            }
            catch (Exception ex)
            {
                // 不写迁移完成标记：失败可在下一次刷新/启动重试，原文件完整保留。
                Debug.WriteLine($"[UserToolLibrary] 旧工具登记迁移失败 ({path}): {ex.Message}");
            }
        }
        if (changed)
        {
            WriteObject(StatePath, state);
            WriteObject(CatalogPath, catalog);
        }
    }
}

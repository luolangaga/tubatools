using System.Text.Json;
using Microsoft.Data.Sqlite;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// WinGet 官方源索引（source2.msix 内的 Public/index.db，SQLite）的本地缓存与查询。
/// 缓存目录 &lt;DataDir&gt;/Store：索引文件按版本号命名（下载后原子切换），
/// 更新时不会阻塞正在进行的查询；超过 24 小时才检查一次 Last-Modified，未变更不重复下载。
/// </summary>
internal static class WingetIndexStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    internal static string CacheDir => Path.Combine(ConfigManager.GetDataDir(), "Store");

    private static string MetadataPath => Path.Combine(CacheDir, "winget-index.json");

    /// <summary>当前可用的索引文件；尚未下载时返回 null。</summary>
    internal static string? CurrentIndexPath
    {
        get
        {
            try
            {
                var meta = ReadMetadata();
                if (meta?.File is { Length: > 0 } file)
                {
                    var path = Path.Combine(CacheDir, file);
                    if (File.Exists(path)) return path;
                }

                return Directory.Exists(CacheDir)
                    ? Directory.EnumerateFiles(CacheDir, "winget-index-*.db")
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault()
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>索引不存在或已过期时下载/更新；已有旧索引时更新失败只降级为继续使用旧索引。</summary>
    internal static async Task EnsureAsync(IProgress<string>? status, CancellationToken ct)
    {
        var current = CurrentIndexPath;
        if (current is not null && !NeedsCheck(current)) return;

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            current = CurrentIndexPath;
            if (current is not null && !NeedsCheck(current)) return;

            string? remoteLastModified = null;
            try
            {
                remoteLastModified = await WingetSourceClient.GetLastModifiedAsync("source2.msix", ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // HEAD 失败不影响后续（可能是代理只放行 GET）：直接按需下载
            }

            if (current is not null && remoteLastModified is not null
                && string.Equals(remoteLastModified, ReadMetadata()?.LastModified, StringComparison.Ordinal))
            {
                WriteMetadata(Path.GetFileName(current), remoteLastModified);
                Touch(current);
                return;
            }

            status?.Report(current is null
                ? "首次使用需要下载 WinGet 源索引（约 4 MB）…"
                : "正在更新 WinGet 源索引…");

            try
            {
                var indexBytes = await WingetSourceClient.DownloadIndexAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                WriteIndexFile(indexBytes, remoteLastModified);
                DeleteStaleIndexFiles();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogService.Warn(LogService.App, $"WinGet 源索引更新失败：{ex.Message}");
                if (current is null) throw;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>按名称/ID/别名模糊搜索，返回 winget search 等价结果。</summary>
    internal static List<WingetSearchResult> Search(string query, int limit)
    {
        var path = CurrentIndexPath ?? throw new InvalidOperationException("WinGet 源索引尚未下载。");
        var escaped = EscapeLike(query);

        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            select id, name, latest_version
            from packages
            where id like @like escape '\'
               or name like @like escape '\'
               or moniker like @like escape '\'
            order by
              case
                when id = @exact collate nocase then 0
                when moniker = @exact collate nocase then 1
                when name like @prefix escape '\' then 2
                when id like @prefix escape '\' then 3
                else 4
              end,
              length(name), name
            limit @limit
            """;
        command.Parameters.AddWithValue("@like", "%" + escaped + "%");
        command.Parameters.AddWithValue("@prefix", escaped + "%");
        command.Parameters.AddWithValue("@exact", query.Trim());
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<WingetSearchResult>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new WingetSearchResult
            {
                PackageIdentifier = reader.GetString(0),
                PackageName = reader.GetString(1),
                LatestVersion = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }

        return results;
    }

    /// <summary>取包的最新版本与 versionData 的 SHA-256（十六进制）。</summary>
    internal static (string LatestVersion, string HashHex)? FindPackage(string packageId)
    {
        var path = CurrentIndexPath ?? throw new InvalidOperationException("WinGet 源索引尚未下载。");

        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            select latest_version, hex(hash)
            from packages
            where id = @id collate nocase
            limit 1
            """;
        command.Parameters.AddWithValue("@id", packageId);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        var version = reader.IsDBNull(0) ? "" : reader.GetString(0);
        var hash = reader.IsDBNull(1) ? "" : reader.GetString(1);
        return hash.Length >= 8 ? (version, hash) : null;
    }

    private static SqliteConnection Open(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static bool NeedsCheck(string indexPath)
    {
        var checkedAt = ReadMetadata()?.CheckedAtUtc;
        if (checkedAt is { } stamp) return DateTimeOffset.UtcNow - stamp > CheckInterval;
        return DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(indexPath) > CheckInterval;
    }

    private static void Touch(string indexPath)
    {
        try { File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow); } catch { }
    }

    private static void WriteIndexFile(byte[] indexBytes, string? lastModified)
    {
        Directory.CreateDirectory(CacheDir);
        var fileName = $"winget-index-{DateTime.UtcNow:yyyyMMddHHmmss}.db";
        var target = Path.Combine(CacheDir, fileName);
        var temp = target + ".tmp";

        File.WriteAllBytes(temp, indexBytes);
        File.Move(temp, target, overwrite: true);
        WriteMetadata(fileName, lastModified);
    }

    private static void DeleteStaleIndexFiles()
    {
        var current = ReadMetadata()?.File;
        try
        {
            foreach (var file in Directory.EnumerateFiles(CacheDir, "winget-index-*.db"))
            {
                if (string.Equals(Path.GetFileName(file), current, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    private static IndexMetadata? ReadMetadata()
    {
        try
        {
            if (!File.Exists(MetadataPath)) return null;
            return JsonSerializer.Deserialize<IndexMetadata>(File.ReadAllText(MetadataPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteMetadata(string fileName, string? lastModified)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var metadata = new IndexMetadata
            {
                File = fileName,
                LastModified = lastModified,
                CheckedAtUtc = DateTimeOffset.UtcNow
            };
            File.WriteAllText(MetadataPath, JsonSerializer.Serialize(metadata, JsonOptions));
        }
        catch (Exception ex)
        {
            LogService.Warn(LogService.App, $"写入 WinGet 索引元数据失败：{ex.Message}");
        }
    }

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    private sealed class IndexMetadata
    {
        public string? File { get; set; }
        public string? LastModified { get; set; }
        public DateTimeOffset? CheckedAtUtc { get; set; }
    }
}

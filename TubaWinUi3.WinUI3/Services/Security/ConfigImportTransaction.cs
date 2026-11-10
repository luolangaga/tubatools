using System.ComponentModel;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TubaWinUi3.Storage;

namespace TubaWinUi3.Services;

/// <summary>Stage and validate everything before changing live files. Keep rollback bytes until commit.</summary>
internal static class ConfigImportTransaction
{
    internal const long MaxExpandedBytes = 8L * 1024 * 1024 * 1024;
    internal static Action<int>? CommitFailureForTests;

    internal static void Import(string zipPath, string root)
    {
        root = Path.GetFullPath(root);
        SafeSharePath.RejectReparsePoints(root);
        Directory.CreateDirectory(root);
        using var pins = new DirectoryPins();
        pins.Add(root);
        var work = Path.Combine(root, ".config-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var stage = Path.Combine(work, "stage");
        var backup = Path.Combine(work, "rollback");
        pins.Add(work); pins.Add(stage); pins.Add(backup);
        var files = new List<(string Relative, string Target, string Staged)>();
        var locks = new List<IDisposable>();
        var committed = new List<(string Target, string? Old)>();
        bool rollbackFailed = false;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (zip.Entries.Count > 100_000) throw new InvalidDataException("备份文件数量过多。");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long remaining = MaxExpandedBytes;
            foreach (var entry in zip.Entries)
            {
                var relative = entry.FullName.TrimEnd('/', '\\');
                var target = SafeSharePath.Resolve(root, relative);
                if (!seen.Add(target)) throw new InvalidDataException("备份包含重复路径。");
                if (ConfigBackupPolicy.Exclude(relative)) continue;
                // ZIP symlinks and special files are never restored as configuration.
                int kind = (entry.ExternalAttributes >> 16) & 0xF000;
                if (kind != 0 && kind != 0x8000 && kind != 0x4000) throw new InvalidDataException("备份包含链接或特殊文件。");
                if (entry.Name.Length == 0) continue;
                if (entry.Length > remaining) throw new InvalidDataException("备份展开后过大。");
                var staged = SafeSharePath.Resolve(stage, relative);
                pins.Add(Path.GetDirectoryName(staged)!);
                using (var input = entry.Open())
                using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[65536];
                    int read;
                    while ((read = input.Read(buffer)) > 0)
                    {
                        if (read > remaining) throw new InvalidDataException("备份展开后过大。");
                        remaining -= read; output.Write(buffer, 0, read);
                    }
                    output.Flush(true);
                }
                if (Path.GetExtension(staged).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var sanitized = ConfigBackupPolicy.Sanitize(File.ReadAllText(staged));
                    if (relative.Equals("settings.json", StringComparison.OrdinalIgnoreCase))
                        _ = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(sanitized)
                            ?? throw new InvalidDataException("设置格式无效。");
                    if (relative.Equals("favorites.json", StringComparison.OrdinalIgnoreCase)
                        && System.Text.Json.JsonSerializer.Deserialize<List<string>>(sanitized) is null)
                        throw new InvalidDataException("收藏格式无效。");
                    AtomicFile.WriteText(staged, sanitized, backup: false);
                }
                files.Add((relative, target, staged));
            }

            // Lock in deterministic order. No live file is touched until every path is pinned.
            foreach (var file in files.OrderBy(f => f.Target, StringComparer.OrdinalIgnoreCase))
            {
                pins.Add(Path.GetDirectoryName(file.Target)!);
                SafeSharePath.Resolve(root, file.Relative);
                locks.Add(AtomicFile.Lock(file.Target));
            }
            foreach (var file in files)
            {
                string? old = null;
                if (File.Exists(file.Target))
                {
                    old = SafeSharePath.Resolve(backup, file.Relative);
                    pins.Add(Path.GetDirectoryName(old)!);
                    File.Copy(file.Target, old);
                }
                CommitFailureForTests?.Invoke(committed.Count);
                File.Move(file.Staged, file.Target, overwrite: true);
                committed.Add((file.Target, old));
            }
        }
        catch
        {
            foreach (var item in committed.AsEnumerable().Reverse())
                try
                {
                    if (item.Old is null) File.Delete(item.Target);
                    else File.Move(item.Old, item.Target, overwrite: true);
                }
                catch { rollbackFailed = true; }
            if (rollbackFailed) throw new IOException("配置恢复失败，原文件副本保留于：" + backup);
            throw;
        }
        finally
        {
            foreach (var lease in locks.AsEnumerable().Reverse()) lease.Dispose();
            // Retain rollback files if restoration failed, so no original bytes are discarded.
            pins.Dispose();
            if (!rollbackFailed) try { SafeSharePath.DeleteTree(work); } catch { }
        }
    }

    /// <summary>Pin all existing/created ancestor directories against renames while committing.</summary>
    private sealed class DirectoryPins : IDisposable
    {
        private readonly Dictionary<string, SafeFileHandle> _handles = new(StringComparer.OrdinalIgnoreCase);
        internal void Add(string path)
        {
            path = Path.GetFullPath(path);
            if (_handles.ContainsKey(path)) return;
            if (Path.GetDirectoryName(path) is { } parent) Add(parent);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            var handle = CreateFile(path, 0x80, FileShare.Read | FileShare.Write, 0, FileMode.Open,
                0x02000000 | 0x00200000, 0); // backup semantics + open reparse point
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            _handles.Add(path, handle);
            SafeSharePath.RejectReparsePoints(path);
        }
        public void Dispose() { foreach (var handle in _handles.Values.Reverse()) handle.Dispose(); }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandle CreateFile(string path, uint access, FileShare share, nint security,
            FileMode mode, uint flags, nint template);
    }
}

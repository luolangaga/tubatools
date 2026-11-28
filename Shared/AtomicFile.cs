using System.Security.Cryptography;
using System.Text;

namespace TubaWinUi3.Storage;

/// <summary>Same-directory durable replacement and a shared per-file cross-process lock.</summary>
internal static class AtomicFile
{
    internal static IDisposable Lock(string path)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        Mutex mutex;
        try
        {
            mutex = new Mutex(false, @"Local\TubaWinUi3.Storage-" + name);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            // 同一会话里另一个账户（提权/切换用户）创建的互斥体 DACL 可能打不开。
            // 跨进程串行只是尽力而为，不能因此让配置文件彻底写不进去。
            return NoLease.Instance;
        }
        try
        {
            try { if (!mutex.WaitOne(TimeSpan.FromSeconds(10))) throw new IOException("配置文件正在使用，请稍后重试。"); }
            catch (AbandonedMutexException) { }
            return new Lease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    private sealed class NoLease : IDisposable
    {
        internal static readonly NoLease Instance = new();
        public void Dispose() { }
    }

    private sealed class Lease(Mutex mutex) : IDisposable
    {
        public void Dispose() { try { mutex.ReleaseMutex(); } finally { mutex.Dispose(); } }
    }

    internal static void WriteText(string path, string text, bool backup = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        ClearReadOnly(path);
        if (backup) ClearReadOnly(path + ".bak");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
                writer.Write(text);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            Commit(temporary, path, backup);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Commit(string temporary, string path, bool backup)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    if (backup && TryReplace(temporary, path)) return;
                    File.Move(temporary, path, overwrite: true);
                }
                else File.Move(temporary, path);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 2)
            {
                // 杀毒/索引器造成的瞬时占用：短退避后重试，别把一次扫描当成永久失败。
                Thread.Sleep(60 * (attempt + 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File.Replace 需要目标文件的删除权限，部分安全软件/被改窄的 ACL/过滤器驱动会拒绝，
                // 之后每次保存都会失败。退到旧版本的原地覆写——它只需要写权限。
                File.Copy(temporary, path, overwrite: true);
                return;
            }
        }
    }

    private static bool TryReplace(string temporary, string path)
    {
        try
        {
            File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // 轮转备份是尽力而为：备份不可写不能拖垮主文件的写入。
            return false;
        }
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static string? ReadText(string path, Action<string> validate)
    {
        var busy = false;
        try
        {
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path); validate(text); return text;
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
        {
            // Quarantine only a file whose content is really broken; repeated UI reads must not
            // create unlimited copies of a bad file.
            if (File.Exists(path)) File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
            if (!File.Exists(path + ".bak")) throw;
        }
        catch (IOException)
        {
            // 瞬时占用（杀毒/同步/备份）读失败：主文件很可能完好，绝不能改名隔离。
            busy = true;
            if (!File.Exists(path + ".bak")) throw;
        }
        if (!File.Exists(path + ".bak")) return null;
        var recovered = File.ReadAllText(path + ".bak");
        validate(recovered);
        if (busy) return recovered;
        // Keep the known-good backup instead of rotating the damaged primary into it.
        if (File.Exists(path)) File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
        WriteText(path, recovered, backup: false);
        return recovered;
    }
}

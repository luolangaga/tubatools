using System.Security.Cryptography;
using System.Text;

namespace TubaWinUi3.Storage;

/// <summary>Same-directory durable replacement and a shared per-file cross-process lock.</summary>
internal static class AtomicFile
{
    internal static IDisposable Lock(string path)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        var mutex = new Mutex(false, @"Local\TubaWinUi3.Storage-" + name);
        try
        {
            try { if (!mutex.WaitOne(TimeSpan.FromSeconds(10))) throw new IOException("配置文件正在使用，请稍后重试。"); }
            catch (AbandonedMutexException) { }
            return new Lease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    private sealed class Lease(Mutex mutex) : IDisposable
    {
        public void Dispose() { try { mutex.ReleaseMutex(); } finally { mutex.Dispose(); } }
    }

    internal static void WriteText(string path, string text, bool backup = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
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
            if (File.Exists(path)) File.Replace(temporary, path, backup ? path + ".bak" : null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static string? ReadText(string path, Action<string> validate)
    {
        try
        {
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path); validate(text); return text;
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            // Quarantine once; repeated UI reads must not create unlimited copies of a bad file.
            if (File.Exists(path)) File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
            if (!File.Exists(path + ".bak")) throw;
        }
        if (!File.Exists(path + ".bak")) return null;
        var recovered = File.ReadAllText(path + ".bak");
        validate(recovered);
        // Keep the known-good backup instead of rotating the damaged primary into it.
        if (File.Exists(path)) File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
        WriteText(path, recovered, backup: false);
        return recovered;
    }
}

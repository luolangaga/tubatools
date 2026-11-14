namespace Tubatools.AiJunkCleanup.Services;

/// <summary>只负责“打开所在位置”。真正的删除在 FastFileDeleter 中直接完成，不走回收站/IFileOperation。</summary>
internal static class ShellFileOperations
{
    internal static void Reveal(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var info = new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
                Arguments = Directory.Exists(full) ? $"\"{full.Replace("\"", "")}\"" : $"/select,\"{full.Replace("\"", "")}\""
            };
            System.Diagnostics.Process.Start(info);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { }
    }
}

namespace TubaWinUi3.Services;

internal static class SafeSharePath
{
    internal static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 1024 || Path.IsPathRooted(relative)
            || relative.Contains(':') || relative.Contains('\0'))
            throw new UnauthorizedAccessException("文件路径不合法");
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length > 32) throw new UnauthorizedAccessException("目录层级过深");
        foreach (var part in parts)
        {
            if (part.Length is 0 or > 255 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsDeviceName(part))
                throw new UnauthorizedAccessException("文件路径不合法");
        }
        root = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("文件必须位于共享目录内");
        RejectReparsePoints(root);
        var current = root;
        foreach (var part in parts) { current = Path.Combine(current, part); RejectReparsePoints(current); }
        return path;
    }

    internal static void RejectReparsePoints(string path)
    {
        // Check ancestors as well: a configured share root can itself be below a junction.
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("共享目录不允许符号链接或目录联接");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static void DeleteTree(string directory)
    {
        RejectReparsePoints(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            RejectReparsePoints(entry);
            if (Directory.Exists(entry)) DeleteTree(entry); else File.Delete(entry);
        }
        Directory.Delete(directory, false);
    }

    private static bool IsDeviceName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]));
    }
}

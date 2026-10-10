using System.IO.Compression;

namespace TubaWinUi3.Services;

public sealed record ImportableExecutable(string EntryPath)
{
    public string FileName => Path.GetFileName(EntryPath);

    public override string ToString() => EntryPath.Replace('/', Path.DirectorySeparatorChar);
}

public sealed record ImportArchVariant(string EntryPath, string Arch);

public sealed record CustomToolImportRequest(
    string PackagePath,
    string ToolName,
    string Category,
    string PrimaryExecutableEntry,
    string? Description,
    string? Publisher,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ImportArchVariant> ArchVariants);

public sealed record CustomToolImportResult(string ToolDirectory, string PrimaryExecutablePath);

public static class CustomToolPackageService
{
    public static bool TryGetExecutables(string packagePath, out IReadOnlyList<ImportableExecutable> executables, out string? error)
    {
        executables = [];
        error = null;
        try
        {
            executables = GetExecutables(packagePath);
            return true;
        }
        catch (InvalidDataException)
        {
            error = LocalizationService.L("Community_InvalidZip", "无法读取 ZIP 压缩包：文件可能损坏、下载不完整，或并非 ZIP 格式。请重新下载或重新打包后选择。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = string.Format(LocalizationService.L("Community_UnreadableZip", "无法读取压缩包，请确认文件仍存在且有读取权限：{0}"), ex.Message);
        }
        return false;
    }

    private static readonly string[] ExecutableExtensions =
    [
        ".exe"
    ];

    public static IReadOnlyList<ImportableExecutable> GetExecutables(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Where(entry => ExecutableExtensions.Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase))
            .Select(entry => new ImportableExecutable(NormalizeEntryPath(entry.FullName)))
            .OrderBy(entry => entry.EntryPath, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static async Task<CustomToolImportResult> ImportAsync(CustomToolImportRequest request)
    {
        if (!File.Exists(request.PackagePath))
            throw new FileNotFoundException("压缩包不存在。", request.PackagePath);

        var category = SanitizePathSegment(request.Category);
        if (string.IsNullOrWhiteSpace(category))
            throw new InvalidOperationException("分类不能为空。");

        var toolName = SanitizePathSegment(request.ToolName);
        if (string.IsNullOrWhiteSpace(toolName))
            toolName = Path.GetFileNameWithoutExtension(request.PrimaryExecutableEntry);

        if (string.IsNullOrWhiteSpace(toolName))
            throw new InvalidOperationException("工具名称不能为空。");

        var categoryRoot = Path.Combine(ToolCatalog.UserToolsRoot, "Custom", category);
        Directory.CreateDirectory(categoryRoot);

        var toolDirectory = GetUniqueDirectory(Path.Combine(categoryRoot, toolName));
        Directory.CreateDirectory(toolDirectory);

        await Task.Run(() => ExtractPackage(request.PackagePath, toolDirectory));

        var primaryPath = Path.Combine(toolDirectory, request.PrimaryExecutableEntry.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(primaryPath))
            throw new FileNotFoundException("导入后没有找到所选主程序。", primaryPath);

        UpsertMetadata(request, toolDirectory);

        ToolMetadataService.InvalidateCache();
        ToolCatalog.InvalidateTagsCache();

        return new CustomToolImportResult(toolDirectory, primaryPath);
    }

    public static async Task ExportCurrentAppAsync(string destinationZipPath)
    {
        var appDirectory = ToolCatalog.AppDirectory;
        if (!Directory.Exists(appDirectory))
            throw new DirectoryNotFoundException(appDirectory);

        var destinationFullPath = Path.GetFullPath(destinationZipPath);
        var parent = Path.GetDirectoryName(destinationFullPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        if (File.Exists(destinationFullPath))
            File.Delete(destinationFullPath);

        _ = ToolMetadataService.GetUserTools();
        await Task.Run(() =>
        {
            var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void AddDirectory(string directory, string prefix)
            {
                if (!Directory.Exists(directory)) return;
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    if (Path.GetFullPath(file).Equals(destinationFullPath, StringComparison.OrdinalIgnoreCase)) continue;
                    entries[Path.Combine(prefix, Path.GetRelativePath(directory, file)).Replace('\\', '/')] = file;
                }
            }
            AddDirectory(appDirectory, "");
            // 用户库可能位于 AppData/自定义目录；整合包必须带上登记与文件。
            AddDirectory(ToolCatalog.ToolsRoot, "Tools");
            AddDirectory(ToolCatalog.UserToolsRoot, "Data/UserTools");
            foreach (var name in new[] { "user-tools.json", "tool-state.json" })
            {
                var source = Path.Combine(UserToolLibrary.DataDirectory, name);
                if (File.Exists(source)) entries["Data/" + name] = source;
            }
            entries.Remove("Data/.config_location");
            using var archive = ZipFile.Open(destinationFullPath, ZipArchiveMode.Create);
            foreach (var (entry, source) in entries) archive.CreateEntryFromFile(source, entry, CompressionLevel.Optimal);
            using var marker = new StreamWriter(archive.CreateEntry("Data/.config_location").Open());
            marker.Write("AppRoot");
        });
    }

    public static async Task<CustomToolImportResult> ImportSingleFileAsync(
        string sourceFilePath,
        string toolName,
        string category,
        string? description,
        string? publisher,
        IReadOnlyList<string> tags)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("源文件不存在。", sourceFilePath);

        var cat = SanitizePathSegment(category);
        if (string.IsNullOrWhiteSpace(cat))
            throw new InvalidOperationException("分类不能为空。");

        var name = SanitizePathSegment(toolName);
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileNameWithoutExtension(sourceFilePath);

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("工具名称不能为空。");

        var categoryRoot = Path.Combine(ToolCatalog.UserToolsRoot, "Custom", cat);
        Directory.CreateDirectory(categoryRoot);

        var toolDirectory = GetUniqueDirectory(Path.Combine(categoryRoot, name));
        Directory.CreateDirectory(toolDirectory);

        var destFileName = Path.GetFileName(sourceFilePath);
        var destFilePath = Path.Combine(toolDirectory, destFileName);

        await Task.Run(() => File.Copy(sourceFilePath, destFilePath, overwrite: true));

        var request = new CustomToolImportRequest(
            sourceFilePath,
            name,
            cat,
            destFileName,
            description,
            publisher,
            tags,
            []);

        UpsertMetadata(request, toolDirectory);

        ToolMetadataService.InvalidateCache();
        ToolCatalog.InvalidateTagsCache();

        return new CustomToolImportResult(toolDirectory, destFilePath);
    }

    private static void ExtractPackage(string packagePath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        using var archive = ZipFile.OpenRead(packagePath);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var entryPath = NormalizeEntryPath(entry.FullName).Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entryPath));
            if (!destinationPath.StartsWith(destinationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("压缩包包含不安全的路径。");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void UpsertMetadata(CustomToolImportRequest request, string toolDirectory)
    {
        ToolMetadataService.UpsertToolMetadataEntry(
            Path.GetFileName(toolDirectory),
            toolDirectory: toolDirectory,
            category: request.Category,
            launchTarget: NormalizeEntryPath(request.PrimaryExecutableEntry).Replace('/', '\\'),
            name: request.ToolName,
            description: request.Description,
            publisher: request.Publisher,
            tags: request.Tags,
            archVariants: request.ArchVariants
                .Where(variant => !string.IsNullOrWhiteSpace(variant.EntryPath) && !string.IsNullOrWhiteSpace(variant.Arch))
                .Select(variant => new ToolMetadataService.JsonArchVariant
                {
                    File = NormalizeEntryPath(variant.EntryPath).Replace('/', '\\'),
                    Arch = variant.Arch.Trim()
                })
                .ToList());
    }

    private static string GetUniqueDirectory(string desiredDirectory)
    {
        if (!Directory.Exists(desiredDirectory))
            return desiredDirectory;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{desiredDirectory}-{i}";
            if (!Directory.Exists(candidate))
                return candidate;
        }

        throw new IOException("无法创建唯一的工具目录。");
    }

    private static string NormalizeEntryPath(string entryPath) =>
        entryPath.Replace('\\', '/').TrimStart('/');

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();
        return new string(chars).Trim();
    }

}

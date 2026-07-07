using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        var categoryRoot = Path.Combine(ToolCatalog.ToolsRoot, category);
        Directory.CreateDirectory(categoryRoot);

        var toolDirectory = GetUniqueDirectory(Path.Combine(categoryRoot, toolName));
        Directory.CreateDirectory(toolDirectory);

        await Task.Run(() => ExtractPackage(request.PackagePath, toolDirectory));

        var primaryPath = Path.Combine(toolDirectory, request.PrimaryExecutableEntry.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(primaryPath))
            throw new FileNotFoundException("导入后没有找到所选主程序。", primaryPath);

        await UpsertMetadataAsync(request, Path.GetFileName(toolDirectory));

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

        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(destinationFullPath, ZipArchiveMode.Create);
            foreach (var file in Directory.EnumerateFiles(appDirectory, "*", SearchOption.AllDirectories))
            {
                var fullPath = Path.GetFullPath(file);
                if (fullPath.Equals(destinationFullPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relativePath = Path.GetRelativePath(appDirectory, fullPath);
                archive.CreateEntryFromFile(fullPath, relativePath, CompressionLevel.Optimal);
            }
        });
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

    private static async Task UpsertMetadataAsync(CustomToolImportRequest request, string metadataMatch)
    {
        var metadataRoot = FindRoot("Metadata");
        Directory.CreateDirectory(metadataRoot);
        var metadataPath = Path.Combine(metadataRoot, "tools.json");

        JsonObject root;
        JsonArray tools;

        if (File.Exists(metadataPath))
        {
            await using var readStream = File.OpenRead(metadataPath);
            root = await JsonNode.ParseAsync(readStream) as JsonObject ?? new JsonObject();
            tools = root["tools"] as JsonArray ?? [];
        }
        else
        {
            root = new JsonObject();
            tools = [];
        }

        root["tools"] = tools;

        var existing = tools
            .OfType<JsonObject>()
            .FirstOrDefault(item =>
                string.Equals(item["match"]?.GetValue<string>(), metadataMatch, StringComparison.CurrentCultureIgnoreCase));

        if (existing is not null)
            tools.Remove(existing);

        var metadata = new JsonObject
        {
            ["match"] = metadataMatch,
            ["description"] = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ["publisher"] = string.IsNullOrWhiteSpace(request.Publisher) ? null : request.Publisher.Trim()
        };

        if (request.Tags.Count > 0)
        {
            metadata["tags"] = new JsonArray(request.Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => JsonValue.Create(tag.Trim()))
                .ToArray<JsonNode?>());
        }

        var variants = request.ArchVariants
            .Where(variant => !string.IsNullOrWhiteSpace(variant.EntryPath) && !string.IsNullOrWhiteSpace(variant.Arch))
            .Select(variant => new JsonObject
            {
                ["file"] = NormalizeEntryPath(variant.EntryPath).Replace('/', '\\'),
                ["arch"] = variant.Arch.Trim()
            })
            .ToArray<JsonNode?>();

        if (variants.Length > 0)
            metadata["archVariants"] = new JsonArray(variants);

        tools.Add(metadata);

        await using var writeStream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(writeStream, root, TubaDefaultIndentedContext.Default.JsonNode);
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

    private static string FindRoot(string folderName)
    {
        var appDir = ToolCatalog.AppDirectory;
        var outputRoot = Path.Combine(appDir, folderName);
        if (Directory.Exists(outputRoot))
            return outputRoot;

        var directory = new DirectoryInfo(appDir);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, folderName);
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return outputRoot;
    }
}

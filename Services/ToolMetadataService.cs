using System.Diagnostics;
using System.Text.Json;

namespace TubaWinUi3.Services;

public sealed record ToolMetadata(
    string? Description,
    string? Publisher,
    string? Version,
    string? DatabaseSource,
    string? DownloadUrl,
    string? DownloadFilter);

public sealed record JsonArchVariantResult(string? File, string? Dir, string? Arch);

public static class ToolMetadataService
{
    private static IReadOnlyList<JsonToolMetadata>? _metadata;

    public static bool HasDownloadUrl(string category, string toolDir)
    {
        var dirName = Path.GetFileName(toolDir);
        var metadata = LoadMetadata();

        return metadata.Any(item =>
            !string.IsNullOrWhiteSpace(item.Match) &&
            !string.IsNullOrWhiteSpace(item.DownloadUrl) &&
            dirName.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase));
    }

    public static ToolMetadata GetMetadata(string category, string toolPath)
    {
        FileVersionInfo? versionInfo = null;
        try
        {
            if (File.Exists(toolPath))
                versionInfo = FileVersionInfo.GetVersionInfo(toolPath);
        }
        catch { }

        var jsonMetadata = FindJsonMetadata(toolPath);
        var description = FirstUseful(
            jsonMetadata?.Description,
            versionInfo?.FileDescription,
            versionInfo?.ProductName,
            ReadFolderDescription(toolPath));

        return new ToolMetadata(
            description,
            FirstUseful(jsonMetadata?.Publisher, versionInfo?.CompanyName, versionInfo?.LegalCopyright),
            FirstUseful(versionInfo?.ProductVersion, versionInfo?.FileVersion),
            jsonMetadata is null ? null : "JSON",
            jsonMetadata?.DownloadUrl,
            jsonMetadata?.DownloadFilter);
    }

    public static IReadOnlyList<JsonArchVariantResult> GetArchVariants(string toolPath, string? toolDir = null)
    {
        var jsonMetadata = FindJsonMetadata(toolPath);
        if (jsonMetadata is null && toolDir is not null)
            jsonMetadata = FindJsonMetadataByDir(toolDir);

        if (jsonMetadata?.ArchVariants is null || jsonMetadata.ArchVariants.Count == 0)
            return [];

        return jsonMetadata.ArchVariants
            .Select(v => new JsonArchVariantResult(v.File, v.Dir, v.Arch))
            .ToList();
    }

    private static JsonToolMetadata? FindJsonMetadata(string toolPath)
    {
        var metadata = LoadMetadata();
        var fileName = Path.GetFileNameWithoutExtension(toolPath);
        var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, toolPath);
        var dirName = Path.GetFileName(Path.GetDirectoryName(toolPath));

        return metadata.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.Match) &&
            (fileName.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
             relativePath.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
             MatchesFlexible(dirName, item.Match)));
    }

    private static JsonToolMetadata? FindJsonMetadataByDir(string toolDir)
    {
        var metadata = LoadMetadata();
        var dirName = Path.GetFileName(toolDir);
        var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, toolDir);

        return metadata.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.Match) &&
            (relativePath.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
             MatchesFlexible(dirName, item.Match)));
    }

    private static bool MatchesFlexible(string? source, string match)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (source.Contains(match, StringComparison.CurrentCultureIgnoreCase))
            return true;

        var normalizedSource = source.Replace(" ", "", StringComparison.Ordinal)
                                      .Replace("-", "", StringComparison.Ordinal)
                                      .Replace("_", "", StringComparison.Ordinal);
        var normalizedMatch = match.Replace(" ", "", StringComparison.Ordinal)
                                   .Replace("-", "", StringComparison.Ordinal)
                                   .Replace("_", "", StringComparison.Ordinal);

        return normalizedSource.Contains(normalizedMatch, StringComparison.CurrentCultureIgnoreCase);
    }

    private static IReadOnlyList<JsonToolMetadata> LoadMetadata()
    {
        if (_metadata is not null)
        {
            return _metadata;
        }

        var path = Path.Combine(FindRoot("Metadata"), "tools.json");
        if (!File.Exists(path))
        {
            _metadata = [];
            return _metadata;
        }

        using var stream = File.OpenRead(path);
        var database = JsonSerializer.Deserialize<JsonToolDatabase>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        _metadata = database?.Tools ?? [];
        return _metadata;
    }

    private static string? ReadFolderDescription(string toolPath)
    {
        var directory = Path.GetDirectoryName(toolPath);
        if (directory is null)
        {
            return null;
        }

        var textFile = Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileName(path).Contains("readme", StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetFileName(path).Contains("说明", StringComparison.CurrentCultureIgnoreCase) ||
                                    Path.GetFileName(path).Contains("What's New", StringComparison.OrdinalIgnoreCase));
        if (textFile is null)
        {
            return null;
        }

        try
        {
            var text = File.ReadLines(textFile).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
            return text is { Length: > 160 } ? text[..160] : text;
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstUseful(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string FindRoot(string folderName)
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, folderName);
        if (Directory.Exists(outputRoot))
        {
            return outputRoot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, folderName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return outputRoot;
    }

    private sealed class JsonToolDatabase
    {
        public List<JsonToolMetadata> Tools { get; set; } = [];
    }

    private sealed class JsonToolMetadata
    {
        public string? Match { get; set; }

        public string? Description { get; set; }

        public string? Publisher { get; set; }

        public string? DownloadUrl { get; set; }

        public string? DownloadFilter { get; set; }

        public List<JsonArchVariant>? ArchVariants { get; set; }
    }

    private sealed class JsonArchVariant
    {
        public string? File { get; set; }

        public string? Dir { get; set; }

        public string? Arch { get; set; }
    }
}

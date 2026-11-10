using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services.AiAgent;

public enum AgentCanvasMode { Chat, Teach }

/// <summary>
/// One canvas message: an immutable page the assistant "sent" at a point in the conversation.
/// <see cref="RelativePath"/> is either the live <c>index.html</c> (newest, still editable) or an
/// archived snapshot under <c>history/</c> frozen when the next update began.
/// </summary>
public sealed record CanvasDocument(string Id, string AfterMessageId, string RelativePath, string Title, string Hash, long CreatedAt);

/// <summary>
/// Teaching canvas files. Every session owns <c>canvas/&lt;id&gt;/</c> inside the agent workspace,
/// so the model can write it with its normal file tools and the host can serve it through a
/// single WebView2 virtual host.
/// </summary>
public static class AgentCanvasStore
{
    public const string FolderName = "canvas";
    public const string IndexName = "index.html";
    private const string DocumentsName = "documents.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Root => Path.Combine(AgentWorkspace.DirectoryPath, FolderName);

    public static string DirectoryFor(string sessionId) => Path.Combine(Root, SafeId(sessionId));
    public static string IndexPath(string sessionId) => Path.Combine(DirectoryFor(sessionId), IndexName);
    public static string AssetsPath(string sessionId) => Path.Combine(DirectoryFor(sessionId), "assets");

    /// <summary>Workspace-relative spelling handed to the model; always forward slashes.</summary>
    public static string RelativeIndex(string sessionId) => FolderName + "/" + SafeId(sessionId) + "/" + IndexName;

    public static bool Exists(string? sessionId) =>
        !string.IsNullOrWhiteSpace(sessionId) && File.Exists(IndexPath(sessionId));

    /// <summary>A compact, visible marker prepended to teaching prompts.</summary>
    public static string PromptPrefix(string sessionId) =>
        "〔教学模式 · 画布 " + RelativeIndex(sessionId) + "〕";

    public static string EnsureDirectory(string sessionId)
    {
        var directory = DirectoryFor(sessionId);
        Directory.CreateDirectory(directory);
        var assets = Path.Combine(directory, "assets");
        Directory.CreateDirectory(assets);
        // The skill tells the model to reuse this page's style and 3D code instead of
        // inventing a look from scratch, so it must exist in every canvas folder.
        try
        {
            var reference = Path.Combine(AppContext.BaseDirectory, "Assets", "AiAgent", "tuba-canvas", "reference.html");
            var target = Path.Combine(assets, "reference.html");
            if (File.Exists(reference) && !File.Exists(target)) File.Copy(reference, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return directory;
    }

    // ---- canvas messages -------------------------------------------------------------------

    private static string DocumentsPath(string sessionId) => Path.Combine(DirectoryFor(sessionId), DocumentsName);

    public static IReadOnlyList<CanvasDocument> ReadDocuments(string sessionId)
    {
        try
        {
            var path = DocumentsPath(sessionId);
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<CanvasDocument[]>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public static void SaveDocuments(string sessionId, IReadOnlyList<CanvasDocument> documents)
    {
        try
        {
            var path = DocumentsPath(sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(documents, Options), new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Copy of the live page that can never be overwritten by later turns.</summary>
    public static string? Snapshot(string sessionId)
    {
        var index = IndexPath(sessionId);
        if (!File.Exists(index)) return null;
        string html;
        try { html = File.ReadAllText(index); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        return WriteArchive(sessionId, html);
    }

    /// <summary>Writes already-finished HTML into a fresh archive folder.</summary>
    public static string? WriteArchive(string sessionId, string html)
    {
        var normalized = NormalizeHtml(html);
        if (normalized is null) return null;
        var name = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6];
        var relative = "history/" + name + "/" + IndexName;
        var directory = Path.Combine(DirectoryFor(sessionId), "history", name);
        try
        {
            Directory.CreateDirectory(directory);
            // Snapshots live one level deeper, so relative asset URLs need the session root.
            File.WriteAllText(Path.Combine(directory, IndexName), InjectBase(normalized, "../../"), new UTF8Encoding(false));
            return relative;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static string? ResolvePage(string sessionId, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal)) return null;
        if (!normalized.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) return null;
        var root = DirectoryFor(sessionId);
        var full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
    }

    public static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    public static string? HashIndex(string sessionId)
    {
        try { return File.Exists(IndexPath(sessionId)) ? HashText(File.ReadAllText(IndexPath(sessionId))) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string InjectBase(string html, string href)
    {
        var tag = "<base href=\"" + href + "\">";
        var head = Regex.Match(html, "<head[^>]*>", RegexOptions.IgnoreCase);
        if (head.Success) return html.Insert(head.Index + head.Length, tag);
        var root = Regex.Match(html, "<html[^>]*>", RegexOptions.IgnoreCase);
        if (root.Success) return html.Insert(root.Index + root.Length, "<head>" + tag + "</head>");
        return tag + html;
    }

    // ---- html helpers ----------------------------------------------------------------------

    /// <summary>Full-page HTML from a model answer. Returns null when the block is unusable.</summary>
    public static string? ExtractHtmlBlock(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return null;
        var match = Regex.Match(markdown, "```[ \t]*(?:html|HTML)[ \t]*\r?\n(?<body>[\\s\\S]*?)```", RegexOptions.CultureInvariant);
        return match.Success ? NormalizeHtml(match.Groups["body"].Value) : null;
    }

    /// <summary>Accepts a complete document or wraps a bare fragment; null when nothing renderable.</summary>
    public static string? NormalizeHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = html.Trim();
        if (!text.Contains('<')) return null;
        if (text.Contains("<html", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase))
            return LooksComplete(text) ? text : null;
        return "<!DOCTYPE html>\n<html lang=\"zh-CN\">\n<head>\n<meta charset=\"utf-8\">\n" +
               "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n</head>\n<body>\n" + text +
               "\n</body>\n</html>";
    }

    /// <summary>
    /// A partial write would render a broken page mid-stream, so a document only counts as
    /// renderable once its closing tag has landed.
    /// </summary>
    public static bool LooksComplete(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return false;
        var text = html.AsSpan().TrimEnd();
        return text.EndsWith("</html>", StringComparison.OrdinalIgnoreCase) ||
               text.EndsWith("</html >", StringComparison.OrdinalIgnoreCase);
    }

    // ---- images ----------------------------------------------------------------------------

    /// <summary>Copies an image into <c>assets/</c>; returns the file name to reference in HTML.</summary>
    public static string? ImportImage(string sessionId, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !File.Exists(sourcePath)) return null;
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".avif")) return null;
        var info = new FileInfo(sourcePath);
        if (info.Length == 0 || info.Length > 20 * 1024 * 1024) return null;
        byte[] bytes;
        try { bytes = File.ReadAllBytes(sourcePath); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        var assets = AssetsPath(sessionId);
        Directory.CreateDirectory(assets);
        var stem = SafeFileName(Path.GetFileNameWithoutExtension(sourcePath));
        if (stem.Length > 40) stem = stem[..40];
        var name = stem + "-" + Convert.ToHexString(SHA256.HashData(bytes))[..8].ToLowerInvariant() + extension;
        var target = Path.Combine(assets, name);
        if (!File.Exists(target))
        {
            var temporary = target + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, target, true);
        }
        return name;
    }

    public static IReadOnlyList<string> ListImages(string sessionId)
    {
        var assets = AssetsPath(sessionId);
        if (!Directory.Exists(assets)) return [];
        return Directory.EnumerateFiles(assets)
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".avif", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string SafeFileName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name ?? "")
            builder.Append(char.IsControl(c) || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' ? '_' : c);
        var text = builder.ToString().Trim(' ', '.', '_');
        return text.Length == 0 ? "image" : text;
    }

    /// <summary>Session ids become directory names; keep them readable but filesystem safe.</summary>
    internal static string SafeId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return "draft";
        var builder = new StringBuilder(sessionId.Length);
        foreach (var c in sessionId) builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        var text = builder.ToString().Trim('_');
        if (text.Length == 0) text = "session";
        if (text.Length > 48) text = text[..48];
        if (!string.Equals(text, sessionId, StringComparison.Ordinal) || sessionId.Length > 48)
            text += "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)))[..8].ToLowerInvariant();
        if (IsReserved(text)) text = "_" + text;
        return text;
    }

    private static bool IsReserved(string name) => name.ToUpperInvariant() is
        "CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or
        "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9";
}
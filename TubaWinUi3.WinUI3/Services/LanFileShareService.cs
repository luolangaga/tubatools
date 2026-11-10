using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace TubaWinUi3.Services;

public static class LanFileShareService
{
    private static LanHttpsServer? _server;
    private static readonly SemaphoreSlim Lifecycle = new(1, 1);
    private static readonly SemaphoreSlim Mutations = new(1, 1);
    private static readonly ConcurrentDictionary<string, SharedFileInfo> _sharedFiles = new();
    private static string _shareDir = "";
    private static int _port = 18080;
    private static string _accessToken = "";
    private const string CookieName = "__Host-tuba-share";
    internal const long MaxUploadBytes = 2L * 1024 * 1024 * 1024;
    internal const long MaxShareBytes = 20L * 1024 * 1024 * 1024;
    internal static Func<long>? AvailableSpaceForTests;
    public static bool IsRunning => _server is not null;
    public static int Port => _port;
    public static string ShareDir => _shareDir;
    public static IReadOnlyDictionary<string, SharedFileInfo> SharedFiles => _sharedFiles;
    public static event Action? StateChanged;
    public static string CertificateFingerprint => LanHttpsServer.Fingerprint;
    public static string GetLocalIp() => PhoneLinkService.GetLocalIps().First();
    public static string BuildAccessUrl(string? ip = null) => $"https://{ip ?? GetLocalIp()}:{_port}/#access={_accessToken}";

    public static void Initialize()
    {
        _shareDir = Path.Combine(ConfigManager.GetDataDir(), "LanShare");
        SafeSharePath.RejectReparsePoints(_shareDir);
        Directory.CreateDirectory(_shareDir);
    }
    public static void SetPort(int port)
    {
        if (IsRunning || port is < 1024 or > 65535) return;
        _port = port; StateChanged?.Invoke();
    }
    public static async Task StartAsync()
    {
        await Lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRunning) return;
            Initialize();
            _accessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            for (int attempt = 0; ; attempt++)
            {
                try { _server = await LanHttpsServer.StartAsync(_port, MaxUploadBytes, HandleRequest).ConfigureAwait(false); break; }
                catch (IOException) when (attempt < 9 && _port < 65535) { _port++; }
            }
            RefreshSharedFiles(); StateChanged?.Invoke();
        }
        finally { Lifecycle.Release(); }
    }
    public static void Stop()
    {
        Lifecycle.Wait();
        try
        {
            var server = Interlocked.Exchange(ref _server, null);
            _accessToken = ""; server?.Dispose(); StateChanged?.Invoke();
        }
        finally { Lifecycle.Release(); }
    }

    private static IEnumerable<string> EnumerateSafe(string directory)
    {
        SafeSharePath.RejectReparsePoints(directory);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            SafeSharePath.RejectReparsePoints(path);
            yield return path;
            if (Directory.Exists(path)) foreach (var child in EnumerateSafe(path)) yield return child;
        }
    }
    public static void RefreshSharedFiles()
    {
        _sharedFiles.Clear();
        if (!Directory.Exists(_shareDir)) return;
        foreach (var file in EnumerateSafe(_shareDir).Where(File.Exists))
        {
            var fi = new FileInfo(file);
            var rel = Path.GetRelativePath(_shareDir, file).Replace('\\', '/');
            _sharedFiles[rel] = new SharedFileInfo { RelativePath = rel, FileName = fi.Name, Size = fi.Length,
                LastModified = fi.LastWriteTimeUtc, ContentType = GetContentType(fi.Extension) };
        }
    }

    public static async Task AddFileAsync(string sourcePath, bool copy = true)
    {
        if (!File.Exists(sourcePath)) return;
        var fileName = Path.GetFileName(sourcePath);
        var destPath = SafeSharePath.Resolve(_shareDir, fileName);

        var counter = 1;
        while (File.Exists(destPath))
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            destPath = Path.Combine(_shareDir, $"{nameNoExt} ({counter}){ext}");
            counter++;
        }

        if (copy)
            File.Copy(sourcePath, destPath);
        else
            File.Move(sourcePath, destPath);

        RefreshSharedFiles();
        StateChanged?.Invoke();
    }

    public static async Task AddFolderAsync(string sourceDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        var folderName = Path.GetFileName(sourceDir);
        var destDir = SafeSharePath.Resolve(_shareDir, folderName);

        var counter = 1;
        while (Directory.Exists(destDir))
        {
            destDir = Path.Combine(_shareDir, $"{folderName} ({counter})");
            counter++;
        }

        foreach (var dir in EnumerateSafe(sourceDir).Where(Directory.Exists))
        {
            var rel = Path.GetRelativePath(sourceDir, dir);
            SafeSharePath.RejectReparsePoints(dir);
            Directory.CreateDirectory(SafeSharePath.Resolve(_shareDir, Path.GetRelativePath(_shareDir, Path.Combine(destDir, rel))));
        }

        foreach (var file in EnumerateSafe(sourceDir).Where(File.Exists))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            SafeSharePath.RejectReparsePoints(file);
            SafeSharePath.Resolve(_shareDir, Path.GetRelativePath(_shareDir, destFile));
            File.Copy(file, destFile);
        }

        RefreshSharedFiles();
        StateChanged?.Invoke();
    }

    public static void RemoveFile(string relativePath)
    {
        var fullPath = SafeSharePath.Resolve(_shareDir, relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            RefreshSharedFiles();
            StateChanged?.Invoke();
        }
    }

    public static void RemoveFolder(string relativePath)
    {
        var fullPath = SafeSharePath.Resolve(_shareDir, relativePath);
        if (Directory.Exists(fullPath))
        {
            SafeSharePath.DeleteTree(fullPath);
            RefreshSharedFiles();
            StateChanged?.Invoke();
        }
    }

    public static void ClearAll()
    {
        if (Directory.Exists(_shareDir))
        {
            SafeSharePath.DeleteTree(_shareDir);
            Directory.CreateDirectory(_shareDir);
        }
        RefreshSharedFiles();
        StateChanged?.Invoke();
    }


    internal static bool IsAuthorizedToken(string? token) => !string.IsNullOrEmpty(_accessToken)
        && token is { Length: 64 } && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(token), Encoding.ASCII.GetBytes(_accessToken));

    internal static async Task HandleRequest(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "/";
        var method = ctx.Request.Method;
        var ct = ctx.RequestAborted;
        try
        {
            // Uploaded HTML/SVG must never execute with the sharing session's origin.
            ctx.Response.Headers["Content-Security-Policy"] = "sandbox allow-scripts allow-same-origin allow-forms allow-downloads allow-popups allow-modals; default-src 'self' blob:; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; media-src 'self' blob:; frame-ancestors 'none'; object-src 'none'";
            if (path is "/" or "/index.html" && method == "GET")
            { ctx.Response.ContentType = "text/html; charset=utf-8"; await ctx.Response.WriteAsync(FluentPageHtml.GetHtml(), ct); return; }
            // CSRF protection for cookie-authenticated operations; no CORS is enabled.
            var origin = ctx.Request.Headers.Origin.ToString();
            if (origin.Length != 0 && origin != $"https://{ctx.Request.Host}")
            { ctx.Response.StatusCode = 403; return; }
            if (path == "/api/session" && method == "POST")
            {
                var json = await RequestLimits.ReadJsonAsync(ctx.Request);
                if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("token", out var t)
                    || t.ValueKind != JsonValueKind.String || !IsAuthorizedToken(t.GetString()))
                { ctx.Response.StatusCode = 401; return; }
                ctx.Response.Cookies.Append(CookieName, t.GetString()!, new CookieOptions
                { HttpOnly = true, Secure = true, SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict, Path = "/" });
                await ctx.Response.WriteAsJsonAsync(new { ok = true }, ct); return;
            }
            if (!IsAuthorizedToken(ctx.Request.Cookies[CookieName]))
            { ctx.Response.StatusCode = 401; return; }
            if (path == "/api/info" && method == "GET")
            { await ctx.Response.WriteAsJsonAsync(new { ip = GetLocalIp(), port = Port, url = BuildAccessUrl() }, ct); return; }
            if (path == "/qr" && method == "GET")
            { ctx.Response.ContentType = "image/png"; await ctx.Response.Body.WriteAsync(QrCodeGenerator.GeneratePng(BuildAccessUrl(), 200), ct); return; }
            if (path == "/api/files" && method == "GET")
            {
                RefreshSharedFiles();
                var list = _sharedFiles.Values.Select(f => new { f.RelativePath, f.FileName, f.Size,
                    LastModified = f.LastModified.ToString("o"), f.ContentType, IsFolder = false }).ToList();
                foreach (var dir in EnumerateSafe(_shareDir).Where(Directory.Exists))
                {
                    var rel = Path.GetRelativePath(_shareDir, dir).Replace('\\', '/');
                    if (!list.Any(f => f.RelativePath.StartsWith(rel + "/", StringComparison.Ordinal)))
                        list.Add(new { RelativePath = rel, FileName = Path.GetFileName(dir), Size = 0L,
                            LastModified = Directory.GetLastWriteTimeUtc(dir).ToString("o"), ContentType = "", IsFolder = true });
                }
                // Keep the existing PascalCase browser protocol.
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(list), ct); return;
            }
            if (path.StartsWith("/download/", StringComparison.Ordinal) && method == "GET")
            {
                var file = SafeSharePath.Resolve(_shareDir, Uri.UnescapeDataString(path[10..]));
                if (!File.Exists(file)) { ctx.Response.StatusCode = 404; return; }
                ctx.Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
                ctx.Response.Headers.ContentDisposition = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(Path.GetFileName(file));
                ctx.Response.ContentType = GetContentType(Path.GetExtension(file));
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                ctx.Response.ContentLength = stream.Length; await stream.CopyToAsync(ctx.Response.Body, ct); return;
            }
            if (method is not ("POST" or "DELETE")) { ctx.Response.StatusCode = 405; return; }
            if (!await Mutations.WaitAsync(0, ct)) { ctx.Response.StatusCode = 429; return; }
            try
            {
                if (path == "/api/upload" && method == "POST") await Upload(ctx);
                else if (path == "/api/files" && method == "DELETE")
                {
                    var body = await RequestLimits.ReadJsonAsync(ctx.Request);
                    var full = SafeSharePath.Resolve(_shareDir, body.GetProperty("path").GetString() ?? "");
                    if (File.Exists(full)) File.Delete(full); else if (Directory.Exists(full)) SafeSharePath.DeleteTree(full);
                }
                else if (path == "/api/folder" && method == "POST")
                {
                    var body = await RequestLimits.ReadJsonAsync(ctx.Request);
                    Directory.CreateDirectory(SafeSharePath.Resolve(_shareDir, body.GetProperty("name").GetString() ?? ""));
                }
                else if (path == "/api/clear" && method == "DELETE") ClearAll();
                else { ctx.Response.StatusCode = 404; return; }
                RefreshSharedFiles(); StateChanged?.Invoke();
                await ctx.Response.WriteAsJsonAsync(new { ok = true }, ct);
            }
            finally { Mutations.Release(); }
        }
        catch (UnauthorizedAccessException) { ctx.Response.StatusCode = 403; }
        catch (BadHttpRequestException) { throw; }
        catch (Exception ex) when (ex is ArgumentException or JsonException or KeyNotFoundException or InvalidDataException)
        { ctx.Response.StatusCode = 400; }
        catch (IOException) when (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 409; }
    }

    private static async Task Upload(HttpContext ctx)
    {
        if (ctx.Request.ContentLength > MaxUploadBytes) throw new BadHttpRequestException("文件过大", 413);
        var used = EnumerateSafe(_shareDir).Where(File.Exists).Sum(f => new FileInfo(f).Length);
        var free = AvailableSpaceForTests?.Invoke() ?? new DriveInfo(Path.GetPathRoot(_shareDir)!).AvailableFreeSpace;
        // Keep at least 1 GiB free for Windows instead of filling a small system volume up to the quota.
        var remaining = Math.Min(MaxUploadBytes, Math.Max(0, Math.Min(MaxShareBytes - used, free - (1L << 30))));
        var media = MediaTypeHeaderValue.Parse(ctx.Request.ContentType ?? "application/octet-stream");
        if (media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = HeaderUtilities.RemoveQuotes(media.Boundary).Value;
            if (string.IsNullOrEmpty(boundary) || boundary.Length > 128) throw new BadHttpRequestException("上传格式错误", 400);
            var reader = new MultipartReader(boundary, ctx.Request.Body)
            { HeadersCountLimit = 16, HeadersLengthLimit = 8192, BodyLengthLimit = MaxUploadBytes };
            MultipartSection? section; int count = 0;
            while ((section = await reader.ReadNextSectionAsync(ctx.RequestAborted)) is not null)
            {
                if (++count > 1000) throw new BadHttpRequestException("单次上传文件过多", 413);
                var disposition = ContentDispositionHeaderValue.Parse(section.ContentDisposition);
                var name = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
                if (string.IsNullOrWhiteSpace(name)) throw new BadHttpRequestException("缺少文件名", 400);
                remaining -= await SaveUpload(section.Body, name, remaining, ctx.RequestAborted);
            }
        }
        else
        {
            var name = Uri.UnescapeDataString(ctx.Request.Headers["X-File-Name"].FirstOrDefault() ?? "upload");
            await SaveUpload(ctx.Request.Body, name, remaining, ctx.RequestAborted);
        }
    }

    private static async Task<long> SaveUpload(Stream body, string name, long limit, CancellationToken ct)
    {
        var destination = SafeSharePath.Resolve(_shareDir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = SafeSharePath.Resolve(_shareDir, ".upload-" + Guid.NewGuid().ToString("N"));
        try
        {
            long size;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                size = await RequestLimits.CopyBoundedAsync(body, output, limit, ct);
            SafeSharePath.Resolve(_shareDir, name);
            File.Move(temporary, destination, true);
            return size;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".json" => "application/json",
            ".xml" => "text/xml",
            ".js" => "text/javascript",
            ".doc" or ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" or ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" or ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".exe" => "application/octet-stream",
            ".msi" => "application/octet-stream",
            ".iso" => "application/x-iso9660-image",
            _ => "application/octet-stream"
        };
    }
}

public class SharedFileInfo
{
    public string RelativePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string ContentType { get; set; } = "";
}

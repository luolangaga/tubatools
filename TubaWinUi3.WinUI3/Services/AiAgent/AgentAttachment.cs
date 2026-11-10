namespace TubaWinUi3.Services.AiAgent;

/// <summary>Local references stay separate from prompt text; images use the component's file input.</summary>
public sealed record AgentAttachment(string Path, string Name, string Mime)
{
    public bool IsImage => Mime.StartsWith("image/", StringComparison.Ordinal);
    public string Uri => new System.Uri(System.IO.Path.GetFullPath(Path)).AbsoluteUri;
    internal static string ImageMime(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp",
        ".gif" => "image/gif", ".bmp" => "image/bmp", ".tif" or ".tiff" => "image/tiff", _ => ""
    };
    internal static async Task<object> PromptInputAsync(string text, IReadOnlyList<AgentAttachment>? attachments, CancellationToken ct)
    {
        var files = new List<object>();
        foreach (var attachment in (attachments ?? []).Where(a => a.IsImage))
        {
            // The component resolves data URIs; the source URI records the local origin for
            // history without relying on its URI resolver to open Windows/Unicode paths.
            if (new FileInfo(attachment.Path).Length > 20 * 1024 * 1024)
                throw new IOException(AgentText.L("ImageTooLarge", "单张图片不能超过 20 MB。"));
            var bytes = await File.ReadAllBytesAsync(attachment.Path, ct);
            files.Add(new { uri = "data:" + attachment.Mime + ";base64," + Convert.ToBase64String(bytes), name = attachment.Name, source = new { type = "uri", uri = attachment.Uri } });
        }
        return new { text, files, metadata = new { tubaAttachments = (attachments ?? []).Select(a => new { uri = a.Uri, name = a.Name, mime = a.Mime }).ToArray() } };
    }
}

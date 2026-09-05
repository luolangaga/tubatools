using System.Text;
using System.Text.Json.Nodes;
using HtmlAgilityPack;

namespace TubaWinUi3.Services;

/// <summary>文档转换的可调参数（OfficeCLI 原生参数 + 内置回退引擎共用）。</summary>
public sealed record DocConvertOptions(int ZipLevel = 6, bool MergeImages = false,
    int ImageMaxEdge = 1600, int JpgQuality = 90, string? PageRange = null, string? RenderMode = null)
{
    public static DocConvertOptions Default { get; } = new();
}

/// <summary>
/// 文档 / 文本 / PDF 转换统一路由：
/// OfficeCLI 渲染引擎优先（docx/xlsx/pptx 真实渲染 → 分页 HTML 派生各目标），未就绪或失败时回退
/// 内置轻量引擎（WebView2 + JS 库 + 纯 C# 解析器），
/// 旧版二进制格式（doc/ppt/wps/et/dps）走 Office / WPS COM 互联。
/// 必须在 UI 线程调用（WebView2 依赖），耗时阶段内部已异步化。
/// </summary>
public sealed class DocumentConvertService
{
    private readonly DocumentEngineService _engine;
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public DocumentConvertService(DocumentEngineService engine)
    {
        _engine = engine;
    }

    public async Task<List<string>> ConvertAsync(string source, SourceCategory category, FormatOption target,
        DocConvertOptions? options = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        options ??= DocConvertOptions.Default;
        var ext = Path.GetExtension(source).ToLowerInvariant();
        progress?.Report($"正在转换 {Path.GetFileName(source)}…");

        // OfficeCLI 真实渲染优先（仅 OOXML 源；.doc/.wps/.et 等老格式走 COM / 内置管线），失败自动回退
        if (ext is ".docx" or ".xlsx" or ".pptx"
            && FormatConvertCatalog.EngineFor(category, target) == ConvertEngine.OfficeCli)
        {
            if (OfficeCliService.IsReady)
            {
                try
                {
                    return await ConvertOfficeViaOfficeCliAsync(source, category, target, options, progress, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    progress?.Report($"OfficeCLI 渲染失败（{ex.Message}），回退内置引擎...");
                }
            }
        }

        return category switch
        {
            SourceCategory.Pdf => await ConvertPdfAsync(source, target, options, progress, ct),
            SourceCategory.Word => await ConvertWordAsync(source, ext, target, options, progress, ct),
            SourceCategory.Excel => await ConvertExcelAsync(source, ext, target, options, progress, ct),
            SourceCategory.Ppt => await ConvertPptAsync(source, ext, target, options, progress, ct),
            SourceCategory.Markdown => await ConvertMarkdownAsync(source, target, options, progress, ct),
            SourceCategory.Text => await ConvertTextAsync(source, target, options, progress, ct),
            SourceCategory.Html => await ConvertHtmlAsync(source, target, options, progress, ct),
            SourceCategory.Json => await ConvertJsonAsync(source, target, options, progress, ct),
            _ => throw new NotSupportedException($"暂不支持的类别：{category}")
        };
    }

    // ══════════════ OfficeCLI 渲染管线 ══════════════

    /// <summary>
    /// 纯 OfficeCLI 原生输出：txt/html/md 由 CLI 直出；pdf/png/jpg 用 CLI 原生逐页截图
    /// （无头浏览器渲染，像素不经任何重渲染），PDF 仅把页面图片原样嵌入容器，
    /// JPG/长图仅做像素格式封装/纵向拼接。不再经过 HTML 打印等任何中间转换链。
    /// </summary>
    private async Task<List<string>> ConvertOfficeViaOfficeCliAsync(string source, SourceCategory category,
        FormatOption target, DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report($"正在用 OfficeCLI 渲染 {Path.GetFileName(source)}…");

        if (target.Ext == ".txt")
        {
            var text = await Task.Run(() => OfficeCliService.TextAsync(source, ct), ct);
            var txtPath = UniqueOutput(source, ".txt");
            await File.WriteAllTextAsync(txtPath, text.TrimEnd() + "\n", Utf8Bom, ct);
            return [txtPath];
        }

        if (target.Ext is ".html" or ".md")
        {
            var tempHtml = Path.Combine(Path.GetTempPath(), $"officecli_{Guid.NewGuid():N}.html");
            try
            {
                await Task.Run(() => OfficeCliService.HtmlAsync(source, tempHtml, options.PageRange, progress, ct), ct);
                if (target.Ext == ".html")
                {
                    var htmlOut = UniqueOutput(source, ".html");
                    await File.WriteAllTextAsync(htmlOut,
                        SanitizeViewerHtml(await File.ReadAllTextAsync(tempHtml, ct)), Utf8Bom, ct);
                    return [htmlOut];
                }
                var md = HtmlConvert.ToMarkdown(await File.ReadAllTextAsync(tempHtml, ct));
                var mdPath = UniqueOutput(source, ".md");
                await File.WriteAllTextAsync(mdPath, md + "\n", Utf8Bom, ct);
                return [mdPath];
            }
            finally
            {
                try { File.Delete(tempHtml); } catch { }
            }
        }

        // pdf / png / jpg：原生逐页截图（干净像素直出）
        var tempDir = Path.Combine(Path.GetTempPath(), "officecli_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var pagePngs = await RenderPagesViaOfficeCliAsync(source, category, options, progress, tempDir, ct);
            var dir = Path.GetDirectoryName(source) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(source) + "_converted";

            // 合并长图：纵向拼接（像素原样叠加）
            if (options.MergeImages && pagePngs.Count > 1)
            {
                progress?.Report($"正在拼接 {pagePngs.Count} 页长图...");
                var merged = MergeImagesVertically(pagePngs, tempDir);
                var final = target.Ext == ".jpg" ? ConvertToJpg(merged, options.JpgQuality, tempDir) : merged;
                var mergedOut = UniquePath(Path.Combine(dir, baseName + (target.Ext == ".jpg" ? ".jpg" : ".png")));
                File.Copy(final, mergedOut, true);
                return [mergedOut];
            }

            // PDF：页面图片原样嵌入容器（officecli 官方 pdf 导出需其 exporter 插件，当前未发布）
            if (target.Ext == ".pdf")
            {
                progress?.Report($"正在合成 PDF（{pagePngs.Count} 页，像素原样嵌入）...");
                var tempPdf = Path.Combine(tempDir, "out.pdf");
                await _engine.ImagesToPdfAsync(pagePngs, tempPdf, progress, ct);
                var pdfOut = UniqueOutput(source, ".pdf");
                File.Copy(tempPdf, pdfOut, true);
                return [pdfOut];
            }

            // png / jpg：逐页输出
            var outputs = new List<string>();
            for (int i = 0; i < pagePngs.Count; i++)
            {
                progress?.Report($"正在输出第 {i + 1}/{pagePngs.Count} 页...");
                var final = target.Ext == ".jpg"
                    ? ConvertToJpg(pagePngs[i], options.JpgQuality, tempDir)
                    : pagePngs[i];
                var pagePath = FormatConvertPlanner.BuildDocPagePath(dir, baseName, target.Ext, i + 1, pagePngs.Count);
                var outPath = UniquePath(pagePath);
                File.Copy(final, outPath, true);
                outputs.Add(outPath);
            }
            return outputs;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// 逐页原生截图。指定了页码范围则按范围渲染；否则从第 1 页起渲染，
    /// 超出末页时 CLI 会钳位回已输出过的页（实测钳回第 1 页），按"页内容哈希已出现过"判定结束。
    /// </summary>
    private async Task<List<string>> RenderPagesViaOfficeCliAsync(string source, SourceCategory category,
        DocConvertOptions options, IProgress<string>? progress, string tempDir, CancellationToken ct)
    {
        // --render 仅对 docx/pptx 有意义（native = 本机 Word/PowerPoint 渲染）
        var renderMode = category is SourceCategory.Word or SourceCategory.Ppt ? options.RenderMode : null;

        var pages = new List<string>();
        var seenHashes = new HashSet<string>(StringComparer.Ordinal);

        var requested = ParsePageRange(options.PageRange);
        foreach (var p in requested)
        {
            ct.ThrowIfCancellationRequested();
            var png = Path.Combine(tempDir, $"page_{p:D4}.png");
            await Task.Run(() => OfficeCliService.ScreenshotAsync(source, p.ToString(), options.ImageMaxEdge,
                renderMode, png, ct), ct);
            pages.Add(png);
            progress?.Report($"OfficeCLI 已渲染第 {p} 页...");
        }
        if (requested.Count > 0) return pages;

        for (int p = 1; p <= 999; p++)
        {
            ct.ThrowIfCancellationRequested();
            var png = Path.Combine(tempDir, $"page_{p:D4}.png");
            await Task.Run(() => OfficeCliService.ScreenshotAsync(source, p.ToString(), options.ImageMaxEdge,
                renderMode, png, ct), ct);
            var hash = Convert.ToHexString(
                System.Security.Cryptography.MD5.HashData(await File.ReadAllBytesAsync(png, ct)));
            if (!seenHashes.Add(hash))
            {
                try { File.Delete(png); } catch { }
                break; // 钳位重复输出 = 已到末页
            }
            pages.Add(png);
            progress?.Report($"OfficeCLI 已渲染第 {p} 页...");
        }
        if (pages.Count == 0)
            throw new InvalidOperationException("OfficeCLI 未渲染出任何页面");
        return pages;
    }

    /// <summary>
    /// 清理 officecli html 观察器的两个浏览器端渲染问题（内容与样式不动，仅禁用其运行时布局脚本）：
    /// ① 摘除运行时分页脚本——对含不可拆分巨型表格行的文档，该脚本在普通浏览器里会陷入克隆循环
    ///    （实测同一份文档被克隆出 341 页 = 重影），移除后为单长页渲染；
    /// ② 把"衬于文字下方"锚定图（position:absolute + z-index:-1 + 100%×100% 背景）转回正常内联图片——
    ///    这类全页扫描图在单长页模式下会失去定位参照、整张漂移覆盖到其他内容上（= 大字"水印"）。
    /// </summary>
    private static string SanitizeViewerHtml(string html)
    {
        var result = html;

        // ① 摘除分页脚本（</body> 前最后一个含 _wordPaginate 的 <script> 块）
        var idx = result.IndexOf("_wordPaginate", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var start = result.LastIndexOf("<script", idx, StringComparison.OrdinalIgnoreCase);
            var end = result.IndexOf("</script>", idx, StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end >= 0)
                result = result[..start] + result[(end + "</script>".Length)..];
        }

        // ② 背景锚定图层 → 正常内联图片（随容器宽度缩放，不再绝对定位漂移）
        const string style = "<style>" +
            ".page div[style*=\"z-index:-1\"]{position:static!important;width:100%!important;height:auto!important;overflow:visible!important}" +
            ".page div[style*=\"z-index:-1\"] img{position:static!important;width:100%!important;height:auto!important;object-fit:initial!important}" +
            "</style>";
        var headClose = result.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        result = headClose >= 0 ? result[..headClose] + style + result[headClose..] : result + style;

        return result;
    }

    /// <summary>解析页码范围（如 "1-3,5"）为页号列表；空 = 全部页。</summary>
    private static List<int> ParsePageRange(string? range)
    {
        var pages = new List<int>();
        if (string.IsNullOrWhiteSpace(range)) return pages;
        foreach (var part in range.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-');
            if (dash > 0 && int.TryParse(part[..dash], out var a) && int.TryParse(part[(dash + 1)..], out var b))
            {
                for (int i = Math.Max(1, a); i <= b; i++) pages.Add(i);
            }
            else if (int.TryParse(part, out var n) && n >= 1)
            {
                pages.Add(n);
            }
        }
        return pages.Distinct().OrderBy(x => x).ToList();
    }

    /// <summary>多张 PNG 纵向拼接为一张长图（System.Drawing，像素原样叠加，白底补齐）。</summary>
    private static string MergeImagesVertically(IReadOnlyList<string> pngPaths, string tempDir)
    {
        var outPath = Path.Combine(tempDir, "merged.png");
        var frames = new List<System.Drawing.Bitmap>();
        try
        {
            foreach (var path in pngPaths)
                frames.Add(new System.Drawing.Bitmap(path));
            var width = frames.Max(b => b.Width);
            var totalHeight = frames.Sum(b => b.Height);

            using var canvas = new System.Drawing.Bitmap(width, totalHeight);
            using (var g = System.Drawing.Graphics.FromImage(canvas))
            {
                g.Clear(System.Drawing.Color.White);
                int y = 0;
                foreach (var frame in frames)
                {
                    g.DrawImage(frame, (width - frame.Width) / 2, y, frame.Width, frame.Height);
                    y += frame.Height;
                }
            }
            canvas.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        }
        finally
        {
            foreach (var frame in frames) frame.Dispose();
        }
        return outPath;
    }

    /// <summary>PNG → JPG（System.Drawing JPEG 编码器，质量可调，不做其他处理）。</summary>
    private static string ConvertToJpg(string pngPath, int quality, string tempDir)
    {
        var jpgPath = Path.Combine(tempDir,
            Path.GetFileNameWithoutExtension(pngPath) + ".jpg");
        using var src = new System.Drawing.Bitmap(pngPath);
        var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
        using var ep = new System.Drawing.Imaging.EncoderParameters(1);
        ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(quality, 50, 100));
        src.Save(jpgPath, codec, ep);
        return jpgPath;
    }

    // ══════════════ PDF ══════════════

    private async Task<List<string>> ConvertPdfAsync(string source, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(source) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(source) + "_converted";

        // 扫描版 OCR：逐页渲染图片 → 识别文字
        if (target.Special == ConvertSpecial.OcrText)
        {
            progress?.Report("正在渲染 PDF 页面用于识别...");
            var tempDir = Path.Combine(Path.GetTempPath(), "pdfocr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var pages = await _engine.PdfToImagesAsync(source, tempDir, "page", "png", 90, progress, ct);
                var sb = new StringBuilder();
                for (int i = 0; i < pages.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report($"正在 OCR 识别第 {i + 1}/{pages.Count} 页...");
                    if (pages.Count > 1)
                        sb.Append($"—— 第 {i + 1} 页 ——\n");
                    sb.AppendLine(await OcrService.RecognizeImageFileAsync(pages[i], ct));
                    if (i < pages.Count - 1) sb.AppendLine();
                }
                var outPath = UniqueOutput(source, ".txt");
                await File.WriteAllTextAsync(outPath, sb.ToString().TrimEnd() + "\n", Utf8Bom, ct);
                return [outPath];
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        // 页面导出为图片压缩包
        if (target.Special == ConvertSpecial.ZipArchive)
        {
            progress?.Report("正在渲染 PDF 页面...");
            var fmt = target.Tag == "jpg" ? "jpg" : "png";
            var images = await _engine.PdfToImagesAsync(source, dir, baseName, fmt, options.JpgQuality, progress, ct,
                options.MergeImages, options.ImageMaxEdge);
            var zipPath = UniqueOutput(source, ".zip");
            var (before, after) = FormatConvertPlanner.CreateZipArchive(images, zipPath, options.ZipLevel);
            progress?.Report($"压缩包已生成（{DownloadQueueService.FormatSize(before)} → {DownloadQueueService.FormatSize(after)}）");
            return [zipPath];
        }

        // 文字型 PDF：文本 / 网页 / Excel 表格提取
        if (target.Ext is ".txt" or ".html" or ".xlsx")
        {
            progress?.Report("正在提取 PDF 文字层...");
            var pages = await _engine.PdfExtractAsync(source, ct);

            if (target.Special == ConvertSpecial.PdfExcel)
            {
                var totalRows = pages.Sum(p => p.Rows.Count);
                if (totalRows == 0)
                    throw new InvalidOperationException(
                        "未检测到文字层（这是扫描版/图片型 PDF）：请改用「OCR 文本」目标进行识别");
                progress?.Report($"正在生成 Excel（{totalRows} 行）...");
                var sheets = pages.Select(p => ($"第{p.Page}页", p.Rows.Select(r => r.ToArray()).ToArray())).ToList();
                var outPath = UniqueOutput(source, ".xlsx");
                await _engine.AoaToXlsxAsync(sheets, outPath, ct);
                return [outPath];
            }

            if (target.Ext == ".txt")
            {
                var sb = new StringBuilder();
                foreach (var page in pages)
                {
                    if (pages.Count > 1)
                        sb.Append($"—— 第 {page.Page} 页 ——\n");
                    sb.AppendLine(page.Text.TrimEnd());
                    sb.AppendLine();
                }
                var outPath = UniqueOutput(source, ".txt");
                await File.WriteAllTextAsync(outPath, sb.ToString().TrimEnd() + "\n", Utf8Bom, ct);
                return [outPath];
            }

            // HTML
            var htmlSb = new StringBuilder();
            foreach (var page in pages)
            {
                if (pages.Count > 1)
                    htmlSb.Append($"<h2>第 {page.Page} 页</h2>");
                var tableish = page.Rows.Any(r => r.Length > 1);
                if (tableish)
                {
                    htmlSb.Append("<table>");
                    foreach (var row in page.Rows)
                    {
                        htmlSb.Append("<tr>");
                        foreach (var cell in row)
                            htmlSb.Append("<td>" + HtmlConvert.Escape(cell) + "</td>");
                        htmlSb.Append("</tr>");
                    }
                    htmlSb.Append("</table>");
                }
                else
                {
                    foreach (var line in page.Text.Split('\n'))
                        if (line.Trim().Length > 0)
                            htmlSb.Append("<p>" + HtmlConvert.Escape(line.Trim()) + "</p>");
                }
            }
            var htmlOut = UniqueOutput(source, ".html");
            await File.WriteAllTextAsync(htmlOut,
                TabularConvert.BuildHtmlDocument(htmlSb.ToString(), Path.GetFileNameWithoutExtension(source)), Utf8Bom, ct);
            return [htmlOut];
        }

        // PNG / JPG 散图（可合并为一张长图）
        progress?.Report(options.MergeImages ? "正在渲染合并长图..." : "正在渲染 PDF 页面...");
        var imageFmt = target.Ext == ".jpg" ? "jpg" : "png";
        return await _engine.PdfToImagesAsync(source, dir, baseName, imageFmt, options.JpgQuality, progress, ct,
            options.MergeImages, options.ImageMaxEdge);
    }

    // ══════════════ Word（doc / docx / wps / rtf / odt） ══════════════

    private async Task<List<string>> ConvertWordAsync(string source, string ext, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        switch (ext)
        {
            case ".doc" or ".wps":
                return await ConvertViaOfficeAsync(source, target, options, progress, ct);

            case ".rtf":
            {
                var text = RtfTextExtractor.Extract(await File.ReadAllTextAsync(source, ct));
                if (text.Trim().Length == 0)
                    throw new InvalidOperationException("RTF 中没有可提取的文字");
                return await FromTextAsync(source, text, target, options, progress, ct);
            }

            case ".odt":
            {
                var html = OdfConverter.ToHtml(source);
                return await FromHtmlAsync(source, html, target, options, progress, ct);
            }

            case ".docx":
            default:
            {
                if (target.Ext == ".txt")
                {
                    var text = DocxReader.ToPlainText(source);
                    var outPath = UniqueOutput(source, ".txt");
                    await File.WriteAllTextAsync(outPath, text + "\n", Utf8Bom, ct);
                    return [outPath];
                }
                if (target.Ext == ".md")
                {
                    var md = DocxReader.ToMarkdown(source);
                    var outPath = UniqueOutput(source, ".md");
                    await File.WriteAllTextAsync(outPath, md + "\n", Utf8Bom, ct);
                    return [outPath];
                }
                var html = await _engine.DocxToHtmlAsync(source);
                return await FromHtmlAsync(source, CleanDocxHtml(html), target, options, progress, ct);
            }
        }
    }

    // ══════════════ Excel（xls / xlsx / et / ods / csv） ══════════════

    private async Task<List<string>> ConvertExcelAsync(string source, string ext, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        // .et 由 WPS/Office 兜底
        if (ext == ".et" && OfficeInteropService.IsExcelAvailable)
            return await ConvertViaOfficeAsync(source, target, options, progress, ct);

        try
        {
            return await ConvertWorkbookAsync(source, target, options, progress, ct);
        }
        catch (Exception ex) when (ext == ".et" && ex is not OperationCanceledException
                                   && target.Ext is ".pdf" or ".xlsx" or ".csv" or ".html"
                                   && OfficeInteropService.IsExcelAvailable)
        {
            progress?.Report("内置引擎无法解析该 .et 文件，正在尝试通过 WPS / Excel 转换...");
            return await ConvertViaOfficeAsync(source, target, options, progress, ct);
        }
    }

    private async Task<List<string>> ConvertWorkbookAsync(string source, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        // PDF / 图片：SheetJS → HTML → 打印 → （图片：pdf.js 渲染）
        if (target.Ext is ".pdf" or ".png" or ".jpg")
        {
            var html = await _engine.XlsxToHtmlAsync(source);
            return await FromHtmlAsync(source, html, target, options, progress, ct);
        }

        if (target.Ext == ".html")
        {
            var html = await _engine.XlsxToHtmlAsync(source);
            var outPath = UniqueOutput(source, ".html");
            await File.WriteAllTextAsync(outPath,
                TabularConvert.BuildHtmlDocument(html, Path.GetFileNameWithoutExtension(source)), Utf8Bom, ct);
            return [outPath];
        }

        if (target.Ext == ".xlsx")
        {
            var outPath = UniqueOutput(source, ".xlsx");
            progress?.Report("正在生成 XLSX...");
            await _engine.WorkbookToXlsxAsync(source, outPath, ct);
            return [outPath];
        }

        if (target.Ext is ".csv" or ".json" or ".md")
        {
            var format = target.Ext == ".json" ? "json" : "csv";
            progress?.Report("正在解析工作簿...");
            var sheets = await _engine.WorkbookOutAsync(source, format, ct);
            var baseName = Path.GetFileNameWithoutExtension(source);

            if (target.Ext == ".json")
            {
                string json;
                if (sheets.Count == 1)
                {
                    json = sheets[0].Text;
                }
                else
                {
                    var obj = new JsonObject();
                    foreach (var (name, text) in sheets)
                        obj[name] = JsonNode.Parse(text) ?? new JsonArray();
                    json = obj.ToJsonString(new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                }
                var jsonOut = UniqueOutput(source, ".json");
                await File.WriteAllTextAsync(jsonOut, json + "\n", Utf8Bom, ct);
                return [jsonOut];
            }

            if (target.Ext == ".csv")
            {
                var outputs = new List<string>();
                foreach (var (name, text) in sheets)
                {
                    var outPath = sheets.Count == 1
                        ? UniqueOutput(source, ".csv")
                        : UniquePath(Path.Combine(
                            Path.GetDirectoryName(source) ?? ".",
                            $"{baseName}_{SanitizeFileName(name)}.csv"));
                    await File.WriteAllTextAsync(outPath, text, Utf8Bom, ct);
                    outputs.Add(outPath);
                }
                return outputs;
            }

            // Markdown：多工作表合并为一个文档
            var mdSb = new StringBuilder();
            foreach (var (name, text) in sheets)
            {
                if (sheets.Count > 1) mdSb.Append($"## {name}\n\n");
                mdSb.Append(TabularConvert.RowsToMarkdown(TabularConvert.ParseCsv(text)));
                mdSb.Append('\n');
            }
            var mdOut = UniqueOutput(source, ".md");
            await File.WriteAllTextAsync(mdOut, mdSb.ToString(), Utf8Bom, ct);
            return [mdOut];
        }

        throw new NotSupportedException($"表格暂不支持的目标格式：{target.Ext}");
    }

    // ══════════════ PPT（ppt / pptx / dps / odp） ══════════════

    private async Task<List<string>> ConvertPptAsync(string source, string ext, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        switch (ext)
        {
            case ".ppt" or ".dps":
                return await ConvertViaOfficeAsync(source, target, options, progress, ct);

            case ".odp":
            {
                if (target.Ext == ".pptx")
                    return await ConvertViaOfficeAsync(source, target, options, progress, ct);
                var html = OdfConverter.ToHtml(source);
                return await FromHtmlAsync(source, html, target, options, progress, ct);
            }

            case ".pptx":
            default:
            {
                var html = PptxToHtmlConverter.ToHtml(source);
                return await FromHtmlAsync(source, html, target, options, progress, ct);
            }
        }
    }

    // ══════════════ Markdown / TXT / HTML / JSON ══════════════

    private async Task<List<string>> ConvertMarkdownAsync(string source, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        var md = TabularConvert.ReadTextSmart(source);
        var html = await _engine.MarkdownToHtmlAsync(md);
        return await FromHtmlAsync(source, html, target, options, progress, ct);
    }

    private async Task<List<string>> ConvertTextAsync(string source, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        var text = TabularConvert.ReadTextSmart(source);
        if (target.Ext == ".md")
        {
            var outPath = UniqueOutput(source, ".md");
            await File.WriteAllTextAsync(outPath, text, Utf8Bom, ct);
            return [outPath];
        }
        return await FromTextAsync(source, text, target, options, progress, ct);
    }

    private async Task<List<string>> ConvertHtmlAsync(string source, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        var html = TabularConvert.ReadTextSmart(source);
        return await FromHtmlAsync(source, html, target, options, progress, ct);
    }

    private async Task<List<string>> ConvertJsonAsync(string source, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        var json = TabularConvert.ReadTextSmart(source);
        try
        {
            _ = JsonNode.Parse(json) ?? throw new InvalidOperationException("JSON 内容为空");
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"不是有效的 JSON 文件：{ex.Message}");
        }

        switch (target.Ext)
        {
            case ".txt":
            {
                var outPath = UniqueOutput(source, ".txt");
                await File.WriteAllTextAsync(outPath, TabularConvert.PrettyJson(json) + "\n", Utf8Bom, ct);
                return [outPath];
            }
            case ".md":
            {
                var outPath = UniqueOutput(source, ".md");
                await File.WriteAllTextAsync(outPath, TabularConvert.JsonToMarkdown(json) + "\n", Utf8Bom, ct);
                return [outPath];
            }
            case ".csv":
            {
                var outPath = UniqueOutput(source, ".csv");
                await File.WriteAllTextAsync(outPath, TabularConvert.JsonToCsv(json), Utf8Bom, ct);
                return [outPath];
            }
            default:
                return await FromHtmlAsync(source, TabularConvert.JsonToHtml(json), target, options, progress, ct);
        }
    }

    // ══════════════ 通用管线 ══════════════

    /// <summary>已有 HTML → 目标（pdf/html/docx/txt/md/png/jpg）。</summary>
    private async Task<List<string>> FromHtmlAsync(string source, string html, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        switch (target.Ext)
        {
            case ".html":
            {
                var outPath = UniqueOutput(source, ".html");
                await File.WriteAllTextAsync(outPath,
                    EnsureHtmlDocument(html, Path.GetFileNameWithoutExtension(source)), Utf8Bom, ct);
                return [outPath];
            }
            case ".txt":
            {
                var outPath = UniqueOutput(source, ".txt");
                await File.WriteAllTextAsync(outPath, HtmlConvert.ToPlainText(html) + "\n", Utf8Bom, ct);
                return [outPath];
            }
            case ".md":
            {
                var outPath = UniqueOutput(source, ".md");
                await File.WriteAllTextAsync(outPath, HtmlConvert.ToMarkdown(html) + "\n", Utf8Bom, ct);
                return [outPath];
            }
            case ".docx":
            {
                var outPath = UniqueOutput(source, ".docx");
                progress?.Report("正在生成 Word 文档...");
                await File.WriteAllBytesAsync(outPath,
                    DocxWriter.FromHtml(html, Path.GetFileNameWithoutExtension(source)), ct);
                return [outPath];
            }
            default:
            {
                // .pdf / .png / .jpg：HTML → PDF（→ 图片）
                progress?.Report("正在排版文档...");
                var pdfPath = Path.Combine(Path.GetTempPath(), $"doceng_{Guid.NewGuid():N}.pdf");
                try
                {
                    await _engine.RenderHtmlToPdfAsync(
                        EnsureHtmlDocument(html, Path.GetFileNameWithoutExtension(source)), pdfPath);
                    if (target.Ext == ".pdf")
                    {
                        var outPath = UniqueOutput(source, ".pdf");
                        File.Copy(pdfPath, outPath, true);
                        progress?.Report("PDF 已生成");
                        return [outPath];
                    }
                    progress?.Report("正在渲染页面为图片...");
                    var dir = Path.GetDirectoryName(source) ?? ".";
                    var baseName = Path.GetFileNameWithoutExtension(source) + "_converted";
                    var fmt = target.Ext == ".jpg" ? "jpg" : "png";
                    return await _engine.PdfToImagesAsync(pdfPath, dir, baseName, fmt, options.JpgQuality, progress, ct, options.MergeImages, options.ImageMaxEdge);
                }
                finally
                {
                    try { File.Delete(pdfPath); } catch { }
                }
            }
        }
    }

    /// <summary>已有纯文本 → 目标。</summary>
    private async Task<List<string>> FromTextAsync(string source, string text, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        if (target.Ext == ".docx")
        {
            var outPath = UniqueOutput(source, ".docx");
            progress?.Report("正在生成 Word 文档...");
            await File.WriteAllBytesAsync(outPath,
                DocxWriter.FromText(text, Path.GetFileNameWithoutExtension(source)), ct);
            return [outPath];
        }
        if (target.Ext == ".md")
        {
            var outPath = UniqueOutput(source, ".md");
            await File.WriteAllTextAsync(outPath, text, Utf8Bom, ct);
            return [outPath];
        }
        return await FromHtmlAsync(source, TabularConvert.TextToHtml(text,
            Path.GetFileNameWithoutExtension(source)), target, options, progress, ct);
    }

    /// <summary>旧版二进制格式 → Office / WPS COM（直接支持的目标或经临时文件中转）。</summary>
    private async Task<List<string>> ConvertViaOfficeAsync(string source, FormatOption target,
        DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        if (!OfficeInteropService.IsAvailableFor(source))
        {
            throw new NotSupportedException(
                $"内置轻量引擎不支持 {Path.GetExtension(source)} 旧版二进制格式，且本机未安装可用的 Microsoft Office / WPS Office。" +
                "请安装 Office/WPS，或先用它们把文件另存为 .docx / .pptx / .xlsx 后再转换。");
        }

        var ext = Path.GetExtension(source).ToLowerInvariant();

        // Office 直接支持的目标
        if (target.Ext is ".docx" or ".pdf" or ".pptx")
        {
            var outPath = UniqueOutput(source, target.Ext);
            return await OfficeInteropService.ConvertAsync(source, target.Ext, outPath, progress, ct);
        }

        // 其余目标先经中转文件：Word 家族转 docx/pdf，Excel/PPT 家族一律先转 PDF（.json 经 CSV）
        string tempExt = target.Ext switch
        {
            ".txt" or ".md" when ext is ".doc" or ".wps" => ".docx",
            ".json" when ext is ".xls" or ".et" => ".csv",
            _ => ".pdf"
        };
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"office_{Guid.NewGuid():N}{tempExt}");
        try
        {
            progress?.Report($"正在通过 Office / WPS 转换为{(tempExt == ".pdf" ? " PDF" : tempExt.ToUpperInvariant())}中转文件...");
            await OfficeInteropService.ConvertAsync(source, tempExt, tempPath, progress, ct);

            if (target.Ext is ".txt" or ".md")
            {
                string text;
                if (tempExt == ".docx")
                    text = target.Ext == ".md" ? DocxReader.ToMarkdown(tempPath) : DocxReader.ToPlainText(tempPath);
                else
                    text = await PdfTextMarkdown(tempPath, ct);
                var outPath = UniqueOutput(source, target.Ext);
                await File.WriteAllTextAsync(outPath, text + "\n", Utf8Bom, ct);
                return [outPath];
            }

            if (target.Ext == ".json" && tempExt == ".csv")
            {
                var json = TabularConvert.CsvToJson(await File.ReadAllTextAsync(tempPath, ct));
                var outPath = UniqueOutput(source, ".json");
                await File.WriteAllTextAsync(outPath, json + "\n", Utf8Bom, ct);
                return [outPath];
            }

            // .html / 图片：从中转 PDF 提取
            return await ConvertPdfDerivedAsync(source, tempPath, target, options, progress, ct);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>以 tempPdf 为源派生 html / png / jpg 输出。</summary>
    private async Task<List<string>> ConvertPdfDerivedAsync(string source, string tempPdf,
        FormatOption target, DocConvertOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(source) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(source) + "_converted";

        if (target.Ext == ".html")
        {
            progress?.Report("正在提取文字生成网页...");
            var pages = await _engine.PdfExtractAsync(tempPdf, ct);
            var sb = new StringBuilder();
            foreach (var page in pages)
            {
                if (pages.Count > 1) sb.Append($"<h2>第 {page.Page} 页</h2>");
                foreach (var line in page.Text.Split('\n'))
                    if (line.Trim().Length > 0)
                        sb.Append("<p>" + HtmlConvert.Escape(line.Trim()) + "</p>");
            }
            var outPath = UniqueOutput(source, ".html");
            await File.WriteAllTextAsync(outPath,
                TabularConvert.BuildHtmlDocument(sb.ToString(), Path.GetFileNameWithoutExtension(source)), Utf8Bom, ct);
            return [outPath];
        }

        progress?.Report("正在渲染页面为图片...");
        var fmt = target.Ext == ".jpg" ? "jpg" : "png";
        return await _engine.PdfToImagesAsync(tempPdf, dir, baseName, fmt, options.JpgQuality, progress, ct, options.MergeImages, options.ImageMaxEdge);
    }

    /// <summary>PDF → Markdown（文字层，逐页）。</summary>
    private async Task<string> PdfTextMarkdown(string pdfPath, CancellationToken ct)
    {
        var pages = await _engine.PdfExtractAsync(pdfPath, ct);
        var sb = new StringBuilder();
        foreach (var page in pages)
        {
            if (pages.Count > 1) sb.Append($"## 第 {page.Page} 页\n\n");
            var rows = page.Rows;
            if (rows.Any(r => r.Length > 1))
                sb.Append(TabularConvert.RowsToMarkdown(rows) + "\n");
            else
                sb.Append(page.Text.TrimEnd() + "\n\n");
        }
        return sb.ToString().TrimEnd();
    }

    // ══════════════ 工具 ══════════════

    /// <summary>生成不冲突的输出路径（支持传入完整目标路径或源文件）。</summary>
    private static string UniqueOutput(string source, string targetExt)
        => FormatConvertPlanner.BuildOutputPath(source, targetExt);

    /// <summary>为指定完整路径消歧（已存在非空文件时追加 _1.._999）。</summary>
    private static string UniquePath(string path)
    {
        static bool Occupied(string p) => File.Exists(p) && new FileInfo(p).Length > 0;
        if (!Occupied(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!Occupied(candidate)) return candidate;
        }
        return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    /// <summary>docx-preview 渲染的 HTML 中图片是 blob: URL（保存后失效），替换为占位文本。</summary>
    private static string CleanDocxHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        foreach (var img in doc.DocumentNode.SelectNodes("//img[starts-with(@src,'blob:')]")
                 ?? Enumerable.Empty<HtmlNode>())
        {
            var placeholder = doc.CreateTextNode("[图片]");
            img.ParentNode?.ReplaceChild(placeholder, img);
        }
        return doc.DocumentNode.OuterHtml;
    }

    /// <summary>确保是带 charset 的完整 HTML 文档（供打印与保存）。</summary>
    private static string EnsureHtmlDocument(string html, string title)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        if (doc.DocumentNode.SelectSingleNode("//html") is null)
            return TabularConvert.BuildHtmlDocument(html, title);
        // 已是完整文档：确保 head 里有 charset
        if (doc.DocumentNode.SelectSingleNode("//meta[@charset]") is null)
        {
            var head = doc.DocumentNode.SelectSingleNode("//head");
            if (head is null)
            {
                head = HtmlNode.CreateNode("<head></head>");
                doc.DocumentNode.PrependChild(head!);
            }
            head.PrependChild(HtmlNode.CreateNode("<meta charset=\"utf-8\">")!);
        }
        return doc.DocumentNode.OuterHtml;
    }
}

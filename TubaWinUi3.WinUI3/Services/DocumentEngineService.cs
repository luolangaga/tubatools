using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace TubaWinUi3.Services;

/// <summary>
/// 文档引擎（纯 WebView2 + 内置浏览器版 JS 库，运行时零下载）：
/// markdown/docx/xlsx/pptx → HTML → PrintToPdf；pdf → 每页图片（pdf.js）。
/// 宿主页 Assets/DocEngine/doceng.html 通过虚拟主机 doceng 映射加载。
///
/// 重要：WebView2 的 ExecuteScriptAsync 不等待 JavaScript Promise（Promise 会被
/// 序列化成空对象 {}），因此所有异步任务采用「同步启动 + 轮询 getJob」模式
/// （见 RunJobAsync）；仅同步函数直接取值。
/// 注意：所有方法必须在 UI 线程调用（WebView2 要求）。
/// </summary>
public sealed class DocumentEngineService
{
    private readonly WebView2 _webView;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public DocumentEngineService(WebView2 webView)
    {
        _webView = webView;
    }

    public static string HostFolder
        => Path.Combine(AppContext.BaseDirectory, "Assets", "DocEngine");

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "…";

    private async Task EnsureReadyAsync()
    {
        if (_initialized) return;
        await _gate.WaitAsync();
        try
        {
            if (_initialized) return;
            if (_webView.CoreWebView2 is null)
                await _webView.EnsureCoreWebView2Async();

            _webView.CoreWebView2!.SetVirtualHostNameToFolderMapping(
                "doceng", HostFolder, CoreWebView2HostResourceAccessKind.Allow);

            var tcs = new TaskCompletionSource<bool>();
            void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(e.IsSuccess);
            _webView.CoreWebView2.NavigationCompleted += OnNav;
            try
            {
                _webView.CoreWebView2.Navigate("https://doceng/doceng.html");
                var ok = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(20));
                if (!ok)
                    throw new InvalidOperationException("文档引擎宿主页加载失败");
            }
            finally
            {
                _webView.CoreWebView2.NavigationCompleted -= OnNav;
            }
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>执行一段返回字符串的同步 JS，返回反序列化后的值。</summary>
    private async Task<string> EvalStringAsync(string js)
    {
        await EnsureReadyAsync();
        var raw = await _webView.CoreWebView2.ExecuteScriptAsync(js);
        if (string.IsNullOrEmpty(raw)) return "";
        try { return JsonSerializer.Deserialize<string>(raw) ?? ""; }
        catch { return raw; }
    }

    /// <summary>
    /// 启动异步 JS 任务并轮询 getJob(key) 直到完成。
    /// 返回 done 时 data 字段的原始 JSON 文本（调用方按需反序列化）。
    /// </summary>
    private async Task<string> RunJobAsync(string jobKey, string startJs, CancellationToken ct = default)
    {
        await EnsureReadyAsync();
        await _webView.CoreWebView2.ExecuteScriptAsync(startJs);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(250, ct);

            var raw = await _webView.CoreWebView2.ExecuteScriptAsync(
                $"docengine.getJob({JsonSerializer.Serialize(jobKey)})");

            var state = "pending";
            var dataJson = "";
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var el = doc.RootElement;
                if (el.ValueKind == JsonValueKind.String)
                    el = JsonDocument.Parse(el.GetString()!).RootElement;
                if (el.ValueKind != JsonValueKind.Object) continue;
                state = el.TryGetProperty("state", out var st) ? st.GetString() ?? "pending" : "pending";
                if (state == "done")
                    dataJson = el.TryGetProperty("data", out var d) ? d.GetRawText() : "";
                else if (state == "error")
                    dataJson = el.TryGetProperty("message", out var m) ? m.GetString() ?? "未知错误" : "未知错误";
            }
            catch
            {
                continue; // 解析异常视为仍在执行（宿主页可能正在过渡）
            }

            if (state == "pending") continue;
            if (state == "error")
                throw new InvalidOperationException("文档引擎错误：" + dataJson);
            return dataJson;
        }
    }

    // ── 各文档类型 → HTML ──

    public Task<string> MarkdownToHtmlAsync(string markdown)
        => EvalStringAsync($"docengine.markdownToHtml({JsonSerializer.Serialize(markdown)})");

    public async Task<string> DocxToHtmlAsync(string filePath)
    {
        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(filePath));
        var data = await RunJobAsync("docx", $"docengine.startDocxToHtml({JsonSerializer.Serialize(b64)})");
        return JsonSerializer.Deserialize<string>(data) ?? "";
    }

    public async Task<string> XlsxToHtmlAsync(string filePath)
    {
        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(filePath));
        return await EvalStringAsync($"docengine.xlsxToHtmlAsync({JsonSerializer.Serialize(b64)})");
    }

    public Task<string> PptxToHtmlAsync(string filePath)
        => Task.FromResult(PptxToHtmlConverter.ToHtml(filePath));

    /// <summary>把 HTML 填入打印容器，等待图片加载后输出 PDF。</summary>
    public async Task RenderHtmlToPdfAsync(string html, string pdfPath)
    {
        await EvalStringAsync($"docengine.setContent({JsonSerializer.Serialize(html)})");
        await RunJobAsync("images", "docengine.startWaitImages()");
        var ok = await _webView.CoreWebView2.PrintToPdfAsync(pdfPath, null);
        if (!ok)
            throw new InvalidOperationException("生成 PDF 失败（WebView2 打印返回失败）");
    }

    /// <summary>
    /// 把 PNG 页面图片依序合成为一份 PDF（pdf-lib，页面尺寸 = 图片像素，像素不做任何重采样）。
    /// </summary>
    public async Task ImagesToPdfAsync(IReadOnlyList<string> pngPaths, string pdfPath, IProgress<string>? progress, CancellationToken ct)
    {
        var b64List = new List<string>(pngPaths.Count);
        foreach (var path in pngPaths)
        {
            ct.ThrowIfCancellationRequested();
            b64List.Add(Convert.ToBase64String(await File.ReadAllBytesAsync(path, ct)));
        }
        var data = await RunJobAsync("imgpdf",
            $"docengine.startImagesToPdf({JsonSerializer.Serialize(b64List)})", ct);
        var b64 = JsonSerializer.Deserialize<string>(data)
            ?? throw new InvalidOperationException("图片合成 PDF 返回为空");
        await File.WriteAllBytesAsync(pdfPath, Convert.FromBase64String(b64), ct);
    }

    /// <summary>
    /// PDF → 每页图片（单页输出原名，多页输出 名称_第N页），返回输出文件列表。
    /// mergeIntoOne=true 时所有页面纵向拼接为一张长图（输出单个文件）。
    /// quality 仅对 JPG 生效（1-100）；maxLongEdge 限制渲染长边像素（清晰度上限，0 = 引擎默认）。
    /// </summary>
    public async Task<List<string>> PdfToImagesAsync(string pdfPath, string outputDir,
        string baseName, string format, int quality, IProgress<string>? progress, CancellationToken ct,
        bool mergeIntoOne = false, int maxLongEdge = 2200)
    {
        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(pdfPath, ct));
        var data = await RunJobAsync("pdf",
            $"docengine.startPdfToImages({JsonSerializer.Serialize(b64)}, {JsonSerializer.Serialize(format)}, {quality}, {maxLongEdge}, {(mergeIntoOne ? "true" : "false")})", ct);

        using var doc = JsonDocument.Parse(data);
        var el = doc.RootElement;
        if (el.ValueKind == JsonValueKind.String)
            el = JsonDocument.Parse(el.GetString()!).RootElement;
        if (el.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"PDF 渲染返回了意外结果：{Truncate(data, 200)}");
        var items = el.EnumerateArray().ToList();
        if (items.Count == 0)
            throw new InvalidOperationException("PDF 未渲染出任何页面");

        Directory.CreateDirectory(outputDir);
        var paths = new List<string>();
        var ext = format == "jpg" ? ".jpg" : ".png";

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var dataUrl = items[i].GetString() ?? "";
            var comma = dataUrl.IndexOf(',');
            if (comma <= 0) continue;
            var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);

            // 合并长图只有一个数据块：输出单文件；分页时按 名称_第N页 命名
            var outPath = mergeIntoOne
                ? FormatConvertPlanner.BuildDocPagePath(outputDir, baseName, ext, 1, 1)
                : FormatConvertPlanner.BuildDocPagePath(outputDir, baseName, ext, i + 1, items.Count);
            try
            {
                await File.WriteAllBytesAsync(outPath, bytes, ct);
            }
            catch
            {
                try { File.Delete(outPath); } catch { }
                throw;
            }
            paths.Add(outPath);
            progress?.Report(mergeIntoOne
                ? "已生成合并长图"
                : $"已输出图片 {i + 1}/{items.Count} 页");
        }
        return paths;
    }

    /// <summary>PDF 文本提取结果（每页）。</summary>
    public sealed record PdfExtractPage(int Page, string Text, List<string[]> Rows);

    /// <summary>PDF → 每页文本 + 表格行（pdf.js 文本层 + 行列聚类）。</summary>
    public async Task<List<PdfExtractPage>> PdfExtractAsync(string pdfPath, CancellationToken ct = default)
    {
        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(pdfPath, ct));
        var data = await RunJobAsync("pdfextract",
            $"docengine.startPdfExtract({JsonSerializer.Serialize(b64)})", ct);

        using var doc = JsonDocument.Parse(data);
        var el = doc.RootElement;
        if (el.ValueKind == JsonValueKind.String)
            el = JsonDocument.Parse(el.GetString()!).RootElement;
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("pages", out var pagesEl)
            || pagesEl.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"PDF 文本提取返回了意外结果：{Truncate(data, 200)}");

        var result = new List<PdfExtractPage>();
        foreach (var page in pagesEl.EnumerateArray())
        {
            var rows = new List<string[]>();
            if (page.TryGetProperty("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rowsEl.EnumerateArray())
                    rows.Add(row.EnumerateArray().Select(c => c.GetString() ?? "").ToArray());
            }
            result.Add(new PdfExtractPage(
                page.TryGetProperty("page", out var n) ? n.GetInt32() : result.Count + 1,
                page.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                rows));
        }
        if (result.Count == 0)
            throw new InvalidOperationException("PDF 没有可提取的页面");
        return result;
    }

    /// <summary>多份 PDF 合并为一个（pdf-lib）。</summary>
    public async Task PdfMergeAsync(IReadOnlyList<string> pdfPaths, string outputPath, CancellationToken ct = default)
    {
        if (pdfPaths.Count == 0) throw new InvalidOperationException("没有可合并的 PDF");
        var b64List = new List<string>(pdfPaths.Count);
        foreach (var p in pdfPaths)
            b64List.Add(Convert.ToBase64String(await File.ReadAllBytesAsync(p, ct)));

        var arrayJson = JsonSerializer.Serialize(b64List);
        var data = await RunJobAsync("pdfmerge",
            $"docengine.startPdfMerge({JsonSerializer.Serialize(arrayJson)})", ct);

        var b64 = data.Trim('"');
        if (string.IsNullOrEmpty(b64))
            throw new InvalidOperationException("PDF 合并返回了空结果");
        await File.WriteAllBytesAsync(outputPath, Convert.FromBase64String(b64), ct);
    }

    /// <summary>单份 PDF 拆分为单页 PDF（名称_第N页.pdf），返回输出文件列表。</summary>
    public async Task<List<string>> PdfSplitAsync(string pdfPath, string outputDir,
        string baseName, CancellationToken ct = default)
    {
        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(pdfPath, ct));
        var data = await RunJobAsync("pdfsplit",
            $"docengine.startPdfSplit({JsonSerializer.Serialize(b64)})", ct);

        using var doc = JsonDocument.Parse(data);
        var el = doc.RootElement;
        if (el.ValueKind == JsonValueKind.String)
            el = JsonDocument.Parse(el.GetString()!).RootElement;
        if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() == 0)
            throw new InvalidOperationException("PDF 拆分没有返回任何页面");

        Directory.CreateDirectory(outputDir);
        var items = el.EnumerateArray().ToList();
        var paths = new List<string>();
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var outPath = FormatConvertPlanner.BuildDocPagePath(outputDir, baseName, ".pdf", i + 1, items.Count);
            await File.WriteAllBytesAsync(outPath, Convert.FromBase64String(items[i].GetString() ?? ""), ct);
            paths.Add(outPath);
        }
        return paths;
    }

    /// <summary>工作簿（xlsx/xls/ods/csv/et）→ 每个工作表的文本。format: "csv" | "json"。</summary>
    public async Task<List<(string Name, string Text)>> WorkbookOutAsync(
        string filePath, string format, CancellationToken ct = default)
    {
        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(filePath, ct));
        var data = await RunJobAsync("wbout",
            $"docengine.startWorkbookOut({JsonSerializer.Serialize(b64)}, {JsonSerializer.Serialize(format)})", ct);

        using var doc = JsonDocument.Parse(data);
        var el = doc.RootElement;
        if (el.ValueKind == JsonValueKind.String)
            el = JsonDocument.Parse(el.GetString()!).RootElement;
        if (el.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"工作簿解析返回了意外结果：{Truncate(data, 200)}");
        return el.EnumerateArray()
            .Select(s => (s.GetProperty("name").GetString() ?? "Sheet",
                s.GetProperty("text").GetString() ?? ""))
            .ToList();
    }

    /// <summary>工作簿 → 合并全部工作表的 XLSX（SheetJS），写入 outputPath。</summary>
    public async Task WorkbookToXlsxAsync(string filePath, string outputPath, CancellationToken ct = default)
    {
        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(filePath, ct));
        var data = await RunJobAsync("wbxlsx",
            $"docengine.startWorkbookToXlsx({JsonSerializer.Serialize(b64)})", ct);
        await File.WriteAllBytesAsync(outputPath, Convert.FromBase64String(data.Trim('"')), ct);
    }

    /// <summary>行数组 → XLSX（SheetJS）。sheets: (工作表名, 行单元格) 列表，写入 outputPath。</summary>
    public async Task AoaToXlsxAsync(IReadOnlyList<(string Name, string[][] Rows)> sheets,
        string outputPath, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            sheets = sheets.Select(s => new { name = s.Name, rows = s.Rows })
        });
        var data = await RunJobAsync("aoaxlsx",
            $"docengine.startAoaToXlsx({JsonSerializer.Serialize(payload)})", ct);
        await File.WriteAllBytesAsync(outputPath, Convert.FromBase64String(data.Trim('"')), ct);
    }
}
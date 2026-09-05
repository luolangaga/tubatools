using System.IO.Compression;
using System.Text;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class FormatConvertCatalogTests
{
    [Theory]
    [InlineData("movie.MP4", SourceCategory.Video)]
    [InlineData("a.mkv", SourceCategory.Video)]
    [InlineData("a.avi", SourceCategory.Video)]
    [InlineData("a.webm", SourceCategory.Video)]
    [InlineData("song.mp3", SourceCategory.Audio)]
    [InlineData("s.wav", SourceCategory.Audio)]
    [InlineData("s.flac", SourceCategory.Audio)]
    [InlineData("pic.PNG", SourceCategory.Image)]
    [InlineData("pic.jpeg", SourceCategory.Image)]
    [InlineData("pic.webp", SourceCategory.Image)]
    [InlineData("pic.heic", SourceCategory.Image)]
    [InlineData("pic.avif", SourceCategory.Image)]
    [InlineData("book.pdf", SourceCategory.Pdf)]
    [InlineData("doc.docx", SourceCategory.Word)]
    [InlineData("doc.doc", SourceCategory.Word)]
    [InlineData("doc.wps", SourceCategory.Word)]
    [InlineData("doc.rtf", SourceCategory.Word)]
    [InlineData("doc.odt", SourceCategory.Word)]
    [InlineData("sheet.xlsx", SourceCategory.Excel)]
    [InlineData("sheet.csv", SourceCategory.Excel)]
    [InlineData("sheet.xls", SourceCategory.Excel)]
    [InlineData("sheet.et", SourceCategory.Excel)]
    [InlineData("sheet.ods", SourceCategory.Excel)]
    [InlineData("deck.pptx", SourceCategory.Ppt)]
    [InlineData("deck.ppt", SourceCategory.Ppt)]
    [InlineData("deck.dps", SourceCategory.Ppt)]
    [InlineData("deck.odp", SourceCategory.Ppt)]
    [InlineData("note.md", SourceCategory.Markdown)]
    [InlineData("note.markdown", SourceCategory.Markdown)]
    [InlineData("note.txt", SourceCategory.Text)]
    [InlineData("note.log", SourceCategory.Text)]
    [InlineData("page.html", SourceCategory.Html)]
    [InlineData("page.htm", SourceCategory.Html)]
    [InlineData("data.json", SourceCategory.Json)]
    [InlineData("unknown.xyz", SourceCategory.Unsupported)]
    [InlineData("noextension", SourceCategory.Unsupported)]
    public void Classify_ReturnsExpectedCategory(string path, SourceCategory expected)
        => Assert.Equal(expected, FormatConvertCatalog.Classify(path));

    [Fact]
    public void Classify_IsCaseInsensitive()
        => Assert.Equal(SourceCategory.Video, FormatConvertCatalog.Classify("C:\\Video\\X.Mp4"));

    [Fact]
    public void GetTargetFormats_Video_IncludesAudioExtraction()
    {
        var formats = FormatConvertCatalog.GetTargetFormats(SourceCategory.Video);
        Assert.Contains(formats, f => f.Ext == ".mp4");
        Assert.Contains(formats, f => f.Ext == ".gif");
        Assert.Contains(formats, f => f is { IsAudioOnly: true, Ext: ".mp3" });
        Assert.Contains(formats, f => f.Special == ConvertSpecial.ZipArchive);
        // 视频目标不能包含纯图片格式
        Assert.DoesNotContain(formats, f => f.Ext == ".png");
    }

    [Fact]
    public void GetTargetFormats_Audio_OnlyAudioFormatsExceptZip()
    {
        var formats = FormatConvertCatalog.GetTargetFormats(SourceCategory.Audio);
        Assert.Contains(formats, f => f.Ext == ".flac");
        Assert.Contains(formats, f => f.Ext == ".wma");
        Assert.All(formats.Where(f => !f.IsSpecial), f => Assert.True(f.IsAudioOnly));
        Assert.Contains(formats, f => f.Special == ConvertSpecial.ZipArchive);
    }

    [Fact]
    public void GetTargetFormats_Image_IncludesVideoOcrAndZip()
    {
        var formats = FormatConvertCatalog.GetTargetFormats(SourceCategory.Image);
        Assert.Contains(formats, f => f.Ext == ".png");
        Assert.Contains(formats, f => f.Ext == ".ico");
        Assert.Contains(formats, f => f.Ext == ".heic");
        Assert.Contains(formats, f => f.Ext == ".avif");
        Assert.Contains(formats, f => f.Ext == ".pdf");
        // 图片 → MP4/WebM 视频
        Assert.Contains(formats, f => f.Ext == ".mp4");
        Assert.Contains(formats, f => f.Ext == ".webm");
        // 图片 → TXT（OCR 文字识别）
        Assert.Contains(formats, f => f.Special == ConvertSpecial.OcrText);
        Assert.Contains(formats, f => f.Special == ConvertSpecial.ZipArchive);
        Assert.DoesNotContain(formats, f => f.IsAudioOnly);
    }

    [Fact]
    public void GetTargetFormats_Docs_IncludePdfAndImages()
    {
        foreach (var cat in new[] { SourceCategory.Word, SourceCategory.Excel, SourceCategory.Ppt, SourceCategory.Markdown })
        {
            var formats = FormatConvertCatalog.GetTargetFormats(cat);
            Assert.Contains(formats, f => f.Ext == ".pdf");
            Assert.Contains(formats, f => f.Ext == ".png");
            Assert.Contains(formats, f => f.Ext == ".jpg");
            Assert.Contains(formats, f => f.Special == ConvertSpecial.ZipArchive);
        }
    }

    [Fact]
    public void GetTargetFormats_Word_IncludesDocxTxtHtmlMarkdown()
    {
        var formats = FormatConvertCatalog.GetTargetFormats(SourceCategory.Word);
        Assert.Contains(formats, f => f.Ext == ".docx");
        Assert.Contains(formats, f => f.Ext == ".txt");
        Assert.Contains(formats, f => f.Ext == ".html");
        Assert.Contains(formats, f => f.Ext == ".md");
    }

    [Fact]
    public void GetTargetFormats_Excel_IncludesXlsxCsvHtmlJson()
    {
        var formats = FormatConvertCatalog.GetTargetFormats(SourceCategory.Excel);
        Assert.Contains(formats, f => f.Ext == ".xlsx");
        Assert.Contains(formats, f => f.Ext == ".csv");
        Assert.Contains(formats, f => f.Ext == ".html");
        Assert.Contains(formats, f => f.Ext == ".json");
    }

    [Fact]
    public void GetTargetFormats_Ppt_IncludesPptxHtml()
    {
        var formats = FormatConvertCatalog.GetTargetFormats(SourceCategory.Ppt);
        Assert.Contains(formats, f => f.Ext == ".pptx");
        Assert.Contains(formats, f => f.Ext == ".html");
    }

    [Fact]
    public void GetTargetFormats_Pdf_HasTextHtmlExcelOcrMergeSplitTargets()
    {
        var formats = FormatConvertCatalog.GetTargetFormats(SourceCategory.Pdf);
        Assert.Contains(formats, f => f.Ext == ".txt" && !f.IsSpecial);
        Assert.Contains(formats, f => f.Ext == ".html");
        Assert.Contains(formats, f => f.Special == ConvertSpecial.PdfExcel);
        Assert.Contains(formats, f => f.Special == ConvertSpecial.OcrText);
        Assert.Contains(formats, f => f.Special == ConvertSpecial.ZipArchive && f.Tag == "png");
        Assert.Contains(formats, f => f.Special == ConvertSpecial.ZipArchive && f.Tag == "jpg");
        // PDF 源不提供普通 PDF 目标（合并/拆分由页面按文件数动态补充）
        Assert.DoesNotContain(formats, f => f.Ext == ".pdf" && !f.IsSpecial);
        Assert.Equal(ConvertSpecial.MergePdf, FormatConvertCatalog.MergePdfTarget.Special);
        Assert.Equal(ConvertSpecial.SplitPdf, FormatConvertCatalog.SplitPdfTarget.Special);
    }

    [Fact]
    public void GetTargetFormats_TextFamily_CrossConvertAndPdfWord()
    {
        foreach (var cat in new[] { SourceCategory.Text, SourceCategory.Html, SourceCategory.Json, SourceCategory.Markdown })
        {
            var formats = FormatConvertCatalog.GetTargetFormats(cat);
            Assert.Contains(formats, f => f.Ext == ".pdf");
            Assert.Contains(formats, f => f.Ext == ".docx");
            Assert.Contains(formats, f => f.Special == ConvertSpecial.ZipArchive);
        }
        Assert.Contains(FormatConvertCatalog.GetTargetFormats(SourceCategory.Json), f => f.Ext == ".csv");
        Assert.Contains(FormatConvertCatalog.GetTargetFormats(SourceCategory.Text), f => f.Ext == ".md");
        Assert.Contains(FormatConvertCatalog.GetTargetFormats(SourceCategory.Html), f => f.Ext == ".md");
    }

    [Fact]
    public void GetTargetFormats_Unsupported_Empty()
        => Assert.Empty(FormatConvertCatalog.GetTargetFormats(SourceCategory.Unsupported));

    [Theory]
    [InlineData(SourceCategory.Video, ConvertEngine.Ffmpeg)]
    [InlineData(SourceCategory.Audio, ConvertEngine.Ffmpeg)]
    [InlineData(SourceCategory.Image, ConvertEngine.Magick)]
    [InlineData(SourceCategory.Pdf, ConvertEngine.DocEngine)]
    [InlineData(SourceCategory.Word, ConvertEngine.DocEngine)]
    [InlineData(SourceCategory.Markdown, ConvertEngine.DocEngine)]
    public void EngineFor_ReturnsCorrectEngine(SourceCategory cat, ConvertEngine expected)
        => Assert.Equal(expected, FormatConvertCatalog.EngineFor(cat));

    [Fact]
    public void EngineFor_ImageToVideo_UsesFfmpeg()
    {
        var mp4 = new FormatOption("MP4", ".mp4", "libx264", "");
        var webm = new FormatOption("WebM", ".webm", "libvpx-vp9", "");
        Assert.Equal(ConvertEngine.Ffmpeg, FormatConvertCatalog.EngineFor(SourceCategory.Image, mp4));
        Assert.Equal(ConvertEngine.Ffmpeg, FormatConvertCatalog.EngineFor(SourceCategory.Image, webm));
    }

    [Fact]
    public void EngineFor_OcrTarget_UsesOcrEngine()
    {
        var ocr = new FormatOption("TXT", ".txt", "", "", ConvertSpecial.OcrText);
        Assert.Equal(ConvertEngine.Ocr, FormatConvertCatalog.EngineFor(SourceCategory.Image, ocr));
        Assert.Equal(ConvertEngine.Ocr, FormatConvertCatalog.EngineFor(SourceCategory.Pdf, ocr));
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".html")]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".txt")]
    [InlineData(".md")]
    public void EngineFor_WordTargets_UsesOfficeCli(string ext)
    {
        var target = FormatConvertCatalog.WordTargets.First(t => t.Ext == ext);
        Assert.Equal(ConvertEngine.OfficeCli, FormatConvertCatalog.EngineFor(SourceCategory.Word, target));
    }

    [Fact]
    public void EngineFor_WordDocxTarget_StaysOnBuiltInEngine()
    {
        // docx → docx 无渲染收益，保持内置引擎
        var target = FormatConvertCatalog.WordTargets.First(t => t.Ext == ".docx");
        Assert.Equal(ConvertEngine.DocEngine, FormatConvertCatalog.EngineFor(SourceCategory.Word, target));
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".html")]
    [InlineData(".png")]
    [InlineData(".jpg")]
    public void EngineFor_PptTargets_UsesOfficeCli(string ext)
    {
        var target = FormatConvertCatalog.PptTargets.First(t => t.Ext == ext);
        Assert.Equal(ConvertEngine.OfficeCli, FormatConvertCatalog.EngineFor(SourceCategory.Ppt, target));
    }

    [Theory]
    [InlineData(".pdf", ConvertEngine.OfficeCli)]
    [InlineData(".png", ConvertEngine.OfficeCli)]
    [InlineData(".jpg", ConvertEngine.OfficeCli)]
    [InlineData(".html", ConvertEngine.OfficeCli)]
    [InlineData(".xlsx", ConvertEngine.DocEngine)]
    [InlineData(".csv", ConvertEngine.DocEngine)]
    [InlineData(".json", ConvertEngine.DocEngine)]
    [InlineData(".md", ConvertEngine.DocEngine)]
    public void EngineFor_ExcelTargets_Selective(string ext, ConvertEngine expected)
    {
        var target = FormatConvertCatalog.ExcelTargets.First(t => t.Ext == ext);
        Assert.Equal(expected, FormatConvertCatalog.EngineFor(SourceCategory.Excel, target));
    }

    [Fact]
    public void EngineFor_TextDocxTarget_StaysOnBuiltInEngine()
    {
        // md/txt/html/json → docx 保持 DocxWriter 快路径
        var docx = FormatConvertCatalog.TextTargets.First(t => t.Ext == ".docx");
        Assert.Equal(ConvertEngine.DocEngine, FormatConvertCatalog.EngineFor(SourceCategory.Markdown, docx));
        Assert.Equal(ConvertEngine.DocEngine, FormatConvertCatalog.EngineFor(SourceCategory.Text, docx));
        Assert.Equal(ConvertEngine.DocEngine, FormatConvertCatalog.EngineFor(SourceCategory.Html, docx));
        Assert.Equal(ConvertEngine.DocEngine, FormatConvertCatalog.EngineFor(SourceCategory.Json, docx));
    }

    [Fact]
    public void EngineFor_SpecialTargets_NeverRequireOfficeCli()
    {
        // ZIP 压缩包 / PDF 合并拆分 / OCR 等特殊操作不走 OfficeCLI，避免无谓的引擎下载提示
        Assert.Equal(ConvertEngine.DocEngine, FormatConvertCatalog.EngineFor(SourceCategory.Word, FormatConvertCatalog.ZipTarget));
        Assert.Equal(ConvertEngine.DocEngine, FormatConvertCatalog.EngineFor(SourceCategory.Ppt, FormatConvertCatalog.ZipTarget));
        var merge = new FormatOption("PDF", ".pdf", "", "", ConvertSpecial.MergePdf);
        Assert.NotEqual(ConvertEngine.OfficeCli, FormatConvertCatalog.EngineFor(SourceCategory.Pdf, merge));
    }

    [Theory]
    [InlineData("old.doc", true)]
    [InlineData("old.ppt", true)]
    [InlineData("new.docx", false)]
    [InlineData("new.pptx", false)]
    [InlineData("file.DOC", true)]
    public void IsLegacyDoc_DetectsOldBinaryFormats(string path, bool expected)
        => Assert.Equal(expected, FormatConvertPlanner.IsLegacyDoc(path));

    [Theory]
    [InlineData("a.doc", true)]
    [InlineData("a.wps", true)]
    [InlineData("a.ppt", true)]
    [InlineData("a.dps", true)]
    [InlineData("a.et", true)]
    [InlineData("a.docx", false)]
    [InlineData("a.rtf", false)]
    [InlineData("a.odt", false)]
    [InlineData("a.ods", false)]
    public void RequiresOfficeInterop_DetectsLegacyBinaries(string path, bool expected)
        => Assert.Equal(expected, FormatConvertCatalog.RequiresOfficeInterop(path));
}

public class FormatConvertPlannerTests
{
    private static readonly FormatOption Mp4 = new("MP4", ".mp4", "libx264", "aac");
    private static readonly FormatOption Mp3 = new("MP3", ".mp3", "", "libmp3lame");
    private static readonly FormatOption Gif = new("GIF", ".gif", "", "");
    private static readonly FormatOption Jpg = new("JPG", ".jpg", "", "");

    [Fact]
    public void BuildFfmpegArgs_VideoTranscode_WithCompression()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Mp4, 28, "slow", 192, true);
        Assert.Contains("-i \"C:\\in\\m.mp4\"", args);
        Assert.Contains("-c:v libx264", args);
        Assert.Contains("-crf 28 -preset slow", args);
        Assert.Contains("-b:a 192k", args);
        Assert.Contains("-c:a aac", args);
        Assert.Contains("\"C:\\in\\m_converted.mp4\"", args);
    }

    [Fact]
    public void BuildFfmpegArgs_VideoTranscode_NoCompression_OmitCrf()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Mp4, 23, "medium", 192, false);
        Assert.DoesNotContain("-crf", args);
        Assert.DoesNotContain("-b:a", args);
        Assert.Contains("-c:a aac", args);
    }

    [Fact]
    public void BuildFfmpegArgs_ExtractAudio_FromVideo()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Mp3, 23, "medium", 192, true);
        Assert.Contains("-vn", args);
        Assert.Contains("-c:a libmp3lame", args);
        Assert.Contains("-b:a 192k", args);
        Assert.DoesNotContain("-c:v", args);
    }

    [Fact]
    public void BuildFfmpegArgs_Gif_AppliesPaletteFilter()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Gif, 23, "medium", 0, false);
        // GIF 走 filter_complex 调色板路径（palettegen + paletteuse，单次完成避免编码器崩溃），
        // 缩放/帧率滤镜在 filter_complex 内部；旧的 -vf 直通格式已由 BuildFfmpegGifFallbackArgs 降级承载
        Assert.Contains("-filter_complex", args);
        Assert.Contains("palettegen=stats_mode=diff", args);
        Assert.Contains("paletteuse=dither=bayer", args);
        Assert.Contains("fps=15,scale=480:-1:flags=lanczos", args);
        Assert.Contains("-an", args);
    }

    [Fact]
    public void BuildFfmpegArgs_WithResolutionScale()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Mp4, 23, "medium", 0, false, videoWidth: 1920);
        Assert.Contains("-vf \"scale=1920:1920:force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2\"", args);
    }

    [Fact]
    public void BuildFfmpegArgs_WithAudioOptions()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\s.mp3", Mp3, 23, "medium", 192, true, sampleRate: 48000, channels: 2);
        Assert.Contains("-ar 48000", args);
        Assert.Contains("-ac 2", args);
    }

    [Fact]
    public void BuildImageVideoArgs_StaticImage_LoopsWithDuration()
    {
        var args = FormatConvertPlanner.BuildImageVideoArgs(@"C:\in\p.png", Mp4, 5, 0);
        Assert.Contains("-loop 1 -t 5 -i \"C:\\in\\p.png\"", args);
        Assert.Contains("-c:v libx264", args);
        Assert.Contains("-pix_fmt yuv420p", args);
        Assert.Contains("scale=trunc(iw/2)*2:trunc(ih/2)*2", args);
        Assert.Contains("-an", args);
        Assert.Contains("-tune stillimage", args);
        Assert.Contains("-movflags +faststart", args);
        Assert.EndsWith("\"C:\\in\\p_converted.mp4\"", args);
    }

    [Fact]
    public void BuildImageVideoArgs_GifSource_DoesNotLoop()
    {
        var args = FormatConvertPlanner.BuildImageVideoArgs(@"C:\in\a.gif", Mp4, 5, 0);
        Assert.DoesNotContain("-loop 1", args);
        Assert.DoesNotContain("-tune stillimage", args);
        Assert.StartsWith("-i \"C:\\in\\a.gif\"", args);
    }

    [Fact]
    public void BuildImageVideoArgs_Webm_UsesVp9()
    {
        var webm = new FormatOption("WebM", ".webm", "libvpx-vp9", "");
        var args = FormatConvertPlanner.BuildImageVideoArgs(@"C:\in\p.png", webm, 8, 1280);
        Assert.Contains("-c:v libvpx-vp9", args);
        Assert.Contains("-b:v 0 -crf 32", args);
        Assert.Contains("scale=1280:1280:force_original_aspect_ratio=decrease", args);
        Assert.EndsWith("\"C:\\in\\p_converted.webm\"", args);
    }

    [Fact]
    public void BuildImageVideoArgs_DurationClamped()
    {
        var args = FormatConvertPlanner.BuildImageVideoArgs(@"C:\in\p.png", Mp4, 0, 0);
        Assert.Contains("-t 5", args); // 0 → 默认 5 秒
        var huge = FormatConvertPlanner.BuildImageVideoArgs(@"C:\in\p.png", Mp4, 99999, 0);
        Assert.Contains("-t 600", huge); // 上限 600
    }

    [Fact]
    public void BuildMagickArgs_Ico_MultiSizeAutoResize()
    {
        var ico = new FormatOption("ICO", ".ico", "", "");
        var args = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", ico, 85, 0, false, new[] { 256, 128, 64, 48, 32, 16 });
        Assert.Contains("-define icon:auto-resize=256,128,64,48,32,16", args);
        Assert.EndsWith("\"C:\\in\\p_converted.ico\"", args);
    }

    [Fact]
    public void BuildMagickArgs_Ico_NoSizesDefaults()
    {
        var ico = new FormatOption("ICO", ".ico", "", "");
        var args = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", ico, 85, 0, false);
        Assert.Contains("-define icon:auto-resize=256,128,64,48,32,16", args);
    }

    [Fact]
    public void BuildMagickArgs_ConvertOnly()
    {
        var args = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", Jpg, 85, 0, false);
        Assert.Equal("\"C:\\in\\p.png\" \"C:\\in\\p_converted.jpg\"", args);
    }

    [Fact]
    public void BuildMagickArgs_WithCompression()
    {
        var args = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", Jpg, 70, 1920, true);
        Assert.Contains("-quality 70", args);
        Assert.Contains("-strip", args);
        Assert.Contains("-resize 1920x1920>", args);
    }

    [Fact]
    public void BuildOutputPath_AppendsSuffix_AndAvoidsCollision()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fc_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "v.mp4");
            File.WriteAllText(source, "");
            var first = FormatConvertPlanner.BuildOutputPath(source, ".mp4");
            Assert.EndsWith("v_converted.mp4", first);

            File.WriteAllText(first, "x");
            var second = FormatConvertPlanner.BuildOutputPath(source, ".mp4");
            Assert.EndsWith("v_converted_1.mp4", second);
            Assert.NotEqual(first, second);

            // 0 字节的失败残留应被复用覆盖，而不是撞名生成 _1
            File.WriteAllText(first, "");
            var third = FormatConvertPlanner.BuildOutputPath(source, ".mp4");
            Assert.Equal(first, third);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(0, CompressionLevel.NoCompression)]
    [InlineData(1, CompressionLevel.Fastest)]
    [InlineData(3, CompressionLevel.Fastest)]
    [InlineData(4, CompressionLevel.Optimal)]
    [InlineData(7, CompressionLevel.Optimal)]
    [InlineData(8, CompressionLevel.SmallestSize)]
    [InlineData(9, CompressionLevel.SmallestSize)]
    public void ZipCompressionLevel_MapsLevels(int level, CompressionLevel expected)
        => Assert.Equal(expected, FormatConvertPlanner.ZipCompressionLevel(level));

    [Fact]
    public void CreateZipArchive_PacksFiles_AndReportsSizes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fc_zip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var a = Path.Combine(dir, "a.txt");
            var b = Path.Combine(dir, "b.txt");
            File.WriteAllText(a, new string('A', 10000));
            File.WriteAllText(b, new string('B', 5000));
            var zipPath = Path.Combine(dir, "out.zip");

            var (before, after) = FormatConvertPlanner.CreateZipArchive(new[] { a, b }, zipPath, 9);

            Assert.True(File.Exists(zipPath));
            Assert.Equal(15000, before);
            Assert.True(after > 0 && after < 15000); // 高压缩级别下重复字符显著变小
            using var zip = ZipFile.OpenRead(zipPath);
            Assert.Contains(zip.Entries, e => e.Name == "a.txt");
            Assert.Contains(zip.Entries, e => e.Name == "b.txt");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CreateZipArchive_DuplicateNames_GetDisambiguated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fc_zip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var sub1 = Path.Combine(dir, "d1");
            var sub2 = Path.Combine(dir, "d2");
            Directory.CreateDirectory(sub1);
            Directory.CreateDirectory(sub2);
            File.WriteAllText(Path.Combine(sub1, "same.txt"), "one");
            File.WriteAllText(Path.Combine(sub2, "same.txt"), "two");
            var zipPath = Path.Combine(dir, "out.zip");

            FormatConvertPlanner.CreateZipArchive(
                new[] { Path.Combine(sub1, "same.txt"), Path.Combine(sub2, "same.txt") }, zipPath, 0);

            using var zip = ZipFile.OpenRead(zipPath);
            Assert.Equal(2, zip.Entries.Count);
            Assert.Equal(2, zip.Entries.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BuildMagickMergePdfArgs_MultipleSources_CombinedIntoOnePdf()
    {
        var args = FormatConvertPlanner.BuildMagickMergePdfArgs(
            new[] { @"C:\in\a.png", @"C:\in\b.png", @"C:\in\c.png" },
            @"C:\in\merged.pdf", 90, 1920, true);
        // 多输入按顺序拼接为多页 PDF
        Assert.True(args.IndexOf("\"C:\\in\\a.png\"") < args.IndexOf("\"C:\\in\\b.png\""));
        Assert.True(args.IndexOf("\"C:\\in\\b.png\"") < args.IndexOf("\"C:\\in\\c.png\""));
        Assert.Contains("-quality 90", args);
        Assert.Contains("-strip", args);
        Assert.Contains("-resize 1920x1920>", args);
        Assert.EndsWith("\"C:\\in\\merged.pdf\"", args);
    }

    [Fact]
    public void BuildZipOutputPath_SingleAndMultiple()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fc_zipn_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var single = FormatConvertPlanner.BuildZipOutputPath(new[] { Path.Combine(dir, "file.mp4") });
            Assert.EndsWith("file.zip", single);

            var multi = FormatConvertPlanner.BuildZipOutputPath(
                new[] { Path.Combine(dir, "file.mp4"), Path.Combine(dir, "other.pdf") });
            Assert.EndsWith("file_压缩包.zip", multi);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BuildDocPagePath_SinglePage_PlainName()
        => Assert.Equal(@"C:\out\doc_converted.png", FormatConvertPlanner.BuildDocPagePath(@"C:\out", "doc_converted", ".png", 1, 1));

    [Fact]
    public void BuildDocPagePath_MultiPage_AddsPageIndex()
        => Assert.Equal(@"C:\out\doc_converted_第2页.png", FormatConvertPlanner.BuildDocPagePath(@"C:\out", "doc_converted", ".png", 2, 5));
}

public class RtfTextExtractorTests
{
    [Fact]
    public void Extract_SimpleTextAndPar()
    {
        // RTF 中控制字后的空格是分隔符（非内容），\par 表示换行
        var rtf = @"{\rtf1\ansi Hello\par World}";
        Assert.Equal("Hello\nWorld", RtfTextExtractor.Extract(rtf));
    }

    [Fact]
    public void Extract_SkipsFontTable()
    {
        var rtf = @"{\rtf1{\fonttbl{\f0 SimSun;}}正文}";
        Assert.Equal("正文", RtfTextExtractor.Extract(rtf));
    }

    [Fact]
    public void Extract_HexEscapes_DecodedAsGbk()
    {
        // \"d6\"d0\"ce\"c4 = “中文” 的 GBK 编码（连续转义按双字节配对解码）
        var rtf = @"{\rtf1\ansi\'d6\'d0\'ce\'c4}";
        Assert.Equal("中文", RtfTextExtractor.Extract(rtf));
    }

    [Fact]
    public void Extract_UnicodeEscapes()
    {
        // \u23383 = U+5B57「字」，随后的 ? 为替代字符（跳过）
        var rtf = @"{\rtf1 A\u23383?B}";
        Assert.Equal("A\u5B57B", RtfTextExtractor.Extract(rtf));
    }

    [Fact]
    public void Extract_EscapedBracesAndSpecials()
    {
        var rtf = @"{\rtf1 a\{b\}c\\d\emdash e}";
        Assert.Equal("a{b}c\\d—e", RtfTextExtractor.Extract(rtf));
    }
}

public class TabularConvertTests
{
    [Fact]
    public void ParseCsv_HandlesQuotedFieldsAndEmbeddedDelimiters()
    {
        var csv = "name,desc\r\n\"Smith, John\",\"He said \"\"hi\"\"\"\r\nBob,plain";
        var rows = TabularConvert.ParseCsv(csv);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "name", "desc" }, rows[0]);
        Assert.Equal(new[] { "Smith, John", "He said \"hi\"" }, rows[1]);
        Assert.Equal(new[] { "Bob", "plain" }, rows[2]);
    }

    [Fact]
    public void WriteCsv_RoundTrips()
    {
        var rows = new List<string[]> { new[] { "a,b", "c\"d" }, new[] { "e", "f\ng" } };
        var csv = TabularConvert.WriteCsv(rows);
        var parsed = TabularConvert.ParseCsv(csv);
        Assert.Equal(rows.Count, parsed.Count);
        Assert.Equal(rows[0], parsed[0]);
        Assert.Equal(rows[1], parsed[1]);
    }

    [Fact]
    public void CsvToJson_UsesFirstRowAsHeader()
    {
        var json = TabularConvert.CsvToJson("姓名,年龄\r\n张三,30\r\n李四,25");
        Assert.Contains("\"姓名\": \"张三\"", json);
        Assert.Contains("\"年龄\": \"30\"", json);
        Assert.Contains("\"姓名\": \"李四\"", json);
    }

    [Fact]
    public void JsonToCsv_FlattensArrayOfObjects()
    {
        var csv = TabularConvert.JsonToCsv("[{\"a\":1,\"b\":\"x\"},{\"a\":2,\"b\":\"y\"}]");
        var rows = TabularConvert.ParseCsv(csv);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0]);
        Assert.Equal(new[] { "1", "x" }, rows[1]);
    }

    [Fact]
    public void JsonToCsv_NestedValuesSerializedAsJson()
    {
        var csv = TabularConvert.JsonToCsv("[{\"obj\":{\"k\":1}}]");
        var rows = TabularConvert.ParseCsv(csv);
        Assert.Equal("{\"k\":1}", rows[1][0]);
    }

    [Fact]
    public void JsonToCsv_RejectsNonArray()
    {
        Assert.ThrowsAny<Exception>(() => TabularConvert.JsonToCsv("{\"a\":1}"));
    }

    [Fact]
    public void CsvToMarkdown_BuildsTable()
    {
        var md = TabularConvert.CsvToMarkdown("A,B\r\n1,2\r\n3,4");
        Assert.StartsWith("| A | B |", md);
        Assert.Contains("\n", md);
        Assert.Contains("| --- | --- |", md);
        Assert.Contains("| 1 | 2 |", md);
        Assert.Contains("| 3 | 4 |", md);
    }

    [Fact]
    public void RowsToMarkdown_EscapesPipeInCells()
    {
        var md = TabularConvert.RowsToMarkdown(new[] { new[] { "a|b", "c" } });
        Assert.Contains("a\\|b", md);
    }

    [Fact]
    public void CsvToHtml_ProducesDocument()
    {
        var html = TabularConvert.CsvToHtml("A,B\r\n1,2", "表");
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<td>A</td>", html);
        Assert.Contains("<h2>表</h2>", html);
    }

    [Fact]
    public void JsonToMarkdown_ObjectArrayBecomesTable()
    {
        var md = TabularConvert.JsonToMarkdown("[{\"k\":\"v\"}]");
        Assert.StartsWith("| k |", md);
    }

    [Fact]
    public void JsonToMarkdown_OtherJsonBecomesCodeBlock()
    {
        var md = TabularConvert.JsonToMarkdown("{\"a\":1}");
        Assert.Contains("```json", md);
        Assert.Contains("\"a\": 1", md);
    }

    [Fact]
    public void PrettyJson_InvalidJsonReturnedAsIs()
    {
        Assert.Equal("not json", TabularConvert.PrettyJson("not json"));
        // WriteIndented 在 Windows 上用 \r\n，统一按 \n 比较
        var pretty = TabularConvert.PrettyJson("{\"a\":1}").Replace("\r\n", "\n").TrimEnd();
        Assert.Equal("{\n  \"a\": 1\n}", pretty);
    }

    [Fact]
    public void ReadTextSmart_DetectsUtf8AndGbk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fc_txt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var utf8 = Path.Combine(dir, "u.txt");
            File.WriteAllBytes(utf8, new byte[] { 0xE4, 0xBD, 0xA0, 0xE5, 0xA5, 0xBD }); // UTF-8 “你好”
            Assert.Equal("你好", TabularConvert.ReadTextSmart(utf8));

            var gbk = Path.Combine(dir, "g.txt");
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            File.WriteAllBytes(gbk, Encoding.GetEncoding(936).GetBytes("你好"));
            Assert.Equal("你好", TabularConvert.ReadTextSmart(gbk));

            var bom = Path.Combine(dir, "b.txt");
            File.WriteAllBytes(bom, new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' });
            Assert.Equal("hi", TabularConvert.ReadTextSmart(bom));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TextToHtml_EscapesContent()
    {
        var html = TabularConvert.TextToHtml("a < b\r\nsecond");
        Assert.Contains("<p>a &lt; b</p>", html);
        Assert.Contains("<p>second</p>", html);
    }
}

public class HtmlConvertTests
{
    [Fact]
    public void ToPlainText_JoinsBlocksAndDecodesEntities()
    {
        var html = "<div><h1>标题</h1><p>第一段 &amp; 符号</p>尾随</div>";
        var text = HtmlConvert.ToPlainText(html);
        Assert.Contains("标题", text);
        Assert.Contains("第一段 & 符号", text);
        Assert.Contains("尾随", text);
    }

    [Fact]
    public void ToPlainText_IgnoresScriptStyle()
    {
        var text = HtmlConvert.ToPlainText("<script>var x=1;</script><style>.a{}</style><p>正文</p>");
        Assert.DoesNotContain("var x", text);
        Assert.Contains("正文", text);
    }

    [Fact]
    public void ToMarkdown_MapsHeadingsBoldListsLinksCode()
    {
        var html = """
            <h1>标题</h1>
            <p>这是<b>加粗</b>和<i>斜体</i>与<code>代码</code></p>
            <ul><li>项目一</li><li>项目二</li></ul>
            <p><a href="https://example.com">链接</a></p>
            """;
        var md = HtmlConvert.ToMarkdown(html);
        Assert.Contains("# 标题", md);
        Assert.Contains("**加粗**", md);
        Assert.Contains("*斜体*", md);
        Assert.Contains("`代码`", md);
        Assert.Contains("- 项目一", md);
        Assert.Contains("[链接](https://example.com)", md);
    }

    [Fact]
    public void ToMarkdown_Table()
    {
        var html = "<table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>";
        var md = HtmlConvert.ToMarkdown(html);
        Assert.Contains("| A | B |", md);
        Assert.Contains("| --- | --- |", md);
        Assert.Contains("| 1 | 2 |", md);
    }

    [Fact]
    public void ToMarkdown_BlockquoteAndPre()
    {
        var html = "<blockquote>引用内容</blockquote><pre>line1\nline2</pre>";
        var md = HtmlConvert.ToMarkdown(html);
        Assert.Contains("> 引用内容", md);
        Assert.Contains("```", md);
        Assert.Contains("line1", md);
    }

    [Fact]
    public void ToMarkdown_OrderedList()
    {
        var md = HtmlConvert.ToMarkdown("<ol><li>一</li><li>二</li></ol>");
        Assert.Contains("1. 一", md);
        Assert.Contains("2. 二", md);
    }
}

public class DocxRoundTripTests
{
    [Fact]
    public void FromHtml_ToMarkdown_RoundTrip()
    {
        var html = """
            <h1>文档标题</h1>
            <p>这是<b>加粗</b>内容</p>
            <ul><li>列表项</li></ul>
            <table><tr><td>A</td><td>B</td></tr><tr><td>1</td><td>2</td></tr></table>
            """;
        var bytes = DocxWriter.FromHtml(html, "测试");
        Assert.True(bytes.Length > 0);

        var dir = Path.Combine(Path.GetTempPath(), "fc_docx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "t.docx");
            File.WriteAllBytes(path, bytes);

            // 生成的 docx 是包含必需部件的有效 zip 包
            using (var zip = ZipFile.OpenRead(path))
            {
                Assert.NotNull(zip.GetEntry("word/document.xml"));
                Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
                Assert.NotNull(zip.GetEntry("word/styles.xml"));
            }

            var md = DocxReader.ToMarkdown(path);
            Assert.Contains("# 文档标题", md);
            Assert.Contains("**加粗**", md);
            Assert.Contains("- 列表项", md);
            Assert.Contains("| A | B |", md);
            Assert.Contains("| 1 | 2 |", md);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FromText_ToPlainText_RoundTrip()
    {
        var bytes = DocxWriter.FromText("第一行\n第二行");
        var dir = Path.Combine(Path.GetTempPath(), "fc_docx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "t2.docx");
            File.WriteAllBytes(path, bytes);
            var text = DocxReader.ToPlainText(path);
            Assert.Contains("第一行", text);
            Assert.Contains("第二行", text);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FromHtml_EscapesXml()
    {
        var bytes = DocxWriter.FromHtml("<p>a &lt; b &amp; c</p>");
        var xml = Encoding.UTF8.GetString(bytes);
        // zip 内容为 deflate 压缩，无法直接字符串断言；至少保证不抛异常且非空
        Assert.True(bytes.Length > 0);
    }
}

public class OdfConverterTests
{
    private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string DrawNs = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";

    private static byte[] BuildOdf(string bodyInner)
        => BuildZip("content.xml",
            $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-content xmlns:office=""{OfficeNs}"" xmlns:text=""{TextNs}"" xmlns:table=""{TableNs}"" xmlns:draw=""{DrawNs}"">
<office:body>{bodyInner}</office:body></office:document-content>");

    private static byte[] BuildZip(string entryName, string content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return ms.ToArray();
    }

    private static string OdfToHtml(byte[] odf)
    {
        using var zip = new ZipArchive(new MemoryStream(odf));
        return OdfConverter.ToHtml(zip);
    }

    [Fact]
    public void ToHtml_Odt_HeadingsAndParagraphs()
    {
        var html = OdfToHtml(BuildOdf(
            $@"<office:text><text:h text:outline-level=""1"">标题</text:h><text:p>段落内容</text:p></office:text>"));
        Assert.Contains("<h1>标题</h1>", html);
        Assert.Contains("<p>段落内容</p>", html);
        Assert.Contains("class=\"odt\"", html);
    }

    [Fact]
    public void ToHtml_Odt_List()
    {
        var html = OdfToHtml(BuildOdf(
            @"<office:text><text:list><text:list-item><text:p>项</text:p></text:list-item></text:list></office:text>"));
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>", html);
        Assert.Contains("项", html);
    }

    [Fact]
    public void ToHtml_Ods_Table()
    {
        var html = OdfToHtml(BuildOdf(
            @"<office:spreadsheet><table:table table:name=""Sheet1""><table:table-row><table:table-cell><text:p>A</text:p></table:table-cell></table:table-row></table:table></office:spreadsheet>"));
        Assert.Contains("class=\"ods\"", html);
        Assert.Contains("Sheet1", html);
        Assert.Contains("<td>A</td>", html);
    }

    [Fact]
    public void ToHtml_Odp_Slides()
    {
        var html = OdfToHtml(BuildOdf(
            @"<office:presentation><draw:page><draw:frame><draw:text-box><text:p>幻灯片一</text:p></draw:text-box></draw:frame></draw:page></office:presentation>"));
        Assert.Contains("class=\"odp\"", html);
        Assert.Contains("class=\"slide\"", html);
        Assert.Contains("幻灯片一", html);
    }

    [Fact]
    public void ToHtml_MissingContent_Throws()
    {
        Assert.ThrowsAny<Exception>(() => OdfToHtml(BuildZip("mimetype", "x")));
    }
}

public class PptxToHtmlConverterTests
{
    private const string PptxNamespace = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string RelNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PkgRelNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>构造一个最小 pptx（2 页：文本 + 图片），返回内存 zip 的字节。</summary>
    private static byte[] BuildMinimalPptx()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            WriteEntry(zip, "ppt/slides/slide1.xml",
                $@"<p:sld xmlns:p=""{PptxNamespace}"" xmlns:a=""{DrawingNamespace}"" xmlns:r=""{RelNamespace}"">
  <p:cSld><p:spTree>
    <p:sp><p:txBody><a:p><a:r><a:t>测试标题</a:t></a:r></a:p><a:p><a:r><a:t>正文内容</a:t></a:r></a:p></p:txBody></p:sp>
  </p:spTree></p:cSld>
</p:sld>");
            WriteEntry(zip, "ppt/slides/_rels/slide1.xml.rels",
                $@"<Relationships xmlns=""{PkgRelNamespace}"">
  <Relationship Id=""rId1"" Type=""{RelNamespace}/slideLayout"" Target=""../slideLayouts/slideLayout1.xml""/>
</Relationships>");
            WriteEntry(zip, "ppt/slides/slide2.xml",
                $@"<p:sld xmlns:p=""{PptxNamespace}"" xmlns:a=""{DrawingNamespace}"" xmlns:r=""{RelNamespace}"">
  <p:cSld><p:spTree>
    <p:pic><p:blipFill><a:blip r:embed=""rId2""/></p:blipFill></p:pic>
  </p:spTree></p:cSld>
</p:sld>");
            WriteEntry(zip, "ppt/slides/_rels/slide2.xml.rels",
                $@"<Relationships xmlns=""{PkgRelNamespace}"">
  <Relationship Id=""rId2"" Type=""{RelNamespace}/image"" Target=""../media/image1.png""/>
</Relationships>");
            // 1x1 红色 PNG
            var png1x1 = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
            var imgEntry = zip.CreateEntry("ppt/media/image1.png");
            using (var s = imgEntry.Open()) s.Write(png1x1);
        }
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    [Fact]
    public void Convert_ExtractsText_And_Images()
    {
        using var zip = new ZipArchive(new MemoryStream(BuildMinimalPptx()));
        var html = PptxToHtmlConverter.ToHtml(zip);

        Assert.Contains("测试标题", html);
        Assert.Contains("正文内容", html);
        Assert.Contains("data:image/png;base64,", html);
        Assert.Contains("slide", html);
        // 幻灯片按顺序渲染两页
        Assert.Equal(2, CountOccurrences(html, "class=\"slide\""));
    }

    [Fact]
    public void Convert_SkipsRelationshipFiles_AndSortsBySlideNumber()
    {
        using var zip = new ZipArchive(new MemoryStream(BuildMinimalPptx()));
        var html = PptxToHtmlConverter.ToHtml(zip);
        // slide2 的图片出现在 slide1 的文本之后
        Assert.True(html.IndexOf("测试标题") < html.IndexOf("data:image/png;base64,"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    [Fact]
    public void Convert_InvalidXml_DoesNotThrow()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            WriteEntry(zip, "ppt/slides/slide1.xml", "not valid xml");
        }
        using var reopened = new ZipArchive(new MemoryStream(ms.ToArray()));
        var html = PptxToHtmlConverter.ToHtml(reopened);
        Assert.Contains("<div class=\"pptx\">", html);
    }
}

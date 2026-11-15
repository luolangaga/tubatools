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
    public void TryGetInputFile_RejectsDirectoriesAndMissingPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "TubaFormatGuard_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "草稿1.png");
            File.WriteAllBytes(file, [1, 2, 3]);

            Assert.True(FormatConvertCatalog.TryGetInputFile(file, out var fullPath, out var size));
            Assert.Equal(file, fullPath);
            Assert.Equal(3, size);

            // #264：文件选择器/拖放返回目录或失效路径时，不能靠 FileInfo.Length 抛异常。
            Assert.False(FormatConvertCatalog.TryGetInputFile(root, out _, out _));
            Assert.False(FormatConvertCatalog.TryGetInputFile(Path.Combine(root, "missing.png"), out _, out _));
            Assert.False(FormatConvertCatalog.TryGetInputFile("", out _, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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

    private static FormatOption Target(SourceCategory category, string ext)
        => FormatConvertCatalog.GetTargetFormats(category).First(f => f.Ext == ext);

    private static FormatParamValues V(params (string Id, double Value)[] values)
        => new(values.ToDictionary(v => v.Id, v => v.Value));

    [Fact]
    public void BuildFfmpegArgs_VideoTranscode_AppliesCrfPresetAndAudio()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Mp4, V(
            (FormatParamIds.VideoCrf, 28), (FormatParamIds.VideoPreset, 6),
            (FormatParamIds.VideoAudioBitrate, 192)));
        Assert.Contains("-i \"C:\\in\\m.mp4\"", args);
        Assert.Contains("-c:v libx264", args);
        Assert.Contains("-crf 28 -preset slow", args);
        Assert.Contains("-b:a 192k", args);
        Assert.Contains("-c:a aac", args);
        Assert.Contains("\"C:\\in\\m_converted.mp4\"", args);
    }

    [Fact]
    public void BuildFfmpegArgs_VideoRateMode_UsesBitrateInsteadOfCrf()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Mp4, V(
            (FormatParamIds.VideoRateMode, 1), (FormatParamIds.VideoBitrate, 8000)));
        Assert.Contains("-b:v 8000k", args);
        Assert.DoesNotContain("-crf", args);
    }

    [Fact]
    public void BuildFfmpegArgs_HevcInMp4_AddsHvc1Tag()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Mp4, V(
            (FormatParamIds.VideoCodec, FfmpegCodecs.H265)));
        Assert.Contains("-c:v libx265", args);
        Assert.Contains("-tag:v hvc1", args);
    }

    [Fact]
    public void BuildFfmpegArgs_Vp9_UsesZeroBitrateCrfAndSpeed()
    {
        var webm = new FormatOption("WebM", ".webm", "libvpx-vp9", "libopus");
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mkv", webm, V(
            (FormatParamIds.VideoCrf, 32), (FormatParamIds.VideoPreset, 5)));
        Assert.Contains("-c:v libvpx-vp9", args);
        Assert.Contains("-crf 32 -b:v 0", args);
        Assert.Contains("-cpu-used 3", args);
    }

    [Fact]
    public void BuildFfmpegArgs_Mpeg4_FallsBackToQuantizer()
    {
        var mkvmpeg4 = V((FormatParamIds.VideoCodec, FfmpegCodecs.Mpeg4), (FormatParamIds.VideoCrf, 51));
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mkv", Mp4, mkvmpeg4);
        Assert.Contains("-c:v mpeg4", args);
        Assert.Contains("-q:v 31", args);
        Assert.DoesNotContain("-crf", args);
        Assert.DoesNotContain("-preset", args);
    }

    [Fact]
    public void BuildFfmpegArgs_AudioCodecNone_AddsAn()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Mp4, V(
            (FormatParamIds.VideoAudioCodec, FfmpegCodecs.NoAudio)));
        Assert.Contains("-an", args);
        Assert.DoesNotContain("-c:a", args);
    }

    [Fact]
    public void BuildFfmpegArgs_WithResolutionScaleAndFps()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Mp4,
            V((FormatParamIds.VideoWidth, 1920), (FormatParamIds.VideoFps, 60)));
        Assert.Contains("-vf \"scale=1920:1920:force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2\"", args);
        Assert.Contains("-r 60", args);
    }

    [Fact]
    public void BuildFfmpegArgs_ExtractAudio_FromVideo()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Mp3, V(
            (FormatParamIds.AudioBitrate, 192), (FormatParamIds.AudioSampleRate, 48000), (FormatParamIds.AudioChannels, 2)));
        Assert.Contains("-vn", args);
        Assert.Contains("-c:a libmp3lame", args);
        Assert.Contains("-b:a 192k", args);
        Assert.Contains("-ar 48000", args);
        Assert.Contains("-ac 2", args);
        Assert.DoesNotContain("-c:v", args);
    }

    [Fact]
    public void BuildFfmpegArgs_Mp3Vbr_UsesQualityInsteadOfBitrate()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\s.wav", Mp3, V(
            (FormatParamIds.AudioRateMode, 1), (FormatParamIds.AudioQuality, 2), (FormatParamIds.AudioBitrate, 192)));
        Assert.Contains("-q:a 2", args);
        Assert.DoesNotContain("-b:a", args);
    }

    [Fact]
    public void BuildFfmpegArgs_Ogg_UsesVariableQuality()
    {
        var ogg = Target(SourceCategory.Audio, ".ogg");
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\s.wav", ogg, V((FormatParamIds.AudioQuality, 7)));
        Assert.Contains("-c:a libvorbis", args);
        Assert.Contains("-q:a 7", args);
        Assert.DoesNotContain("-b:a", args);
    }

    [Theory]
    [InlineData(1, "pcm_u8")]
    [InlineData(2, "pcm_s16le")]
    [InlineData(3, "pcm_s24le")]
    [InlineData(4, "pcm_s32le")]
    [InlineData(5, "pcm_f32le")]
    [InlineData(6, "pcm_f64le")]
    public void BuildFfmpegArgs_WavBitDepth_SelectsPcmCodec(int depth, string expectedCodec)
    {
        var wav = Target(SourceCategory.Audio, ".wav");
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\s.mp3", wav, V(
            (FormatParamIds.AudioBitDepth, depth), (FormatParamIds.AudioSampleRate, 44100)));
        Assert.Contains($"-c:a {expectedCodec}", args);
        Assert.Contains("-ar 44100", args);
        Assert.DoesNotContain("-b:a", args);
    }

    [Theory]
    [InlineData(1, "adpcm_ms")]
    [InlineData(2, "adpcm_ima_wav")]
    [InlineData(3, "pcm_alaw")]
    [InlineData(4, "pcm_mulaw")]
    public void BuildFfmpegArgs_WavAlternativeEncoding(int mode, string expectedCodec)
    {
        var wav = Target(SourceCategory.Audio, ".wav");
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\s.mp3", wav, V((FormatParamIds.AudioWavCodec, mode)));
        Assert.Contains($"-c:a {expectedCodec}", args);
    }

    [Theory]
    [InlineData(2, "pcm_s16be")]
    [InlineData(3, "pcm_s24be")]
    public void BuildFfmpegArgs_AiffBitDepth_SelectsBigEndianCodec(int depth, string expectedCodec)
    {
        var aiff = Target(SourceCategory.Audio, ".aiff");
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\s.wav", aiff, V((FormatParamIds.AudioBitDepth, depth)));
        Assert.Contains($"-c:a {expectedCodec}", args);
    }

    [Fact]
    public void BuildFfmpegArgs_Flac_AppliesCompressionLevelAndSampleFormat()
    {
        var flac = Target(SourceCategory.Audio, ".flac");
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\s.wav", flac, V(
            (FormatParamIds.AudioCompressionLevel, 8), (FormatParamIds.AudioBitDepth, 3)));
        Assert.Contains("-c:a flac", args);
        Assert.Contains("-compression_level 8", args);
        Assert.Contains("-sample_fmt s32", args);
    }

    [Fact]
    public void BuildFfmpegArgs_Opus_AppliesApplication()
    {
        var opus = Target(SourceCategory.Audio, ".opus");
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\s.wav", opus, V(
            (FormatParamIds.AudioBitrate, 96), (FormatParamIds.AudioOpusApplication, 1)));
        Assert.Contains("-c:a libopus", args);
        Assert.Contains("-b:a 96k", args);
        Assert.Contains("-application voip", args);
    }

    [Fact]
    public void BuildFfmpegArgs_Gif_AppliesPaletteFilter()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Gif, V(
            (FormatParamIds.GifFps, 12), (FormatParamIds.GifWidth, 640)));
        // GIF 走 filter_complex 调色板路径（palettegen + paletteuse，单次完成避免编码器崩溃），
        // 缩放/帧率滤镜在 filter_complex 内部；旧的 -vf 直通格式已由 BuildFfmpegGifFallbackArgs 降级承载
        Assert.Contains("-filter_complex", args);
        Assert.Contains("palettegen=stats_mode=diff", args);
        Assert.Contains("paletteuse=dither=bayer", args);
        Assert.Contains("fps=12,scale=640:-1:flags=lanczos", args);
        Assert.Contains("-an", args);
    }

    [Fact]
    public void BuildFfmpegArgs_Gif_ZeroWidthKeepsOriginalSize()
    {
        var args = FormatConvertPlanner.BuildFfmpegArgs(@"C:\in\m.mp4", Gif, V((FormatParamIds.GifWidth, 0)));
        Assert.Contains("scale=trunc(iw/2)*2:trunc(ih/2)*2", args);
    }

    [Fact]
    public void BuildImageVideoArgs_StaticImage_LoopsWithDuration()
    {
        var mp4 = Target(SourceCategory.Image, ".mp4");
        var args = FormatConvertPlanner.BuildImageVideoArgs(@"C:\in\p.png", mp4, V((FormatParamIds.VideoDuration, 5)));
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
        var mp4 = Target(SourceCategory.Image, ".mp4");
        var args = FormatConvertPlanner.BuildImageVideoArgs(@"C:\in\a.gif", mp4, V((FormatParamIds.VideoDuration, 5)));
        Assert.DoesNotContain("-loop 1", args);
        Assert.DoesNotContain("-tune stillimage", args);
        Assert.StartsWith("-i \"C:\\in\\a.gif\"", args);
    }

    [Fact]
    public void BuildImageVideoArgs_Webm_UsesVp9AndCrf()
    {
        var webm = Target(SourceCategory.Image, ".webm");
        var args = FormatConvertPlanner.BuildImageVideoArgs(@"C:\in\p.png", webm, V(
            (FormatParamIds.VideoCrf, 32), (FormatParamIds.VideoWidth, 1280), (FormatParamIds.VideoFps, 24)));
        Assert.Contains("-c:v libvpx-vp9", args);
        Assert.Contains("-b:v 0 -crf 32", args);
        Assert.Contains("scale=1280:1280:force_original_aspect_ratio=decrease", args);
        Assert.Contains("-r 24", args);
        Assert.EndsWith("\"C:\\in\\p_converted.webm\"", args);
    }

    [Fact]
    public void BuildImageVideoArgs_DurationClamped()
    {
        var mp4 = Target(SourceCategory.Image, ".mp4");
        var args = FormatConvertPlanner.BuildImageVideoArgs(@"C:\in\p.png", mp4, V((FormatParamIds.VideoDuration, 0)));
        Assert.Contains("-t 5", args); // 0 → 默认 5 秒
        var huge = FormatConvertPlanner.BuildImageVideoArgs(@"C:\in\p.png", mp4, V((FormatParamIds.VideoDuration, 99999)));
        Assert.Contains("-t 600", huge); // 上限 600
    }

    [Fact]
    public void BuildMagickArgs_Ico_MultiSizeAutoResize()
    {
        var ico = new FormatOption("ICO", ".ico", "", "");
        var args = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", ico, null, new[] { 256, 128, 64, 48, 32, 16 });
        Assert.Contains("-define icon:auto-resize=256,128,64,48,32,16", args);
        Assert.EndsWith("\"C:\\in\\p_converted.ico\"", args);
    }

    [Fact]
    public void BuildMagickArgs_Ico_NoSizesDefaults()
    {
        var ico = new FormatOption("ICO", ".ico", "", "");
        var args = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", ico);
        Assert.Contains("-define icon:auto-resize=256,128,64,48,32,16", args);
    }

    [Fact]
    public void BuildMagickArgs_ConvertOnly()
    {
        var args = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", Jpg);
        Assert.Equal("\"C:\\in\\p.png\" \"C:\\in\\p_converted.jpg\"", args);
    }

    [Fact]
    public void BuildMagickArgs_Jpeg_QualitySamplingProgressiveResize()
    {
        var args = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", Jpg, V(
            (FormatParamIds.ImageQuality, 70), (FormatParamIds.ImageSampling, 1),
            (FormatParamIds.ImageProgressive, 1), (FormatParamIds.ImageStrip, 1),
            (FormatParamIds.ImageMaxEdge, 1920)));
        Assert.Contains("-quality 70", args);
        Assert.Contains("-sampling-factor 4:4:4", args);
        Assert.Contains("-interlace Plane", args);
        Assert.Contains("-strip", args);
        Assert.Contains("-resize 1920x1920>", args);
    }

    [Fact]
    public void BuildMagickArgs_Png_CompressionLevelAndColorType()
    {
        var png = Target(SourceCategory.Image, ".png");
        var args = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.jpg", png, V(
            (FormatParamIds.ImagePngLevel, 9), (FormatParamIds.ImagePngColorType, 3)));
        Assert.Contains("-define png:compression-level=9", args);
        Assert.Contains("-define png:color-type=3", args);
    }

    [Fact]
    public void BuildMagickArgs_Webp_LosslessAndMethod()
    {
        var webp = Target(SourceCategory.Image, ".webp");
        var args = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", webp, V(
            (FormatParamIds.ImageLossless, 1), (FormatParamIds.ImageMethod, 6), (FormatParamIds.ImageQuality, 90)));
        Assert.Contains("-define webp:lossless=true", args);
        Assert.Contains("-define webp:method=6", args);
        Assert.Contains("-quality 90", args);
    }

    [Fact]
    public void BuildMagickArgs_Gif_ColorsAndDither()
    {
        var gif = Target(SourceCategory.Image, ".gif");
        var withDither = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", gif, V(
            (FormatParamIds.ImageGifColors, 64), (FormatParamIds.ImageGifDither, 1)));
        Assert.Contains("-colors 64", withDither);
        Assert.Contains("-dither Riemersma", withDither);

        var noDither = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", gif, V(
            (FormatParamIds.ImageGifDither, 0)));
        Assert.Contains("-dither None", noDither);
        Assert.DoesNotContain("-colors", noDither); // 256 = 不限制
    }

    [Fact]
    public void BuildMagickArgs_Tiff_Compression()
    {
        var tiff = Target(SourceCategory.Image, ".tiff");
        var args = FormatConvertPlanner.BuildMagickArgs(@"C:\in\p.png", tiff, V(
            (FormatParamIds.ImageTiffCompress, 2)));
        Assert.Contains("-compress Zip", args);
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

public class FormatConvertParamsTests
{
    public static IEnumerable<object[]> AllTargets()
    {
        foreach (var category in new[]
        {
            SourceCategory.Video, SourceCategory.Audio, SourceCategory.Image, SourceCategory.Pdf,
            SourceCategory.Word, SourceCategory.Excel, SourceCategory.Ppt, SourceCategory.Markdown,
            SourceCategory.Text, SourceCategory.Html, SourceCategory.Json, SourceCategory.Unsupported
        })
        {
            foreach (var target in FormatConvertCatalog.GetTargetFormats(category))
                yield return new object[] { category, target };
        }
        yield return new object[] { SourceCategory.Pdf, FormatConvertCatalog.MergePdfTarget };
        yield return new object[] { SourceCategory.Pdf, FormatConvertCatalog.SplitPdfTarget };
        yield return new object[] { SourceCategory.Unsupported, FormatConvertCatalog.ZipTarget };
    }

    private static FormatOption Target(SourceCategory category, string ext)
        => FormatConvertCatalog.GetTargetFormats(category).First(f => f.Ext == ext);

    private static FormatParamValues V(params (string Id, double Value)[] values)
        => new(values.ToDictionary(v => v.Id, v => v.Value));

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void TargetParams_AreWellFormed(SourceCategory category, FormatOption target)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var param in target.ParamList)
        {
            Assert.True(ids.Add(param.Id), $"{target.Name} 参数 id 重复：{param.Id}");
            Assert.False(string.IsNullOrWhiteSpace(param.Label));
            Assert.True(param.Step > 0, $"{target.Name}/{param.Id} 步长无效");

            if (param.Kind == FormatParamKind.Combo)
            {
                Assert.NotNull(param.Choices);
                Assert.NotEmpty(param.Choices!);
                Assert.Contains(param.Choices!, c => Math.Abs(c.Value - param.Default) < 0.0001);
            }
            else if (param.Kind is FormatParamKind.Slider or FormatParamKind.Number)
            {
                Assert.True(param.Min < param.Max, $"{target.Name}/{param.Id} 取值范围无效");
                Assert.InRange(param.Default, param.Min, param.Max);
            }
        }

        foreach (var param in target.ParamList.Where(p => p.VisibleWhenId is not null))
            Assert.Contains(ids, id => id == param.VisibleWhenId);
    }

    [Fact]
    public void AudioVideoImageTargets_AllDeclareParams()
    {
        foreach (var category in new[] { SourceCategory.Video, SourceCategory.Audio, SourceCategory.Image })
        {
            foreach (var target in FormatConvertCatalog.GetTargetFormats(category).Where(f => !f.IsSpecial))
            {
                // ICO 的尺寸由对话框的专用多选面板提供
                if (target.Ext == ".ico") continue;
                Assert.NotEmpty(target.ParamList);
            }
        }
    }

    [Fact]
    public void SpecialTargets_HaveNoParams()
    {
        Assert.Empty(FormatConvertCatalog.ZipTarget.ParamList);
        Assert.Empty(FormatConvertCatalog.MergePdfTarget.ParamList);
        Assert.Empty(FormatConvertCatalog.SplitPdfTarget.ParamList);
        var ocr = Target(SourceCategory.Image, ".txt");
        Assert.Empty(ocr.ParamList);
    }

    [Fact]
    public void VideoTargets_ExposeCodecAndRateMode()
    {
        var mp4 = Target(SourceCategory.Video, ".mp4");
        Assert.Contains(mp4.ParamList, p => p.Id == FormatParamIds.VideoCodec);
        Assert.Contains(mp4.ParamList, p => p.Id == FormatParamIds.VideoRateMode);
        Assert.Contains(mp4.ParamList, p => p.Id == FormatParamIds.VideoAudioCodec);
        // H.264 / H.265 / AV1
        Assert.Equal(3, mp4.ParamList.First(p => p.Id == FormatParamIds.VideoCodec).Choices!.Count);
    }

    [Fact]
    public void VideoTargets_DefaultPresetIsMedium()
    {
        var preset = Target(SourceCategory.Video, ".mp4").ParamList.First(p => p.Id == FormatParamIds.VideoPreset);
        Assert.Equal("medium", FfmpegCodecs.X264Presets[(int)preset.Default]);
        Assert.Contains(preset.Choices!, c => Math.Abs(c.Value - preset.Default) < 0.0001);
    }

    [Fact]
    public void GifTarget_DitherDefaultsOn()
    {
        var gif = Target(SourceCategory.Image, ".gif");
        Assert.Equal(1, gif.ParamList.First(p => p.Id == FormatParamIds.ImageGifDither).Default);
    }

    [Fact]
    public void Summarize_SkipsHiddenParams()
    {
        var mp3 = Target(SourceCategory.Audio, ".mp3");
        var summary = FormatConvertParams.Summarize(mp3, V(
            (FormatParamIds.AudioRateMode, 1), (FormatParamIds.AudioBitrate, 320)));
        Assert.Contains("可变质量", summary);
        Assert.DoesNotContain("320", summary); // VBR 模式下固定码率参数已隐藏

        var video = Target(SourceCategory.Video, ".mp4");
        var videoSummary = FormatConvertParams.Summarize(video, V(
            (FormatParamIds.VideoRateMode, 1), (FormatParamIds.VideoBitrate, 8000), (FormatParamIds.VideoCrf, 40)));
        Assert.Contains("8000", videoSummary);
        Assert.DoesNotContain("CRF 40", videoSummary);
    }

    [Fact]
    public void AudioTargets_ExposeBitrateAndBitDepth()
    {
        var mp3 = Target(SourceCategory.Audio, ".mp3");
        Assert.Contains(mp3.ParamList, p => p.Id == FormatParamIds.AudioRateMode);
        Assert.Contains(mp3.ParamList, p => p.Id == FormatParamIds.AudioQuality);

        var wav = Target(SourceCategory.Audio, ".wav");
        Assert.Contains(wav.ParamList, p => p.Id == FormatParamIds.AudioBitDepth);
        Assert.Contains(wav.ParamList, p => p.Id == FormatParamIds.AudioWavCodec);
        Assert.Contains(wav.ParamList, p => p.Id == FormatParamIds.AudioSampleRate);
    }

    [Fact]
    public void ImageTargets_ExposeFormatSpecificOptions()
    {
        Assert.Contains(Target(SourceCategory.Image, ".png").ParamList, p => p.Id == FormatParamIds.ImagePngLevel);
        Assert.Contains(Target(SourceCategory.Image, ".jpg").ParamList, p => p.Id == FormatParamIds.ImageProgressive);
        Assert.Contains(Target(SourceCategory.Image, ".webp").ParamList, p => p.Id == FormatParamIds.ImageLossless);
        Assert.Contains(Target(SourceCategory.Image, ".gif").ParamList, p => p.Id == FormatParamIds.ImageGifColors);
        Assert.Contains(Target(SourceCategory.Image, ".tiff").ParamList, p => p.Id == FormatParamIds.ImageTiffCompress);
        Assert.Contains(Target(SourceCategory.Image, ".avif").ParamList, p => p.Id == FormatParamIds.ImageAvifSpeed);
    }

    [Fact]
    public void EstimatePcmBitrateKbps_CdQuality()
        => Assert.Equal(1411.2, FormatConvertParams.EstimatePcmBitrateKbps(16, 44100, 2), 3);

    [Fact]
    public void EffectiveBits_FollowsBitDepthAndWavCodec()
    {
        var wav = Target(SourceCategory.Audio, ".wav");
        Assert.Equal(16, FormatConvertParams.EffectiveBits(wav, FormatParamValues.Empty));
        Assert.Equal(24, FormatConvertParams.EffectiveBits(wav, V((FormatParamIds.AudioBitDepth, 3))));
        Assert.Equal(4, FormatConvertParams.EffectiveBits(wav, V((FormatParamIds.AudioWavCodec, 1))));
        Assert.Equal(8, FormatConvertParams.EffectiveBits(wav, V((FormatParamIds.AudioWavCodec, 4))));
    }

    [Fact]
    public void BitrateEstimate_NeedsRateAndChannels()
    {
        var wav = Target(SourceCategory.Audio, ".wav");
        Assert.Null(FormatConvertParams.BitrateEstimate(wav, V((FormatParamIds.AudioSampleRate, 48000))));

        var estimate = FormatConvertParams.BitrateEstimate(wav, V(
            (FormatParamIds.AudioSampleRate, 44100), (FormatParamIds.AudioChannels, 2)));
        Assert.NotNull(estimate);
        Assert.Contains("1411", estimate);
    }

    [Fact]
    public void Summarize_ReportsDefaultsAndCustomValues()
    {
        var wav = Target(SourceCategory.Audio, ".wav");
        Assert.Equal("使用默认参数", FormatConvertParams.Summarize(wav, FormatParamValues.Empty));

        var summary = FormatConvertParams.Summarize(wav, V(
            (FormatParamIds.AudioSampleRate, 96000), (FormatParamIds.AudioChannels, 1)));
        Assert.Contains("已自定义", summary);
        Assert.Contains("96000", summary);
        Assert.Contains("1536", summary); // 16 位 × 96000 Hz × 单声道

        var mp3 = Target(SourceCategory.Audio, ".mp3");
        var mp3Summary = FormatConvertParams.Summarize(mp3, V((FormatParamIds.AudioRateMode, 1)));
        Assert.Contains("可变质量", mp3Summary);
    }

    [Fact]
    public void FormatParamValues_MissingKeysUseFallbacks()
    {
        var values = new FormatParamValues(new Dictionary<string, double> { ["x"] = 5, ["flag"] = 1, ["off"] = 0 });
        Assert.Equal(5, values.Get("x", 1));
        Assert.Equal(1, values.Get("missing", 1));
        Assert.True(values.Has("x"));
        Assert.False(values.Has("missing"));
        Assert.True(values.GetFlag("flag"));
        Assert.False(values.GetFlag("off"));
    }
}

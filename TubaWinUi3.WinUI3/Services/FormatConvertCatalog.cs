namespace TubaWinUi3.Services;

/// <summary>源文件类别。</summary>
public enum SourceCategory
{
    Video,
    Audio,
    Image,
    Pdf,
    Word,
    Excel,
    Ppt,
    Markdown,
    Text,
    Html,
    Json,
    Unsupported
}

/// <summary>转换使用的引擎。</summary>
public enum ConvertEngine
{
    Ffmpeg,
    Magick,
    DocEngine,
    Ocr,
    OfficeInterop,
    OfficeCli
}

/// <summary>非常规格式输出的特殊操作（由页面/文档服务专门调度）。</summary>
public enum ConvertSpecial
{
    None,
    OcrText,        // 图片 / 扫描版 PDF → TXT（Windows AI / OCR）
    MergePdf,       // 多份 PDF 合并为一个
    SplitPdf,       // 单份 PDF 拆分为单页 PDF
    ZipArchive,     // 任意文件 / PDF 页面图片 → ZIP 压缩包（Tag 为内层图片格式）
    PdfExcel        // 文字型 PDF → Excel（表格提取）
}

/// <summary>目标格式选项（沿用原视频处理的 FormatOption 结构）。</summary>
public sealed record FormatOption(string Name, string Ext, string DefaultVCodec, string DefaultACodec,
    ConvertSpecial Special = ConvertSpecial.None, string? Tag = null, IReadOnlyList<FormatParam>? Params = null)
{
    /// <summary>纯音频输出（如视频提取 MP3、音频转 MP3）。</summary>
    public bool IsAudioOnly => DefaultVCodec == "" && DefaultACodec != "";

    /// <summary>特殊操作（合并/拆分/OCR/ZIP 等非普通格式输出）。</summary>
    public bool IsSpecial => Special != ConvertSpecial.None;

    /// <summary>该目标格式的专属参数（页面据此生成滑块 / 输入框 / 下拉框）。</summary>
    public IReadOnlyList<FormatParam> ParamList => Params ?? Array.Empty<FormatParam>();
}

/// <summary>
/// 格式转换支持表：扩展名 → 类别、类别 → 目标格式、引擎归属。
/// 纯逻辑，可单元测试。
/// </summary>
public static class FormatConvertCatalog
{
    /// <summary>任意文件 → ZIP 压缩包（各类别通用，页面按特殊操作调度）。</summary>
    public static readonly FormatOption ZipTarget = new("ZIP 压缩包", ".zip", "", "", ConvertSpecial.ZipArchive);

    private static readonly Dictionary<string, SourceCategory> ExtMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // 视频
        ["mp4"] = SourceCategory.Video, ["mkv"] = SourceCategory.Video, ["avi"] = SourceCategory.Video,
        ["mov"] = SourceCategory.Video, ["wmv"] = SourceCategory.Video, ["flv"] = SourceCategory.Video,
        ["webm"] = SourceCategory.Video, ["m4v"] = SourceCategory.Video, ["3gp"] = SourceCategory.Video,
        ["ts"] = SourceCategory.Video, ["mts"] = SourceCategory.Video, ["m2ts"] = SourceCategory.Video,
        ["vob"] = SourceCategory.Video, ["ogv"] = SourceCategory.Video, ["rm"] = SourceCategory.Video,
        ["rmvb"] = SourceCategory.Video, ["mpg"] = SourceCategory.Video, ["mpeg"] = SourceCategory.Video,
        ["asf"] = SourceCategory.Video, ["dv"] = SourceCategory.Video, ["f4v"] = SourceCategory.Video,
        // 音频
        ["mp3"] = SourceCategory.Audio, ["wav"] = SourceCategory.Audio, ["flac"] = SourceCategory.Audio,
        ["aac"] = SourceCategory.Audio, ["m4a"] = SourceCategory.Audio, ["ogg"] = SourceCategory.Audio,
        ["opus"] = SourceCategory.Audio, ["wma"] = SourceCategory.Audio, ["aiff"] = SourceCategory.Audio,
        ["aif"] = SourceCategory.Audio, ["ape"] = SourceCategory.Audio, ["wv"] = SourceCategory.Audio,
        ["ac3"] = SourceCategory.Audio, ["amr"] = SourceCategory.Audio, ["mka"] = SourceCategory.Audio,
        // 图片
        ["png"] = SourceCategory.Image, ["jpg"] = SourceCategory.Image, ["jpeg"] = SourceCategory.Image,
        ["webp"] = SourceCategory.Image, ["gif"] = SourceCategory.Image, ["bmp"] = SourceCategory.Image,
        ["tif"] = SourceCategory.Image, ["tiff"] = SourceCategory.Image, ["ico"] = SourceCategory.Image,
        ["tga"] = SourceCategory.Image, ["jfif"] = SourceCategory.Image,
        ["heic"] = SourceCategory.Image, ["avif"] = SourceCategory.Image,
        // PDF
        ["pdf"] = SourceCategory.Pdf,
        // Word 文档（doc/wps 需 Office/WPS 互联，rtf/odt 由内置引擎解析）
        ["doc"] = SourceCategory.Word, ["docx"] = SourceCategory.Word, ["wps"] = SourceCategory.Word,
        ["rtf"] = SourceCategory.Word, ["odt"] = SourceCategory.Word,
        // 表格（et 尝试 SheetJS，失败回退 WPS 互联）
        ["xls"] = SourceCategory.Excel, ["xlsx"] = SourceCategory.Excel, ["et"] = SourceCategory.Excel,
        ["ods"] = SourceCategory.Excel, ["csv"] = SourceCategory.Excel,
        // 演示（ppt/dps 需 Office/WPS 互联，odp 由内置引擎解析）
        ["ppt"] = SourceCategory.Ppt, ["pptx"] = SourceCategory.Ppt, ["dps"] = SourceCategory.Ppt,
        ["odp"] = SourceCategory.Ppt,
        // 文本
        ["md"] = SourceCategory.Markdown, ["markdown"] = SourceCategory.Markdown,
        ["txt"] = SourceCategory.Text, ["log"] = SourceCategory.Text,
        ["html"] = SourceCategory.Html, ["htm"] = SourceCategory.Html, ["xhtml"] = SourceCategory.Html,
        ["json"] = SourceCategory.Json,
    };

    public static readonly FormatOption[] VideoTargets =
    {
        new("MP4", ".mp4", "libx264", "aac",
            Params: FormatConvertParams.VideoParams(51, 23, FormatConvertParams.Mp4VideoCodecs, FormatConvertParams.Mp4AudioCodecs)),
        new("MKV", ".mkv", "libx264", "aac",
            Params: FormatConvertParams.VideoParams(51, 23, FormatConvertParams.MkvVideoCodecs, FormatConvertParams.MkvAudioCodecs)),
        new("AVI", ".avi", "libx264", "mp3",
            Params: FormatConvertParams.VideoParams(51, 23, FormatConvertParams.AviVideoCodecs, FormatConvertParams.AviAudioCodecs)),
        new("MOV", ".mov", "libx264", "aac",
            Params: FormatConvertParams.VideoParams(51, 23, FormatConvertParams.Mp4VideoCodecs, FormatConvertParams.Mp4AudioCodecs)),
        new("WebM", ".webm", "libvpx-vp9", "libopus",
            Params: FormatConvertParams.VideoParams(63, 32, FormatConvertParams.WebmVideoCodecs, FormatConvertParams.WebmAudioCodecs)),
        new("GIF", ".gif", "", "", Params: FormatConvertParams.GifVideoParams),
        new("TS", ".ts", "libx264", "aac",
            Params: FormatConvertParams.VideoParams(51, 23, FormatConvertParams.TsVideoCodecs, FormatConvertParams.TsAudioCodecs)),
        new("FLV", ".flv", "libx264", "aac",
            Params: FormatConvertParams.VideoParams(51, 23, FormatConvertParams.H264OnlyCodecs, FormatConvertParams.FlvAudioCodecs)),
        new("WMV", ".wmv", "wmv2", "wmav2",
            Params: FormatConvertParams.VideoParams(31, 23, FormatConvertParams.Wmv2OnlyCodecs, FormatConvertParams.WmaOnlyCodecs)),
        new("MP3", ".mp3", "", "libmp3lame", Params: FormatConvertParams.Mp3Params),
        new("AAC", ".aac", "", "aac", Params: FormatConvertParams.AacParams),
        new("FLAC", ".flac", "", "flac", Params: FormatConvertParams.FlacParams),
        new("WAV", ".wav", "", "pcm_s16le", Params: FormatConvertParams.WavParams),
        new("M4A", ".m4a", "", "aac", Params: FormatConvertParams.AacParams),
        new("OGG", ".ogg", "", "libvorbis", Params: FormatConvertParams.OggParams),
        new("OPUS", ".opus", "", "libopus", Params: FormatConvertParams.OpusParams),
        ZipTarget,
    };

    public static readonly FormatOption[] AudioTargets =
    {
        new("MP3", ".mp3", "", "libmp3lame", Params: FormatConvertParams.Mp3Params),
        new("AAC", ".aac", "", "aac", Params: FormatConvertParams.AacParams),
        new("FLAC", ".flac", "", "flac", Params: FormatConvertParams.FlacParams),
        new("WAV", ".wav", "", "pcm_s16le", Params: FormatConvertParams.WavParams),
        new("M4A", ".m4a", "", "aac", Params: FormatConvertParams.AacParams),
        new("OGG", ".ogg", "", "libvorbis", Params: FormatConvertParams.OggParams),
        new("OPUS", ".opus", "", "libopus", Params: FormatConvertParams.OpusParams),
        new("AIFF", ".aiff", "", "pcm_s16be", Params: FormatConvertParams.AiffParams),
        new("WMA", ".wma", "", "wmav2", Params: FormatConvertParams.WmaParams),
        ZipTarget,
    };

    public static readonly FormatOption[] ImageTargets =
    {
        new("PNG", ".png", "", "", Params: FormatConvertParams.PngParams),
        new("JPG", ".jpg", "", "", Params: FormatConvertParams.JpegParams),
        new("WebP", ".webp", "", "", Params: FormatConvertParams.WebpParams),
        new("GIF", ".gif", "", "", Params: FormatConvertParams.GifImageParams),
        new("BMP", ".bmp", "", "", Params: FormatConvertParams.PlainImageParams),
        new("TIFF", ".tiff", "", "", Params: FormatConvertParams.TiffParams),
        new("HEIC", ".heic", "", "", Params: FormatConvertParams.HeicParams),
        new("AVIF", ".avif", "", "", Params: FormatConvertParams.AvifParams),
        new("TGA", ".tga", "", "", Params: FormatConvertParams.PlainImageParams),
        new("PSD", ".psd", "", "", Params: FormatConvertParams.PlainImageParams),
        new("PDF", ".pdf", "", "", Params: FormatConvertParams.ImagePdfParams),
        new("ICO", ".ico", "", ""),
        new("MP4 视频", ".mp4", "libx264", "",
            Params: FormatConvertParams.ImageVideoParams(23, FormatConvertParams.ImageToMp4Codecs)),
        new("WebM 视频", ".webm", "libvpx-vp9", "",
            Params: FormatConvertParams.ImageVideoParams(32, FormatConvertParams.ImageToWebmCodecs)),
        new("TXT 文字识别", ".txt", "", "", ConvertSpecial.OcrText),
        ZipTarget,
    };

    /// <summary>Word 文档：doc / docx / wps / rtf / odt。</summary>
    public static readonly FormatOption[] WordTargets =
    {
        new("PDF", ".pdf", "", ""), new("DOCX", ".docx", "", ""),
        new("TXT", ".txt", "", ""), new("HTML", ".html", "", ""), new("Markdown", ".md", "", ""),
        new("PNG", ".png", "", ""), new("JPG", ".jpg", "", ""),
        ZipTarget,
    };

    /// <summary>表格：xls / xlsx / et / ods / csv。</summary>
    public static readonly FormatOption[] ExcelTargets =
    {
        new("PDF", ".pdf", "", ""), new("XLSX", ".xlsx", "", ""),
        new("CSV", ".csv", "", ""), new("HTML", ".html", "", ""),
        new("JSON", ".json", "", ""), new("Markdown", ".md", "", ""),
        new("PNG", ".png", "", ""), new("JPG", ".jpg", "", ""),
        ZipTarget,
    };

    /// <summary>演示：ppt / pptx / dps / odp。</summary>
    public static readonly FormatOption[] PptTargets =
    {
        new("PDF", ".pdf", "", ""), new("PPTX", ".pptx", "", ""),
        new("HTML", ".html", "", ""),
        new("PNG", ".png", "", ""), new("JPG", ".jpg", "", ""),
        ZipTarget,
    };

    public static readonly FormatOption[] MarkdownTargets =
    {
        new("PDF", ".pdf", "", ""), new("HTML", ".html", "", ""),
        new("TXT", ".txt", "", ""), new("DOCX", ".docx", "", ""),
        new("PNG", ".png", "", ""), new("JPG", ".jpg", "", ""),
        ZipTarget,
    };

    public static readonly FormatOption[] TextTargets =
    {
        new("PDF", ".pdf", "", ""), new("DOCX", ".docx", "", ""),
        new("Markdown", ".md", "", ""), new("HTML", ".html", "", ""),
        new("PNG", ".png", "", ""), new("JPG", ".jpg", "", ""),
        ZipTarget,
    };

    public static readonly FormatOption[] HtmlTargets =
    {
        new("PDF", ".pdf", "", ""), new("DOCX", ".docx", "", ""),
        new("TXT", ".txt", "", ""), new("Markdown", ".md", "", ""),
        new("PNG", ".png", "", ""), new("JPG", ".jpg", "", ""),
        ZipTarget,
    };

    public static readonly FormatOption[] JsonTargets =
    {
        new("TXT", ".txt", "", ""), new("Markdown", ".md", "", ""),
        new("CSV", ".csv", "", ""), new("HTML", ".html", "", ""),
        new("PDF", ".pdf", "", ""), new("DOCX", ".docx", "", ""),
        ZipTarget,
    };

    /// <summary>
    /// PDF 源：文字提取（TXT/HTML/Excel 表格）、扫描版 OCR、页面导出图片（散图/压缩包）、
    /// 合并（多份）/拆分（单份）。合并在页面按文件数动态补充，拆分单份时补充。
    /// </summary>
    public static readonly FormatOption[] PdfTargets =
    {
        new("TXT 文本", ".txt", "", ""),
        new("HTML 网页", ".html", "", ""),
        new("Excel 表格", ".xlsx", "", "", ConvertSpecial.PdfExcel),
        new("OCR 文本", ".txt", "", "", ConvertSpecial.OcrText),
        new("PNG 图片", ".png", "", ""),
        new("JPG 图片", ".jpg", "", ""),
        new("PNG 压缩包", ".zip", "", "", ConvertSpecial.ZipArchive, "png"),
        new("JPG 压缩包", ".zip", "", "", ConvertSpecial.ZipArchive, "jpg"),
        ZipTarget,
    };

    /// <summary>PDF 合并目标（多份 PDF 时出现）。</summary>
    public static readonly FormatOption MergePdfTarget = new("合并为一个 PDF", ".pdf", "", "", ConvertSpecial.MergePdf);

    /// <summary>PDF 拆分目标（单份 PDF 时出现）。</summary>
    public static readonly FormatOption SplitPdfTarget = new("拆分为单页 PDF", ".pdf", "", "", ConvertSpecial.SplitPdf);

    /// <summary>
    /// 校验拖入/选择到的路径确实是可读取的文件。
    /// 文件选择器或拖放偶尔会给出目录、失效的快捷方式或已被移走的路径；
    /// 直接 new FileInfo(path).Length 会抛 FileNotFoundException（#264），
    /// 因此所有入队路径都必须先过这里。
    /// </summary>
    public static bool TryGetInputFile(string filePath, out string fullPath, out long size)
    {
        fullPath = "";
        size = 0;
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0)
                return false;
            fullPath = info.FullName;
            size = info.Length;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>按扩展名（带不带点均可、大小写不敏感）识别类别。</summary>
    public static SourceCategory Classify(string filePath)
    {
        var ext = Path.GetExtension(filePath).TrimStart('.');
        return ExtMap.TryGetValue(ext, out var category) ? category : SourceCategory.Unsupported;
    }

    /// <summary>类别支持的转换目标格式列表。</summary>
    public static IReadOnlyList<FormatOption> GetTargetFormats(SourceCategory category) => category switch
    {
        SourceCategory.Video => VideoTargets,
        SourceCategory.Audio => AudioTargets,
        SourceCategory.Image => ImageTargets,
        SourceCategory.Pdf => PdfTargets,
        SourceCategory.Word => WordTargets,
        SourceCategory.Excel => ExcelTargets,
        SourceCategory.Ppt => PptTargets,
        SourceCategory.Markdown => MarkdownTargets,
        SourceCategory.Text => TextTargets,
        SourceCategory.Html => HtmlTargets,
        SourceCategory.Json => JsonTargets,
        _ => Array.Empty<FormatOption>()
    };

    /// <summary>该类别（含目标格式）使用的转换引擎。图片转 MP4/WebM 时借用 FFmpeg；Office 文档真实渲染用 OfficeCLI。</summary>
    public static ConvertEngine EngineFor(SourceCategory category, FormatOption? target = null)
    {
        if (target is not null)
        {
            if (target.Special == ConvertSpecial.OcrText)
                return ConvertEngine.Ocr;
            if (category == SourceCategory.Image && (target.Ext == ".mp4" || target.Ext == ".webm"))
                return ConvertEngine.Ffmpeg;
            // 非常规输出（ZIP 压缩包等）不参与 OfficeCLI 路由
            if (target.Special == ConvertSpecial.None && ShouldUseOfficeCli(category, target))
                return ConvertEngine.OfficeCli;
        }
        return category switch
        {
            SourceCategory.Video or SourceCategory.Audio => ConvertEngine.Ffmpeg,
            SourceCategory.Image => ConvertEngine.Magick,
            _ => ConvertEngine.DocEngine
        };
    }

    /// <summary>
    /// 该类别 + 目标是否用 OfficeCLI 真实渲染：Word/PPT/Excel 的排版类目标
    /// （HTML/PDF/图片 由渲染 HTML 派生，Word 的 txt/md 走结构化提取）。
    /// xlsx/csv/json/md 数据类目标保持 SheetJS 快路径；仅 OOXML 源适用（老格式由文档服务走 COM）。
    /// </summary>
    internal static bool ShouldUseOfficeCli(SourceCategory category, FormatOption target) => category switch
    {
        SourceCategory.Word => target.Ext is ".pdf" or ".html" or ".png" or ".jpg" or ".txt" or ".md",
        SourceCategory.Excel => target.Ext is ".pdf" or ".html" or ".png" or ".jpg",
        SourceCategory.Ppt => target.Ext is ".pdf" or ".html" or ".png" or ".jpg",
        _ => false
    };

    /// <summary>该文件是否只能通过 Office / WPS 互联转换（旧版二进制格式）。</summary>
    public static bool RequiresOfficeInterop(string filePath)
        => Path.GetExtension(filePath).ToLowerInvariant() is ".doc" or ".wps" or ".ppt" or ".dps" or ".et";
}

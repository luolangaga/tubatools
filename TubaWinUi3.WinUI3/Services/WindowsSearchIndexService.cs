using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// 工具快捷方式的统一写入点：注册 Windows 搜索索引（开始菜单）与「发送到桌面」。
/// 内置工具快捷方式以 --open-builtin &lt;id&gt; 启动本程序直达工具，
/// 图标用该工具的字体图标（Segoe Fluent Icons 字形）离线渲染成 .ico。
/// </summary>
internal static class WindowsSearchIndexService
{
    private static readonly string StartMenuFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Start Menu\Programs\图吧工具箱CE");

    /// <summary>
    /// 将所有工具注册到 Windows 搜索索引（后台执行，不阻塞 UI）。
    /// </summary>
    public static async Task RegisterAllToolsAsync()
    {
        try
        {
            var allTools = ToolCatalog.GetAllToolsCached();
            if (allTools.Count == 0)
                return;

            await Task.Run(() => RegisterTools(allTools));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 注册失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 工具目录变化后刷新索引快捷方式。
    /// </summary>
    public static async Task RefreshAsync()
    {
        try
        {
            var allTools = ToolCatalog.GetAllToolsCached();
            await Task.Run(() => RegisterTools(allTools));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 刷新失败: {ex.Message}");
        }
    }

    private static void RegisterTools(IReadOnlyList<ToolItem> tools)
    {
        // 确保目标文件夹存在
        if (!Directory.Exists(StartMenuFolder))
            Directory.CreateDirectory(StartMenuFolder);

        // 记录当前应该存在的快捷方式文件名，用于后续清理
        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) 外部工具（.exe 等）
        var toRegister = DeduplicateTools(tools);
        foreach (var (name, tool) in toRegister)
        {
            var shortcutPath = Path.Combine(StartMenuFolder, $"{SanitizeFileName(name)}.lnk");
            expectedFiles.Add(Path.GetFileName(shortcutPath));

            try
            {
                if (File.Exists(shortcutPath) && IsShortcutUpToDate(shortcutPath, tool.EffectivePath))
                    continue;

                CreateShortcut(shortcutPath, tool.EffectivePath, tool.EffectiveWorkingDir,
                    $"{name} - {tool.Category}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 创建快捷方式失败 [{name}]: {ex.Message}");
            }
        }

        // 2) 内置工具（通过 --open-builtin 启动参数打开）
        var appExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var appDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(appExe) && File.Exists(appExe))
        {
            foreach (var builtin in BuiltinToolRegistry.Tools)
            {
                var displayName = builtin.Name;
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;

                // 同名去重（内置工具与外部工具同名时，优先保留外部工具）
                if (expectedFiles.Contains($"{SanitizeFileName(displayName)}.lnk"))
                    continue;

                var shortcutPath = Path.Combine(StartMenuFolder, $"{SanitizeFileName(displayName)}.lnk");
                expectedFiles.Add(Path.GetFileName(shortcutPath));

                try
                {
                    CreateShortcut(shortcutPath, appExe, appDir,
                        $"{displayName} - {builtin.Category}",
                        $"--open-builtin {builtin.Id}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 创建内置工具快捷方式失败 [{displayName}]: {ex.Message}");
                }
            }
        }

        // 3) 清理过期快捷方式
        CleanupStaleShortcuts(expectedFiles);
    }

    /// <summary>
    /// 对工具列表去重：
    /// - 跳过内置工具链接（没有真实文件路径）
    /// - 跳过需要下载但还没下载的工具
    /// - 同名工具（不区分大小写）只保留第一个有效项
    /// - 同一路径的工具只保留一次
    /// </summary>
    private static List<(string Name, ToolItem Tool)> DeduplicateTools(IReadOnlyList<ToolItem> tools)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Name, ToolItem Tool)>();

        foreach (var tool in tools)
        {
            // 跳过内置工具链接
            if (tool.IsBuiltinLink)
                continue;

            // 跳过需要下载但还没下载的工具
            if (tool.NeedsDownload)
                continue;

            // 跳过路径为空或文件不存在的工具
            var effectivePath = tool.EffectivePath;
            if (string.IsNullOrWhiteSpace(effectivePath) || !File.Exists(effectivePath))
                continue;

            // 同一可执行文件路径去重（不同分类下的同一工具）
            if (!seenPaths.Add(effectivePath))
                continue;

            // 同名工具去重（用户装了多个版本时只保留一个）
            var displayName = tool.Name;
            if (!seenNames.Add(displayName))
                continue;

            result.Add((displayName, tool));
        }

        return result;
    }

    /// <summary>
    /// 检查已有快捷方式是否指向正确的目标（避免重复写入）。
    /// </summary>
    private static bool IsShortcutUpToDate(string shortcutPath, string targetPath)
    {
        try
        {
            // 通过读取文件的最后写入时间和大小做粗略判断，
            // 精确比对需要 COM 互操作，在批量场景下太慢
            // 这里简单返回 false 让它每次重建，开销很小
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 创建 .lnk 快捷方式（进程内调用 WScript.Shell COM，无子进程）。
    /// WScript.Shell 需要在 STA 线程上调用：UI 线程本身是 STA，
    /// 后台注册路径（Task.Run 的 MTA 线程池）则临时起一个 STA 线程执行。
    /// </summary>
    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir,
        string description, string? arguments = null, string? iconPath = null)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            Exception? error = null;
            var staThread = new Thread(() =>
            {
                try { CreateShortcutOnSta(shortcutPath, targetPath, workingDir, description, arguments, iconPath); }
                catch (Exception ex) { error = ex; }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();
            if (error is not null)
                throw error;
            return;
        }

        CreateShortcutOnSta(shortcutPath, targetPath, workingDir, description, arguments, iconPath);
    }

    private static void CreateShortcutOnSta(string shortcutPath, string targetPath, string workingDir,
        string description, string? arguments, string? iconPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            throw new InvalidOperationException("无法加载 WScript.Shell 组件。");

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("无法创建 WScript.Shell 组件。");
            shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
            dynamic sc = shortcut;
            sc.TargetPath = targetPath;
            sc.WorkingDirectory = workingDir;
            sc.Description = description;
            if (!string.IsNullOrWhiteSpace(arguments))
                sc.Arguments = arguments;
            if (!string.IsNullOrWhiteSpace(iconPath))
                sc.IconLocation = $"{iconPath},0";
            sc.Save();
        }
        finally
        {
            if (shortcut is not null)
            {
                try { Marshal.FinalReleaseComObject(shortcut); } catch { }
            }
            if (shell is not null)
            {
                try { Marshal.FinalReleaseComObject(shell); } catch { }
            }
        }
    }

    /// <summary>
    /// 清理不再对应的过期快捷方式。
    /// </summary>
    private static void CleanupStaleShortcuts(HashSet<string> expectedFiles)
    {
        try
        {
            if (!Directory.Exists(StartMenuFolder))
                return;

            foreach (var file in Directory.GetFiles(StartMenuFolder, "*.lnk"))
            {
                var fileName = Path.GetFileName(file);
                if (!expectedFiles.Contains(fileName))
                {
                    try
                    {
                        File.Delete(file);
                        System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 清理过期快捷方式: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 清理失败 [{fileName}]: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 清理扫描失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 清理所有由本服务创建的快捷方式（卸载/重置时调用）。
    /// </summary>
    public static void RemoveAll()
    {
        try
        {
            if (Directory.Exists(StartMenuFolder))
            {
                Directory.Delete(StartMenuFolder, recursive: true);
                System.Diagnostics.Debug.WriteLine("[WindowsSearchIndex] 已移除所有搜索索引快捷方式");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 移除失败: {ex.Message}");
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "工具" : sanitized;
    }

    #region 内置工具桌面快捷方式 + 字体图标渲染

    private static readonly object _iconLock = new();

    private static string IconCacheDir => Path.Combine(ConfigManager.GetDataDir(), "DesktopIcons");

    /// <summary>
    /// 「发送到桌面」内置工具快捷方式：双击以 --open-builtin 启动本程序直达工具，
    /// 图标用该工具的字体图标（与卡片展示同源）。
    /// </summary>
    internal static void CreateDesktopShortcut(IBuiltinTool tool)
    {
        var appExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(appExe) || !File.Exists(appExe))
            throw new InvalidOperationException("无法定位工具箱自身路径，无法创建快捷方式。");

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, $"{tool.Name}.lnk");

        // 图标渲染失败只影响显示，不阻断快捷方式本身（退回程序默认图标）
        string? iconPath = null;
        try { iconPath = EnsureBuiltinIcon(tool); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsSearchIndex] 生成内置工具图标失败 [{tool.Id}]: {ex.Message}");
        }

        CreateShortcut(shortcutPath, appExe, AppContext.BaseDirectory,
            $"{tool.Name} - {tool.Category}", $"--open-builtin {tool.Id}", iconPath);
    }

    /// <summary>按工具 Id 缓存字形 .ico；已存在直接复用。</summary>
    private static string? EnsureBuiltinIcon(IBuiltinTool tool)
    {
        try
        {
            Directory.CreateDirectory(IconCacheDir);
        }
        catch
        {
            return null;
        }

        var safeName = string.Concat((tool.Id ?? "builtin").Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        var path = Path.Combine(IconCacheDir,
            string.IsNullOrWhiteSpace(safeName) ? "builtin" : safeName + ".ico");
        if (File.Exists(path))
            return path;

        lock (_iconLock)
        {
            if (File.Exists(path))
                return path;

            Bitmap? master = null;
            try
            {
                master = RenderGlyphBitmap(tool.Glyph);
                var bytes = EncodeIco(master);
                File.WriteAllBytes(path, bytes);
            }
            finally
            {
                master?.Dispose();
            }
            return path;
        }
    }

    /// <summary>256×256 透明底字形图：字形居中铺满、着系统强调色。</summary>
    private static Bitmap RenderGlyphBitmap(string glyph)
    {
        const int canvas = 256;
        var bmp = new Bitmap(canvas, canvas, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(AccentColor());
        var familyName = FindIconFontFamilyName();
        if (familyName is null || string.IsNullOrEmpty(glyph))
            return bmp;

        using var family = new FontFamily(familyName);
        try
        {
            // 字形走路径填充：按实际轮廓测量缩放，居中与对齐不受字体度量影响
            using var path = new GraphicsPath();
            path.AddString(glyph, family, (int)FontStyle.Regular, 256f, new PointF(0f, 0f),
                StringFormat.GenericTypographic);

            var bounds = path.GetBounds();
            if (bounds.Width > 2f && bounds.Height > 2f &&
                !float.IsNaN(bounds.Width) && !float.IsNaN(bounds.Height) &&
                !float.IsInfinity(bounds.Width) && !float.IsInfinity(bounds.Height))
            {
                const float pad = 26f;
                var scale = Math.Min(1f, (canvas - pad * 2f) / Math.Max(bounds.Width, bounds.Height));
                var matrix = new Matrix();
                matrix.Translate(
                    (canvas - bounds.Width * scale) / 2f - bounds.X * scale,
                    (canvas - bounds.Height * scale) / 2f - bounds.Y * scale);
                matrix.Scale(scale, scale);
                path.Transform(matrix);
                g.FillPath(brush, path);
                return bmp;
            }
        }
        catch
        {
            // 某些字体对个别码位 AddString 会抛异常，退回文本绘制
        }

        // 兜底：直接按字形框居中绘制
        using var font = new Font(family, 200f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.DrawString(glyph, font, brush, new RectangleF(0f, 0f, canvas, canvas), fmt);
        return bmp;
    }

    private static string? FindIconFontFamilyName()
    {
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol" })
        {
            try
            {
                using var family = new FontFamily(name);
                return name; // 构造成功即已安装
            }
            catch { }
        }
        return null;
    }

    private static Color AccentColor()
    {
        try
        {
            var c = new Windows.UI.ViewManagement.UISettings()
                .GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }
        catch
        {
            return Color.FromArgb(255, 0, 103, 192);
        }
    }

    /// <summary>编码多尺寸 ICO（16/32/48/64/256，32bpp 未压缩 DIB + 全零 AND 掩码）。</summary>
    private static byte[] EncodeIco(Bitmap master)
    {
        var sizes = new[] { 256, 64, 48, 32, 16 };
        var images = new List<(int Size, Bitmap Bmp)>();
        try
        {
            foreach (var size in sizes)
            {
                if (size == master.Width)
                {
                    images.Add((size, master));
                    continue;
                }

                var small = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(master, 0, 0, size, size);
                }
                images.Add((size, small));
            }

            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms))
            {
                const int headerSize = 6;
                const int entrySize = 16;
                var dataStart = headerSize + entrySize * images.Count;

                bw.Write((ushort)0); // ICONDIR.reserved
                bw.Write((ushort)1); // 类型:图标
                bw.Write((ushort)images.Count);

                var cursor = dataStart;
                foreach (var (size, _) in images)
                {
                    var xorSize = size * size * 4;
                    var maskRowBytes = ((size + 31) / 32) * 4;
                    var bytesInRes = 40 + xorSize + maskRowBytes * size;

                    bw.Write((byte)(size >= 256 ? 0 : size)); // 0 表示 256
                    bw.Write((byte)(size >= 256 ? 0 : size));
                    bw.Write((byte)0);   // 调色板颜色数
                    bw.Write((byte)0);   // 保留
                    bw.Write((ushort)1); // 颜色平面
                    bw.Write((ushort)32); // 位深
                    bw.Write(bytesInRes);
                    bw.Write(cursor);
                    cursor += bytesInRes;
                }

                foreach (var (_, bmp) in images)
                    WriteDib(bw, bmp);
            }
            return ms.ToArray();
        }
        finally
        {
            foreach (var (_, bmp) in images)
            {
                if (!ReferenceEquals(bmp, master))
                    bmp.Dispose();
            }
        }
    }

    private static void WriteDib(BinaryWriter bw, Bitmap bmp)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        var maskRowBytes = ((w + 31) / 32) * 4;

        bw.Write(40);      // BITMAPINFOHEADER.biSize
        bw.Write(w);       // biWidth
        bw.Write(h * 2);   // biHeight:XOR + AND
        bw.Write((ushort)1);  // biPlanes
        bw.Write((ushort)32); // biBitCount
        bw.Write(0);       // biCompression:BI_RGB
        bw.Write(0);       // biSizeImage（图标解码以 dwBytesInRes 为准）
        bw.Write(0);       // biXPelsPerMeter
        bw.Write(0);       // biYPelsPerMeter
        bw.Write(0);       // biClrUsed
        bw.Write(0);       // biClrImportant

        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var row = new byte[stride];
            for (var y = h - 1; y >= 0; y--) // ICO 像素自下而上
            {
                Marshal.Copy(data.Scan0 + y * stride, row, 0, stride);
                bw.Write(row, 0, w * 4); // BGRA
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        var maskRow = new byte[maskRowBytes]; // 32bpp alpha 下 AND 掩码全零即可
        for (var y = 0; y < h; y++)
            bw.Write(maskRow);
    }

    #endregion
}

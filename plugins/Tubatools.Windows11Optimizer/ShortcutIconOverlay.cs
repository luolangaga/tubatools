using System.Buffers.Binary;

namespace Tubatools.Windows11Optimizer;

/// <summary>
/// 快捷方式箭头/盾牌覆盖使用的真正透明 ICO。
/// 旧实现借用 shell32.dll,-50，部分 Windows 11 版本在开机重建图标缓存时会把它画成黑块。
/// 这里生成 16/32/48 三帧、每帧带一个不可见像素的 ICO 并放到所有用户可读的固定目录，
/// 避免升级、清理插件包或清空图标缓存后失效。
/// </summary>
internal static class ShortcutIconOverlay
{
    internal const string BlankIconPath = @"%ProgramData%\TubaWinUi3\Windows11Optimizer\blank.ico";
    internal const string LegacyShell32Blank = @"%SystemRoot%\System32\shell32.dll,-50";
    private const string IconFileName = "blank.ico";
    private const int HeaderBytes = 40;
    private const int DirectoryHeaderBytes = 6;
    private const int DirectoryEntryBytes = 16;

    // Explorer 10/11 会把“所有像素 alpha=0”的 32 位图标走成不透明黑块。
    // 每个尺寸保留一个 alpha=1 的黑色像素，强制它走 alpha 混合路径；肉眼不可见。
    private static readonly int[] IconSizes = [16, 32, 48];
    private static readonly byte[] IconBytes = BuildIcon();

    /// <summary>写入供 Shell Icons 使用的透明图标，返回实际文件路径。</summary>
    public static string EnsureInstalled()
    {
        var path = InstallPath;
        EnsureInstalled(path);
        return path;
    }

    internal static string InstallPath
    {
        get
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
                throw new InvalidOperationException("无法定位 ProgramData 目录，不能安装透明快捷方式图标。");
            return Path.Combine(programData, "TubaWinUi3", "Windows11Optimizer", IconFileName);
        }
    }

    /// <summary>文件存在且内容正确时不做任何写入；缺失或被破坏时原子重建。</summary>
    internal static void EnsureInstalled(string path)
    {
        if (HasCurrentIcon(path)) return;

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("透明快捷方式图标路径无效。");
        Directory.CreateDirectory(directory);

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(IconBytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // 临时文件残留不影响最终图标；下次写入会使用新文件名。
            }
        }
    }

    internal static bool HasCurrentIcon(string path)
    {
        try
        {
            return File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(IconBytes);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>识别旧版 shell32.dll,-50（环境变量写法和展开后的绝对路径都认）。</summary>
    internal static bool IsLegacyShell32Blank(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        var legacy = Environment.ExpandEnvironmentVariables(LegacyShell32Blank);
        if (string.Equals(normalized, legacy, StringComparison.OrdinalIgnoreCase)) return true;

        // ExpandEnvironmentVariables 不会识别 %windir% 之外一些大小写变体时，再做一次手工比较。
        var withoutRootVariable = normalized;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(root) && withoutRootVariable.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            withoutRootVariable = "%SystemRoot%" + withoutRootVariable[root.Length..];
        return string.Equals(withoutRootVariable, LegacyShell32Blank, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 构造 16/32/48 三帧 32 位 ICO。每帧只有一个 alpha=1 的近透明像素，AND 掩码按 4 字节行对齐、
    /// 有效位全为 1（透明），只保留一个 0 位与那个像素对应：这既是 Explorer 不把透明覆盖图标画成黑块的必要条件，
    /// 也能覆盖 100%～300% DPI。
    /// </summary>
    internal static byte[] BuildIcon()
    {
        var images = IconSizes.Select(BuildImage).ToArray();
        var directoryBytes = DirectoryHeaderBytes + DirectoryEntryBytes * images.Length;
        var totalBytes = directoryBytes + images.Sum(image => image.Length);

        using var stream = new MemoryStream(totalBytes);
        using var writer = new BinaryWriter(stream);

        // ICONDIR
        writer.Write((ushort)0);                        // reserved
        writer.Write((ushort)1);                        // type: icon
        writer.Write((ushort)images.Length);            // image count

        var offset = directoryBytes;
        for (var i = 0; i < images.Length; i++)
        {
            var size = IconSizes[i];
            var image = images[i];
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);                      // palette count
            writer.Write((byte)0);                      // reserved
            writer.Write((ushort)1);                    // color planes
            writer.Write((ushort)32);                   // bits per pixel
            writer.Write((uint)image.Length);
            writer.Write((uint)offset);
            offset += image.Length;
        }

        foreach (var image in images) writer.Write(image);

        writer.Flush();
        var bytes = stream.ToArray();
        if (bytes.Length != totalBytes)
            throw new InvalidOperationException("透明快捷方式图标生成失败。");
        return bytes;
    }

    private static byte[] BuildImage(int size)
    {
        var xorBytes = size * size * 4;
        // ICON DIB 的 AND 掩码是 1bpp 位图，每行必须按 4 字节对齐。
        var andStride = ((size + 31) / 32) * 4;
        var andBytes = andStride * size;
        var imageBytes = HeaderBytes + xorBytes + andBytes;
        var image = new byte[imageBytes];

        // BITMAPINFOHEADER。ICO 的 DIB 高度包含 XOR 位图和 AND 掩码，因此为 2×size。
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0, 4), HeaderBytes);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(4, 4), size);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(8, 4), size * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(12, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(14, 2), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(20, 4), (uint)xorBytes);

        // BGRA 全为 0，只把第一个（左下角最右）像素的 alpha 设为 1。
        // Explorer 只有在位图含有非 0 alpha 时才按透明图标做 alpha 混合。
        image[HeaderBytes + (size - 1) * 4 + 3] = 1;

        // AND 掩码里 1 = 透明、0 = 不透明黑；全 0 会直接画出黑块。
        // 所有有效位默认置 1，只把最下面一行最右边的像素清 0，与上面的 alpha 像素对应。
        var andStart = HeaderBytes + xorBytes;
        var effectiveBytes = (size + 7) / 8;
        for (var row = 0; row < size; row++)
            image.AsSpan(andStart + row * andStride, effectiveBytes).Fill(0xFF);
        image[andStart + effectiveBytes - 1] &= 0xFE;
        return image;
    }
}

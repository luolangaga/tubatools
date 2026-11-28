using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// .lnk「以管理员身份运行」标志补丁（ShortcutRunAsFlag）的纯逻辑测试：
/// MS-SHLLINK LinkFlags @0x14 的 SLDF_RUNAS_USER(0x2000) 位、头部校验、幂等与文件回读校验。
/// 快捷方式带该标志后 Explorer 直接弹 UAC 单进程管理员启动，跳过主程序内部提权重启。
/// </summary>
public class ShortcutRunAsFlagTests
{
    /// <summary>构造最小合法 ShellLinkHeader（78 字节）：HeaderSize=0x4C + LinkCLSID + LinkFlags。</summary>
    private static byte[] MinimalLnk(byte linkFlagsHighByte = 0)
    {
        var bytes = new byte[0x4C];
        bytes[0] = 0x4C; // HeaderSize DWORD = 0x0000004C（小端）
        // LinkCLSID {00021401-0000-0000-C000-000000000046} 的首字节（非必须，贴近真实文件）
        bytes[4] = 0x01; bytes[5] = 0x14; bytes[6] = 0x02;
        bytes[0x15] = linkFlagsHighByte;
        return bytes;
    }

    [Fact]
    public void TrySet_SetsRunAsUserBit()
    {
        var lnk = MinimalLnk();
        Assert.True(ShortcutRunAsFlag.TrySet(lnk));
        Assert.Equal(0x20, lnk[0x15]);
        Assert.True(ShortcutRunAsFlag.IsSet(lnk));
    }

    [Fact]
    public void TrySet_PreservesOtherLinkFlags()
    {
        // HasLinkTargetIDList(0x1) | HasRelativePath(0x8) | IsUnicode(0x80) 等低位与同字节其他位不受影响
        var lnk = MinimalLnk();
        lnk[0x14] = 0x89;
        lnk[0x15] = 0x40; // HasExpIcon 等高字节位
        Assert.True(ShortcutRunAsFlag.TrySet(lnk));
        Assert.Equal(0x89, lnk[0x14]);
        Assert.Equal(0x60, lnk[0x15]);
    }

    [Fact]
    public void TrySet_Idempotent_WhenAlreadySet()
    {
        var lnk = MinimalLnk(0x20);
        Assert.False(ShortcutRunAsFlag.TrySet(lnk));
        Assert.True(ShortcutRunAsFlag.IsSet(lnk));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(0x17)]
    public void TrySet_RejectsTooShortFiles(int length)
    {
        var lnk = new byte[length];
        if (length > 0) lnk[0] = 0x4C;
        Assert.False(ShortcutRunAsFlag.TrySet(lnk));
        Assert.False(ShortcutRunAsFlag.IsSet(lnk));
    }

    [Fact]
    public void TrySet_RejectsInvalidHeader()
    {
        var lnk = MinimalLnk();
        lnk[0] = 0x00; // 不是 ShellLinkHeader
        Assert.False(ShortcutRunAsFlag.TrySet(lnk));
        Assert.False(ShortcutRunAsFlag.IsSet(lnk));
    }

    [Fact]
    public void TrySetOnFile_RoundTrip_VerifiesWrittenBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tuba-runas-test-{Guid.NewGuid():N}.lnk");
        try
        {
            File.WriteAllBytes(path, MinimalLnk());
            Assert.True(ShortcutRunAsFlag.TrySetOnFile(path));
            Assert.True(ShortcutRunAsFlag.IsSet(File.ReadAllBytes(path)));
            // 已置位再跑一次：不重写、仍报成功
            Assert.True(ShortcutRunAsFlag.TrySetOnFile(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TrySetOnFile_MissingOrInvalid_ReturnsFalseWithoutThrowing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"tuba-runas-missing-{Guid.NewGuid():N}.lnk");
        Assert.False(ShortcutRunAsFlag.TrySetOnFile(missing));

        var invalid = Path.Combine(Path.GetTempPath(), $"tuba-runas-invalid-{Guid.NewGuid():N}.lnk");
        try
        {
            File.WriteAllBytes(invalid, new byte[] { 1, 2, 3 });
            Assert.False(ShortcutRunAsFlag.TrySetOnFile(invalid));
        }
        finally { File.Delete(invalid); }
    }
}
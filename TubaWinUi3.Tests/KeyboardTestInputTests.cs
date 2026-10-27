using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class KeyboardTestInputTests
{
    [Theory]
    [InlineData(0x10, 0x2A, false, 0xA0)]
    [InlineData(0x10, 0x36, false, 0xA1)]
    [InlineData(0x11, 0x1D, false, 0xA2)]
    [InlineData(0x11, 0x1D, true, 0xA3)]
    [InlineData(0x12, 0x38, false, 0xA4)]
    [InlineData(0x12, 0x38, true, 0xA5)]
    [InlineData(0x0D, 0x1C, false, 0x0D)]
    [InlineData(0x0D, 0x1C, true, 0xE01C)]
    [InlineData(0x86, 0x7E, true, 0xE07E)]
    public void PreservesPhysicalKeyIdentity(int vk, int scan, bool extended, int expected)
        => Assert.Equal(expected, KeyboardTestInput.ResolveKey(vk, scan, extended));

    [Theory]
    [InlineData(0x24, 0x47, 0x67)]
    [InlineData(0x26, 0x48, 0x68)]
    [InlineData(0x21, 0x49, 0x69)]
    [InlineData(0x25, 0x4B, 0x64)]
    [InlineData(0x27, 0x4D, 0x66)]
    [InlineData(0x23, 0x4F, 0x61)]
    [InlineData(0x28, 0x50, 0x62)]
    [InlineData(0x22, 0x51, 0x63)]
    [InlineData(0x2D, 0x52, 0x60)]
    [InlineData(0x2E, 0x53, 0x6E)]
    public void NumLockOffDoesNotConfuseNumpadWithNavigation(int vk, int scan, int expected)
    {
        Assert.Equal(expected, KeyboardTestInput.ResolveKey(vk, scan, false));
        Assert.Equal(vk, KeyboardTestInput.ResolveKey(vk, scan, true));
    }

    [Fact]
    public void NumpadFiveWithNumLockOffIsNotClear()
        => Assert.Equal(0x65, KeyboardTestInput.ResolveKey(0x0C, 0x4C, false));
}

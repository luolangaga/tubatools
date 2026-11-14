using Tubatools.AiXamlCard;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>卡片 XAML 白名单的回归：能写什么、不能写什么，都在这里定死。</summary>
public sealed class AiXamlCardGuardTests
{
    private const string Root = """
        <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <StackPanel Spacing="8">
            <TextBlock Text="你好" Foreground="TubaText" />
            <Button x:Name="Go" Content="继续" />
          </StackPanel>
        </Border>
        """;

    [Fact]
    public void Validate_AcceptsAnOrdinaryCard()
    {
        var result = CardGuard.Validate(Root);
        Assert.Equal(CardGuardStatus.Ok, result.Status);
        Assert.True(result.Ok);
    }

    [Fact]
    public void Validate_TreatsPartialWritesAsIncomplete()
    {
        // 模型写到一半时不能报错，只能等下一次写入。
        Assert.Equal(CardGuardStatus.Incomplete, CardGuard.Validate("<Border").Status);
        Assert.Equal(CardGuardStatus.Incomplete, CardGuard.Validate("").Status);
        Assert.Equal(CardGuardStatus.Incomplete, CardGuard.Validate(null).Status);
    }

    [Theory]
    [InlineData("<WebView2 xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />")]
    [InlineData("<MediaPlayerElement xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />")]
    [InlineData("<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />")]
    [InlineData("<Frame xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />")]
    public void Validate_RejectsElementsThatCanLoadOutsideContent(string xaml)
    {
        var result = CardGuard.Validate(xaml);
        Assert.Equal(CardGuardStatus.Invalid, result.Status);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void Validate_RejectsEventHandlers()
    {
        var xaml = """
            <Button xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Click="Steal" Content="x" />
            """;
        var result = CardGuard.Validate(xaml);
        Assert.Equal(CardGuardStatus.Invalid, result.Status);
        Assert.Contains("代码后置", result.Error);
    }

    [Theory]
    [InlineData("Image")]
    [InlineData("BitmapImage")]
    public void Validate_RejectsRemoteOrFileImageSources(string element)
    {
        foreach (var source in new[] { "https://example.com/a.png", "file:///C:/Windows/a.png", "ms-appdata:///local/a.png", "../a.png" })
        {
            var xaml = $"""
                <{element} xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Source="{source}" />
                """;
            Assert.Equal(CardGuardStatus.Invalid, CardGuard.Validate(xaml).Status);
        }
    }

    [Fact]
    public void Validate_AllowsOnlySessionAssetsImages()
    {
        var ok = """
            <Image xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Source="assets/photo.png" />
            """;
        Assert.Equal(CardGuardStatus.Ok, CardGuard.Validate(ok).Status);
        foreach (var bad in new[] { "assets/../../secret.png", "assets/a.exe", "assets/", "assets/sub/a.png" })
            Assert.Equal(CardGuardStatus.Invalid,
                CardGuard.Validate($"""<Image xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Source="{bad}" />""").Status);
    }

    [Fact]
    public void Validate_RejectsForeignNamespacesAndDtd()
    {
        var foreign = """<Border xmlns="urn:evil" />""";
        Assert.Equal(CardGuardStatus.Invalid, CardGuard.Validate(foreign).Status);

        var dtd = """
            <!DOCTYPE x [<!ENTITY e SYSTEM "file:///c:/windows/win.ini">]>
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        Assert.NotEqual(CardGuardStatus.Ok, CardGuard.Validate(dtd).Status);
    }

    [Fact]
    public void Validate_EnforcesSizeAndDepthLimits()
    {
        var huge = "<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                   new string('x', CardGuard.MaxCharacters) + "</Border>";
        Assert.Equal(CardGuardStatus.Invalid, CardGuard.Validate(huge).Status);

        var deep = string.Concat(Enumerable.Repeat("<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">", CardGuard.MaxDepth + 2))
                   + string.Concat(Enumerable.Repeat("</StackPanel>", CardGuard.MaxDepth + 2));
        Assert.Equal(CardGuardStatus.Invalid, CardGuard.Validate(deep).Status);
    }

    [Fact]
    public void Validate_RejectsNonNumericSizes()
    {
        var xaml = """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="calc(1px)" />
            """;
        Assert.Equal(CardGuardStatus.Invalid, CardGuard.Validate(xaml).Status);
    }
}

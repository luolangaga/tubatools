using System.Text.Json;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

public sealed class AgentAttachmentTests
{
    [Fact]
    public async Task ImageInput_UsesPixelsWithEscapedSourceUri_WithoutEmbeddingPixelsInPromptText()
    {
        var path = Path.Combine(Path.GetTempPath(), "图片 #" + Guid.NewGuid().ToString("N") + ".png");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        try
        {
        var image = new AgentAttachment(path, "图片.png", "image/png");
        var file = new AgentAttachment(@"C:\测试目录\报告.txt", "报告.txt", "");
        var input = JsonSerializer.SerializeToElement(await AgentAttachment.PromptInputAsync("查看附件", [image, file], CancellationToken.None));
        Assert.Equal("查看附件", input.GetProperty("text").GetString());
        var sent = Assert.Single(input.GetProperty("files").EnumerateArray());
        Assert.Equal("data:image/png;base64,AQID", sent.GetProperty("uri").GetString());
        Assert.Equal(image.Path, new Uri(sent.GetProperty("source").GetProperty("uri").GetString()!).LocalPath);
        Assert.Equal(image.Name, sent.GetProperty("name").GetString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UserHistory_OnlyUsesLocalFileSources_ForImagePreviews()
    {
        using var json = JsonDocument.Parse("""[{"id":"user","type":"user","text":"图片","files":[{"mime":"image/png","name":"截图.png","data":"large base64 is not copied","source":{"type":"uri","uri":"file:///C:/test/shot.png"}},{"mime":"image/png","source":{"type":"uri","uri":"https://example.com/remote.png"}}]}]""");
        var message = Assert.Single(AgentProtocol.ParseMessages(json.RootElement));
        var attachment = Assert.Single(message.Attachments!);
        Assert.Equal(@"C:\test\shot.png", attachment.Path); Assert.True(attachment.IsImage);
        Assert.DoesNotContain("base64", message.Text);
    }

    [Fact]
    public void Models_UseDeclaredVisionSupport_InsteadOfInferringFromName()
    {
        using var json = JsonDocument.Parse("""[{"id":"vision-in-name","providerID":"p","name":"Vision","enabled":true},{"id":"actual","providerID":"p","name":"Other","enabled":true,"capabilities":{"input":["text","image"]}}]""");
        var models = AgentProtocol.ParseModels(json.RootElement);
        Assert.False(models.Single(m => m.Id == "vision-in-name").SupportsImages);
        Assert.True(models.Single(m => m.Id == "actual").SupportsImages);
    }
}

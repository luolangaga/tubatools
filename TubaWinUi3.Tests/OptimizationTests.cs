using System.Text.Json;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Ai;

namespace TubaWinUi3.Tests;

public partial class AiProviderStoreTests
{
    [Fact]
    public void Save_ProtectsKeyInPrimaryAndBackup_AndRoundTrips()
    {
        var provider = AiProviderStore.GetProvider("deepseek")!;
        provider.ApiKey = "test-secret-never-plaintext";
        AiProviderStore.Save();
        AiProviderStore.Save();
        Assert.Null(AiProviderStore.LastSaveError);
        Assert.DoesNotContain(provider.ApiKey, File.ReadAllText(_path));
        Assert.DoesNotContain(provider.ApiKey, File.ReadAllText(_path + ".bak"));
        AiProviderStore.InvalidateCache();
        Assert.Equal("test-secret-never-plaintext", AiProviderStore.GetProvider("deepseek")!.ApiKey);
    }

    [Fact]
    public void Load_LegacyPlaintextKey_MigratesBothFiles()
    {
        File.WriteAllText(_path, """{"providers":[{"id":"custom","name":"custom","apiKey":"legacy-secret"}]}""");
        Assert.Equal("legacy-secret", AiProviderStore.GetProvider("custom")!.ApiKey);
        Assert.DoesNotContain("legacy-secret", File.ReadAllText(_path));
        Assert.DoesNotContain("legacy-secret", File.ReadAllText(_path + ".bak"));
    }

    [Fact]
    public void Load_DamagedPrimary_RecoversBackup_AndPreservesDamagedFile()
    {
        AiProviderStore.GetProviders();
        AiProviderStore.GetProvider("deepseek")!.ApiKey = "recovered-secret";
        AiProviderStore.Save(); AiProviderStore.Save();
        File.WriteAllText(_path, "broken");
        AiProviderStore.InvalidateCache();
        Assert.Equal("recovered-secret", AiProviderStore.GetProvider("deepseek")!.ApiKey);
        Assert.Equal("broken", File.ReadAllText(_path + ".damaged"));
    }

    [Fact]
    public void Load_BothFilesDamaged_DoesNotReplaceWithDefaults()
    {
        File.WriteAllText(_path, "broken");
        File.WriteAllText(_path + ".bak", "broken-backup");
        Assert.Throws<IOException>(() => AiProviderStore.GetProviders());
        Assert.Equal("broken", File.ReadAllText(_path));
        Assert.Equal("broken-backup", File.ReadAllText(_path + ".bak"));
    }

    [Fact]
    public void Save_FailureIsReported_ExistingFileSurvives()
    {
        AiProviderStore.GetProviders();
        var original = File.ReadAllText(_path);
        File.Delete(_path + ".bak");
        Directory.CreateDirectory(_path + ".bak");
        string? error = null;
        void OnError(string message) => error = message;
        AiProviderStore.SaveFailed += OnError;
        try
        {
            AiProviderStore.Save();
            Assert.NotNull(error);
            Assert.NotNull(AiProviderStore.LastSaveError);
            Assert.Equal(original, File.ReadAllText(_path));
        }
        finally { AiProviderStore.SaveFailed -= OnError; }
    }
}

public class OptimizationTests
{
    [Theory]
    [InlineData("查硬盘寿命", "CrystalDiskInfo")]
    [InlineData("测试内存稳定性", "MemTest")]
    [InlineData("游戏掉帧", "游戏监控")]
    [InlineData("新机验收", "新机验机")]
    public void Search_RecognizesTaskIntent(string query, string name) =>
        Assert.True(ToolSearchMatcher.Score(query, name) > 0);

    [Fact]
    public void Search_DescriptionMatches_AndExactNameRanksHigher()
    {
        var description = ToolSearchMatcher.Score("温度", "传感器", ["读取硬件温度"]);
        Assert.True(description > 0);
        Assert.True(ToolSearchMatcher.Score("温度", "温度") > description);
        Assert.Equal(0, ToolSearchMatcher.Score("不存在的需求", "CPU-Z"));
    }

    [Theory]
    [InlineData("ypjk", "硬盘健康")]
    [InlineData("clqgj", "处理器工具")]
    [InlineData("xkcs", "显卡测试")]
    public void Search_SupportsChineseInitials(string query, string name) => Assert.True(ToolSearchMatcher.Score(query, name) > 0);

    [Fact]
    public void ProtectedKey_PreservesUnreadableCiphertext_AndOtherConfiguration()
    {
        var provider = JsonSerializer.Deserialize<AiProvider>("""{"baseUrl":"https://example.test","apiKey":"dpapi:v1:invalid"}""")!;
        Assert.Equal("https://example.test", provider.BaseUrl);
        Assert.Empty(provider.ApiKey);
        Assert.True(provider.RequiresApiKeyReentry);
        Assert.Contains("dpapi:v1:invalid", JsonSerializer.Serialize(provider));
        provider.ApiKey = "replacement";
        Assert.False(provider.RequiresApiKeyReentry);
        Assert.DoesNotContain("replacement", JsonSerializer.Serialize(provider));
    }

    [Fact]
    public void MonitorCopy_DoesNotShareMutableValues()
    {
        var sample = new MonitorSample { CpuLoad = 25, CapturedAt = DateTimeOffset.UtcNow };
        var copy = sample.Copy();
        copy.CpuLoad = 100;
        Assert.Equal(25, sample.CpuLoad);
        Assert.Equal(sample.CapturedAt, copy.CapturedAt);
    }
}

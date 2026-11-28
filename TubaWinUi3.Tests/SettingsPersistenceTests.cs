using System.Collections.Concurrent;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 1.9.1 把设置落盘从「原地覆写」换成 File.Replace 后，部分 MSIX 用户（目标文件只读、
/// 被安全软件占用、或 ACL 不允许删除）会永久提示「保存失败」。这里锁定三条退路：
/// 只读属性清掉、备份被占不拖垮主文件、瞬时占用后能自愈。
/// </summary>
[Collection("GlobalConfigTests")]
public sealed class SettingsPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TubaSettings-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previous = ConfigManager.DataDirectoryOverride;
    private readonly ConcurrentQueue<string> _failures = new();

    public SettingsPersistenceTests()
    {
        AppSettings.Flush();
        Directory.CreateDirectory(_directory);
        ConfigManager.DataDirectoryOverride = _directory;
        AppSettings.InvalidateCache();
        AppSettings.SaveFailed += OnSaveFailed;
    }

    public void Dispose()
    {
        AppSettings.SaveFailed -= OnSaveFailed;
        AppSettings.Flush();
        AppSettings.InvalidateCache();
        ConfigManager.DataDirectoryOverride = _previous;
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_directory, true);
        }
        catch { }
    }

    private void OnSaveFailed(string message) => _failures.Enqueue(message);

    private string SettingsPath => ConfigManager.GetSettingsPath();

    [Fact]
    public void ReadOnlySettingsFileIsStillSaved()
    {
        AppSettings.Set("theme", "first");
        AppSettings.Flush();
        File.SetAttributes(SettingsPath, File.GetAttributes(SettingsPath) | FileAttributes.ReadOnly);

        AppSettings.Set("theme", "second");
        AppSettings.Flush();

        Assert.Null(AppSettings.LastSaveError);
        Assert.Contains("\"theme\":\"second\"", File.ReadAllText(SettingsPath));
        Assert.Empty(_failures);
    }

    [Fact]
    public void LockedBackupDoesNotFailThePrimaryWrite()
    {
        AppSettings.Set("answer", "1");
        AppSettings.Flush();
        var backup = SettingsPath + ".bak";
        File.WriteAllText(backup, "{\"answer\":\"old\"}");

        using (new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AppSettings.Set("answer", "2");
            AppSettings.Flush();

            Assert.Null(AppSettings.LastSaveError);
            Assert.Contains("\"answer\":\"2\"", File.ReadAllText(SettingsPath));
        }
        Assert.Empty(_failures);
    }

    [Fact]
    public void BusySettingsFileReportsOnceAndRecoversAfterRelease()
    {
        AppSettings.Set("scale", "100");
        AppSettings.Flush();

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AppSettings.Set("scale", "125");
            AppSettings.Flush();
            AppSettings.Set("scale", "150");
            AppSettings.Flush();

            Assert.NotNull(AppSettings.LastSaveError);
            Assert.True(SpinWait.SpinUntil(() => !_failures.IsEmpty, TimeSpan.FromSeconds(5)));
            Assert.Single(_failures);
        }

        AppSettings.Flush();

        Assert.Null(AppSettings.LastSaveError);
        Assert.Contains("\"scale\":\"150\"", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void BusySettingsFileIsNotQuarantinedOnLoad()
    {
        AppSettings.Set("answer", "1");
        AppSettings.Flush();
        AppSettings.InvalidateCache();

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Empty(AppSettings.Load());
        }

        Assert.True(File.Exists(SettingsPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.corrupt-*"));
        AppSettings.InvalidateCache();
        Assert.Equal("1", AppSettings.Get("answer"));
    }
}

extern alias backend;
using backend::TubaWinUI3.BackEnd;
using backend::TubaWinUI3.BackEnd.Models;

namespace TubaWinUi3.Tests;

/// <summary>
/// 主动拦截后端状态/事件落盘逻辑测试。
/// 仅覆盖文件级持久化（StateStore / EventLog），注册表屏蔽需管理员权限，不纳入常规单测。
/// </summary>
public class ActiveInterceptStateTests
{
    private static string CreateTempDataDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tubai_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void StateStore_NewEntry_SavesAndReloads()
    {
        var dir = CreateTempDataDir();
        try
        {
            var store = new StateStore(dir);
            Assert.False(store.BaselineEstablished);

            store.SetBaselineEstablished(true);
            store.Upsert(new InterceptStateEntry
            {
                Id = "HKCU|Default|Software\\Classes\\*\\shell\\test",
                Hive = RegHive.HKCU,
                View = RegView.Default,
                SubKey = "Software\\Classes\\*\\shell\\test",
                Kind = ContextMenuKind.ShellVerb,
                Name = "测试项",
                Command = "C:\\test.exe",
                DesiredState = DesiredState.Blocked,
                FirstSeenUtc = "2026-08-17T00:00:00Z",
                Source = "new",
            });
            store.Save();

            // 重新加载验证持久化
            var reloaded = new StateStore(dir);
            Assert.True(reloaded.BaselineEstablished);
            var entry = reloaded.ById()["HKCU|Default|Software\\Classes\\*\\shell\\test"];
            Assert.Equal(DesiredState.Blocked, entry.DesiredState);
            Assert.Equal("测试项", entry.Name);
            Assert.Equal(RegHive.HKCU, entry.Hive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StateStore_Upsert_DeduplicatesById()
    {
        var dir = CreateTempDataDir();
        try
        {
            var store = new StateStore(dir);
            for (int i = 0; i < 3; i++)
            {
                store.Upsert(new InterceptStateEntry
                {
                    Id = "same-id",
                    Name = $"v{i}",
                    DesiredState = DesiredState.Blocked,
                });
            }
            store.Save();

            var reloaded = new StateStore(dir);
            Assert.Single(reloaded.State.Entries);
            Assert.Equal("v2", reloaded.State.Entries[0].Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StateStore_RemoveWhere_RemovesMatching()
    {
        var dir = CreateTempDataDir();
        try
        {
            var store = new StateStore(dir);
            store.Upsert(new InterceptStateEntry { Id = "a", Name = "A", DesiredState = DesiredState.Blocked });
            store.Upsert(new InterceptStateEntry { Id = "b", Name = "B", DesiredState = DesiredState.Blocked });
            store.Upsert(new InterceptStateEntry { Id = "c", Name = "C", DesiredState = DesiredState.Allowed });

            store.RemoveWhere(id => id == "a" || id == "b");
            store.Save();

            var reloaded = new StateStore(dir);
            Assert.Single(reloaded.State.Entries);
            Assert.Equal("c", reloaded.State.Entries[0].Id);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EventLog_Append_ReadsBackNewestFirst()
    {
        var dir = CreateTempDataDir();
        try
        {
            var log = new EventLog(dir);
            log.Append(new InterceptEvent { Action = "Blocked", Id = "id1", Name = "n1" });
            log.Append(new InterceptEvent { Action = "Allowed", Id = "id2", Name = "n2" });

            var events = EventLog.ReadAll(dir);
            Assert.Equal(2, events.Count);
            // 倒序：最新在前
            Assert.Equal("Allowed", events[0].Action);
            Assert.Equal("id2", events[0].Id);
            Assert.Equal("Blocked", events[1].Action);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EventLog_ReadAll_EmptyWhenNoFile()
    {
        var dir = CreateTempDataDir();
        try
        {
            var events = EventLog.ReadAll(dir);
            Assert.Empty(events);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EventLog_ReadAll_SkipsCorruptLines()
    {
        var dir = CreateTempDataDir();
        try
        {
            var log = new EventLog(dir);
            log.Append(new InterceptEvent { Action = "Blocked", Id = "ok" });
            File.AppendAllText(Path.Combine(dir, "events.jsonl"), "{corrupt line}\n");

            var events = EventLog.ReadAll(dir);
            Assert.Single(events);
            Assert.Equal("Blocked", events[0].Action);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ClsidResolver_NormalizeServerPath_StripsQuotesAndArgs()
    {
        // 纯函数路径规范化测试（不访问注册表）
        Assert.Equal(@"C:\Program Files\App\a.dll",
            backend::TubaWinUI3.BackEnd.ClsidResolver.NormalizeServerPath(@"""C:\Program Files\App\a.dll"" -embedding"));
        Assert.Equal(@"C:\Windows\System32\b.dll",
            backend::TubaWinUI3.BackEnd.ClsidResolver.NormalizeServerPath(@"C:\Windows\System32\b.dll,"));
    }
}

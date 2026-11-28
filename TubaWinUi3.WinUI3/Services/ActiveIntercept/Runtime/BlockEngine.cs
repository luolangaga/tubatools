using System.Text.Json;
using Microsoft.Win32;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 屏蔽/放行原语（ContextMenuMgr 的 enable/disable 手法）：
/// - Shell 扩展：在 HKCU/HKLM 的 Software\...\Shell Extensions\Blocked 写入 CLSID 值。
/// - Shell 命令：在 verb 子键写入 LegacyDisable。
/// 所有写操作前先做值快照备份（可回滚），并对实际写入做校验。
/// </summary>
public sealed class BlockEngine
{
    private const string BlockedRoot = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
    private const string DisableNote = "由流氓软件克星主动拦截禁用";

    private readonly string _backupDir;

    public BlockEngine(string dataDir)
    {
        _backupDir = Path.Combine(dataDir, "backups");
        Directory.CreateDirectory(_backupDir);
    }

    /// <summary>当前是否处于被屏蔽状态。</summary>
    public bool IsBlocked(ContextMenuItem item)
    {
        if (!item.Writable) return false;

        if (item.Kind == ContextMenuKind.ShellExtension)
        {
            if (string.IsNullOrWhiteSpace(item.Clsid)) return false;
            using var key = RegistryAccess.OpenSubKey(item.Hive, item.View, BlockedRoot, writable: false);
            return RegistryAccess.HasValue(key, item.Clsid);
        }
        else
        {
            using var key = RegistryAccess.OpenSubKey(item.Hive, item.View, item.SubKey, writable: false);
            return RegistryAccess.HasValue(key, "LegacyDisable");
        }
    }

    /// <summary>屏蔽条目。返回是否发生了实际写入。</summary>
    public bool Block(ContextMenuItem item, out string note)
    {
        note = "";
        if (!item.Writable) return false;

        if (item.Kind == ContextMenuKind.ShellExtension)
        {
            if (string.IsNullOrWhiteSpace(item.Clsid)) return false;
            var target = new ActionTarget(item.Hive, item.View, BlockedRoot, item.Clsid);
            if (RegistryAccess.HasValue(RegistryAccess.OpenSubKey(item.Hive, item.View, BlockedRoot, writable: false), item.Clsid))
            {
                note = "已屏蔽（Blocked 列表已存在）";
                return false;
            }
            WriteValue(target, DisableNote, RegistryValueKind.String);
            note = $"写入 Shell Extensions\\Blocked（{item.Clsid}）";
            return true;
        }
        else
        {
            var target = new ActionTarget(item.Hive, item.View, item.SubKey, "LegacyDisable");
            if (RegistryAccess.HasValue(RegistryAccess.OpenSubKey(item.Hive, item.View, item.SubKey, writable: false), "LegacyDisable"))
            {
                note = "已屏蔽（LegacyDisable 已存在）";
                return false;
            }
            WriteValue(target, "", RegistryValueKind.String);
            note = "写入 LegacyDisable";
            return true;
        }
    }

    /// <summary>解除屏蔽。返回是否发生了实际删除。</summary>
    public bool Unblock(ContextMenuItem item, out string note)
    {
        note = "";
        if (!item.Writable) return false;

        if (item.Kind == ContextMenuKind.ShellExtension)
        {
            if (string.IsNullOrWhiteSpace(item.Clsid)) return false;
            var target = new ActionTarget(item.Hive, item.View, BlockedRoot, item.Clsid);
            if (!RegistryAccess.HasValue(RegistryAccess.OpenSubKey(item.Hive, item.View, BlockedRoot, writable: false), item.Clsid))
            {
                note = "已解除（Blocked 列表不存在该 CLSID）";
                return false;
            }
            DeleteValue(target);
            note = $"删除 Shell Extensions\\Blocked\\{item.Clsid}";
            return true;
        }
        else
        {
            var target = new ActionTarget(item.Hive, item.View, item.SubKey, "LegacyDisable");
            if (!RegistryAccess.HasValue(RegistryAccess.OpenSubKey(item.Hive, item.View, item.SubKey, writable: false), "LegacyDisable"))
            {
                note = "已解除（LegacyDisable 不存在）";
                return false;
            }
            DeleteValue(target);
            note = "删除 LegacyDisable";
            return true;
        }
    }

    // ---------- 底层写入（带备份） ----------

    private readonly record struct ActionTarget(RegHive Hive, RegView View, string SubKey, string ValueName);

    private void WriteValue(ActionTarget target, string value, RegistryValueKind kind)
    {
        Backup(target);
        using var root = RegistryAccess.OpenBase(target.Hive, target.View, writable: true);
        using var key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree);
        key.SetValue(target.ValueName, value, kind);
    }

    private void DeleteValue(ActionTarget target)
    {
        Backup(target);
        using var root = RegistryAccess.OpenBase(target.Hive, target.View, writable: true);
        using var key = root.OpenSubKey(target.SubKey, writable: true);
        key?.DeleteValue(target.ValueName, throwOnMissingValue: false);
    }

    /// <summary>写前快照到 backups/ 下的 JSON，供主程序/恢复中心回滚。</summary>
    private void Backup(ActionTarget target)
    {
        try
        {
            var backup = new RegistryValueBackup
            {
                Id = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..24],
                Hive = target.Hive,
                View = target.View,
                SubKey = target.SubKey,
                ValueName = target.ValueName,
                CreatedUtc = DateTime.UtcNow.ToString("o"),
            };

            using var key = RegistryAccess.OpenSubKey(target.Hive, target.View, target.SubKey, writable: false);
            if (key is null)
            {
                backup.Existed = false;
            }
            else
            {
                var names = key.GetValueNames();
                var actual = names.FirstOrDefault(n => string.Equals(n, target.ValueName, StringComparison.OrdinalIgnoreCase));
                backup.Existed = actual is not null;
                if (backup.Existed)
                {
                    try
                    {
                        backup.Value = Convert.ToString(key.GetValue(actual, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
                        backup.ValueKind = key.GetValueKind(actual).ToString();
                    }
                    catch { }
                }
            }

            var json = JsonSerializer.Serialize(backup, BackEndJsonContext.Default.RegistryValueBackup);
            File.WriteAllText(Path.Combine(_backupDir, backup.Id + ".json"), json);
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"备份失败（继续执行）：{ex.Message}");
        }
    }
}


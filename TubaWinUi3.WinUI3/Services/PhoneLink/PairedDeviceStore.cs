using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services;

/// <summary>已配对设备的公开信息（电脑端设备列表用，不含任何凭据）。</summary>
public sealed record PhoneLinkDeviceInfo(string Id, string Name, DateTime Since, DateTime ExpiresAt);

/// <summary>落盘的配对记录：只保存令牌的 SHA-256 摘要，明文令牌永远不写磁盘。</summary>
internal sealed class PairedDeviceRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Since { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string TokenHash { get; set; } = "";
}

/// <summary>
/// 配对令牌的持久化存储：服务重启后手机不必重新配对（以前令牌只在内存，重启即失效）。
/// 令牌是 24 字节随机数，落盘只存摘要；有效期 <see cref="Lifetime"/>，过期后需重新配对。
/// </summary>
internal static class PairedDeviceStore
{
    internal const string SettingKey = "PhoneLinkPairedDevices";

    /// <summary>令牌有效期：过期后手机端会收到 401 并用保存的配对码重新配对。</summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    private static readonly object Gate = new();
    private static List<PairedDeviceRecord>? _records;

    private static void EnsureLoadedLocked()
    {
        if (_records is not null) return;
        _records = LoadFromSettings();
    }

    private static List<PairedDeviceRecord> LoadFromSettings()
    {
        try
        {
            var json = AppSettings.Get(SettingKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var list = JsonSerializer.Deserialize<List<PairedDeviceRecord>>(json);
                if (list is not null)
                    return list.Where(r => !string.IsNullOrEmpty(r.TokenHash)).ToList();
            }
        }
        catch
        {
            // 设置损坏时按「没有已配对设备」处理，手机重新配对即可
        }
        return [];
    }

    private static void SaveLocked()
    {
        try
        {
            AppSettings.Set(SettingKey, JsonSerializer.Serialize(_records));
            AppSettings.Flush();
        }
        catch
        {
        }
    }

    /// <summary>签发新令牌并落盘；返回明文令牌（只在此刻存在）。</summary>
    public static string Issue(string deviceName, out PairedDeviceRecord issued)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        issued = new PairedDeviceRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(deviceName) ? "手机" : deviceName.Trim(),
            Since = DateTime.Now,
            ExpiresAt = DateTime.Now.Add(Lifetime),
            TokenHash = HashToken(token)
        };
        lock (Gate)
        {
            EnsureLoadedLocked();
            _records!.Add(issued);
            SaveLocked();
        }
        return token;
    }

    /// <summary>校验令牌（固定时间比较摘要）。过期记录会被顺手清掉。</summary>
    public static bool TryAuthorize(string token, out PairedDeviceRecord record)
    {
        record = null!;
        if (string.IsNullOrEmpty(token)) return false;
        var hash = HashToken(token);
        PairedDeviceRecord? expired = null;
        lock (Gate)
        {
            EnsureLoadedLocked();
            foreach (var candidate in _records!)
            {
                if (!FixedTimeEqualsHex(candidate.TokenHash, hash)) continue;
                if (candidate.ExpiresAt <= DateTime.Now)
                {
                    expired = candidate;
                    break;
                }
                record = candidate;
                return true;
            }
            if (expired is not null)
            {
                _records!.Remove(expired);
                SaveLocked();
            }
        }
        return false;
    }

    public static bool Contains(string token) => TryAuthorize(token, out _);

    public static IReadOnlyList<PhoneLinkDeviceInfo> Snapshot()
    {
        lock (Gate)
        {
            EnsureLoadedLocked();
            return _records!
                .OrderBy(r => r.Since)
                .Select(r => new PhoneLinkDeviceInfo(r.Id, r.Name, r.Since, r.ExpiresAt))
                .ToList();
        }
    }

    /// <summary>撤销单台设备。返回是否真的删掉了记录。</summary>
    public static bool Revoke(string deviceId)
    {
        lock (Gate)
        {
            EnsureLoadedLocked();
            var removed = _records!.RemoveAll(r => string.Equals(r.Id, deviceId, StringComparison.Ordinal)) > 0;
            if (removed) SaveLocked();
            return removed;
        }
    }

    public static bool RevokeByToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var hash = HashToken(token);
        lock (Gate)
        {
            EnsureLoadedLocked();
            var removed = _records!.RemoveAll(r => FixedTimeEqualsHex(r.TokenHash, hash)) > 0;
            if (removed) SaveLocked();
            return removed;
        }
    }

    public static void RevokeAll()
    {
        lock (Gate)
        {
            _records = [];
            try { AppSettings.Remove(SettingKey); } catch { }
        }
    }

    internal static int Count
    {
        get
        {
            lock (Gate)
            {
                EnsureLoadedLocked();
                return _records!.Count;
            }
        }
    }

    /// <summary>供测试模拟进程重启：丢弃内存缓存，下次访问重新从设置读取。</summary>
    internal static void ReloadForTests()
    {
        lock (Gate) _records = null;
    }

    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool FixedTimeEqualsHex(string a, string b)
    {
        try
        {
            var left = Convert.FromHexString(a);
            var right = Convert.FromHexString(b);
            return CryptographicOperations.FixedTimeEquals(left, right);
        }
        catch
        {
            return false;
        }
    }
}

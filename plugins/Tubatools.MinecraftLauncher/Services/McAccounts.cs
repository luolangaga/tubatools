using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tubatools.MinecraftLauncher.Services;

internal sealed record McAccount(
    string Name,
    string Uuid,
    string Type = "离线",
    [property: JsonConverter(typeof(McSecretConverter))] string? AccessToken = null,
    [property: JsonConverter(typeof(McSecretConverter))] string? RefreshToken = null,
    DateTime ExpiresAt = default,
    string OAuthClientId = "")
{
    public bool IsMicrosoft => string.Equals(Type, "Microsoft", StringComparison.OrdinalIgnoreCase);
    public string TypeLabel => IsMicrosoft ? "微软正版" : "离线账户";
}

/// <summary>离线账户：UUID = MD5("OfflinePlayer:" + name)，与 Minecraft 离线模式一致。</summary>
internal static class McOfflineUuid
{
    public static string Create(string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length is >= 3 and <= 16 &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}

internal sealed class McAccountStore
{
    private sealed class Payload
    {
        public List<McAccount> Accounts { get; set; } = [];
        public string? Selected { get; set; }
        public string? SelectedUuid { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _path;
    private readonly List<McAccount> _accounts = [];

    public McAccountStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "accounts.json");
        Load();
    }

    public IReadOnlyList<McAccount> Accounts => _accounts;
    public McAccount? Selected { get; private set; }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var payload = JsonSerializer.Deserialize<Payload>(File.ReadAllText(_path), JsonOptions);
            if (payload is null) return;
            foreach (var account in payload.Accounts)
            {
                if (string.IsNullOrWhiteSpace(account.Name)) continue;
                _accounts.Add(account with
                {
                    Uuid = string.IsNullOrWhiteSpace(account.Uuid) ? McOfflineUuid.Create(account.Name) : account.Uuid,
                    Type = string.IsNullOrWhiteSpace(account.Type)
                        ? "离线"
                        : account.Type
                });
            }

            Selected = (!string.IsNullOrWhiteSpace(payload.SelectedUuid)
                           ? _accounts.FirstOrDefault(a =>
                               string.Equals(a.Uuid, payload.SelectedUuid, StringComparison.OrdinalIgnoreCase))
                           : null)
                       ?? _accounts.FirstOrDefault(a =>
                           string.Equals(a.Name, payload.Selected, StringComparison.OrdinalIgnoreCase))
                       ?? _accounts.FirstOrDefault();
        }
        catch
        {
            // 损坏的账户文件按空处理，避免启动器打不开。
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Payload
            {
                Accounts = _accounts.ToList(),
                Selected = Selected?.Name,
                SelectedUuid = Selected?.Uuid
            }, JsonOptions));
            File.Move(temp, _path, true);
        }
        catch
        {
            // 忽略写入失败，账户仍留在本次会话内。
        }
    }

    public McAccount Add(string name)
    {
        if (!McOfflineUuid.IsValidName(name))
            throw new ArgumentException("用户名需为 3–16 位字母、数字或下划线。");
        var existing = _accounts.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Selected = existing;
            Save();
            return existing;
        }

        var account = new McAccount(name, McOfflineUuid.Create(name));
        _accounts.Add(account);
        Selected = account;
        Save();
        return account;
    }

    public McAccount UpsertMicrosoft(McMicrosoftSession session)
    {
        var existing = _accounts.FirstOrDefault(a =>
            a.IsMicrosoft && string.Equals(a.Uuid, session.Uuid, StringComparison.OrdinalIgnoreCase));
        var account = new McAccount(session.Name, session.Uuid, "Microsoft",
            session.AccessToken, session.RefreshToken, session.ExpiresAt, session.ClientId);
        if (existing is not null)
            _accounts[_accounts.IndexOf(existing)] = account;
        else
            _accounts.Add(account);
        Selected = account;
        Save();
        return account;
    }

    public void UpdateTokens(McAccount account, McMicrosoftSession session)
    {
        var updated = account with
        {
            Name = session.Name,
            AccessToken = session.AccessToken,
            RefreshToken = session.RefreshToken,
            ExpiresAt = session.ExpiresAt,
            OAuthClientId = session.ClientId
        };
        var index = _accounts.FindIndex(a =>
            string.Equals(a.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _accounts[index] = updated;
        else _accounts.Add(updated);
        if (string.Equals(Selected?.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
            Selected = updated;
        Save();
    }

    public void Select(McAccount account)
    {
        Selected = account;
        Save();
    }

    public void Remove(McAccount account)
    {
        _accounts.RemoveAll(a => string.Equals(a.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(Selected?.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
            Selected = _accounts.FirstOrDefault();
        Save();
    }
}

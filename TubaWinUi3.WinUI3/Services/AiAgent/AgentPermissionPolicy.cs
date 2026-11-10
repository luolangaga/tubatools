using TubaWinUi3.Services;

namespace TubaWinUi3.Services.AiAgent;

public enum AgentPermissionMode { FullAccess, Ask }
internal sealed record AgentPermissionRule(string action, string resource, string effect);

internal static class AgentPermissionPolicy
{
    private const string SettingKey = "AgentPermissions";
    private const string VersionKey = "AgentPermissionsVersion";
    private const int CurrentVersion = 2;

    internal static IReadOnlyList<AgentPermissionRule> Rules(AgentPermissionMode mode) => mode == AgentPermissionMode.FullAccess
        ? [new("*", "*", "allow")]
        : [new("*", "*", "ask"), .. new[] { "read", "glob", "grep", "skill", "question" }.Select(a => new AgentPermissionRule(a, "*", "allow"))];

    /// <summary>The product default is full access; the setting only exists to remember an opt-out.</summary>
    internal static AgentPermissionMode Default => AgentPermissionMode.FullAccess;

    internal static AgentPermissionMode Read() => AppSettings.Get(SettingKey) == "ask" ? AgentPermissionMode.Ask : Default;

    internal static void Write(AgentPermissionMode mode) =>
        AppSettings.Set(SettingKey, mode == AgentPermissionMode.Ask ? "ask" : "full");

    /// <summary>
    /// Builds before the default flipped wrote "ask" themselves, so an existing install kept
    /// asking for every tool. Reset that once; afterwards an explicit choice is preserved.
    /// </summary>
    internal static void MigrateLegacyDefault()
    {
        if (AppSettings.GetInt(VersionKey, 1) >= CurrentVersion) return;
        AppSettings.Set(VersionKey, CurrentVersion);
        if (AppSettings.Get(SettingKey) == "ask") AppSettings.Remove(SettingKey);
    }
}

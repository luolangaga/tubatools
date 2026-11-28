using System.Text.Json;
using System.Text.Json.Serialization;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

/// <summary>程序信任策略。</summary>
public enum TrustPolicyKind
{
    /// <summary>每次都询问（默认：拦截后等待用户审核）。</summary>
    Ask = 0,
    /// <summary>总是放行：该程序新增的右键菜单自动加入白名单。</summary>
    Allow = 1,
    /// <summary>总是拦截：该程序新增的右键菜单自动屏蔽，不通知。</summary>
    Block = 2,
}

/// <summary>单条信任策略。</summary>
public sealed class TrustPolicyEntry
{
    /// <summary>程序路径（exe 路径，大小写不敏感比较）。</summary>
    public string ExePath { get; set; } = "";

    /// <summary>策略。</summary>
    public TrustPolicyKind Policy { get; set; } = TrustPolicyKind.Ask;

    /// <summary>用户备注（可选）。</summary>
    public string Note { get; set; } = "";

    /// <summary>创建时间。</summary>
    public string CreatedUtc { get; set; } = "";
}

/// <summary>信任策略文件。</summary>
public sealed class TrustPolicyFile
{
    public List<TrustPolicyEntry> Policies { get; set; } = [];
}


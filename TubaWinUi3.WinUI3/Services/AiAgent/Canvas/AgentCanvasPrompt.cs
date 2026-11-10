using System.Text.RegularExpressions;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// The marker prepended to a teaching turn. The detailed rules live in the workspace
/// AGENTS.md and the <c>tuba-canvas</c> skill so every user message stays readable in the
/// transcript instead of carrying a wall of instructions.
/// </summary>
public static class AgentCanvasPrompt
{
    private const string PrefixLead = "〔教学模式 · 画布 ";
    private const string PrefixTail = "〕";
    private const string PrefixNote = "（本轮是教学任务：按 tuba-canvas 技能把讲解更新到上面的画布文件。）";
    private const string TurnHintLead = "（本条来自画布页面的按钮。更新 ";
    private const string TurnHintTail = " 后简要说明变化。）";

    public static string Prefix(string sessionId) =>
        PrefixLead + AgentCanvasStore.RelativeIndex(sessionId) + PrefixTail + "\n" + PrefixNote + "\n";

    public static string CanvasTurnHint(string sessionId) =>
        "\n" + TurnHintLead + AgentCanvasStore.RelativeIndex(sessionId) + TurnHintTail;

    /// <summary>
    /// Removes the teaching markers from a user message before it is shown. The agent still
    /// receives the full text; only the transcript is cleaned up.
    /// </summary>
    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var value = text;
        var start = value.IndexOf(PrefixLead, StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = value.IndexOf(PrefixTail, start, StringComparison.Ordinal);
            if (end >= 0)
            {
                value = value.Remove(start, end + PrefixTail.Length - start);
                value = value.Replace(PrefixNote, "", StringComparison.Ordinal);
            }
        }
        if (value.Contains(TurnHintLead, StringComparison.Ordinal))
            value = Regex.Replace(value, Regex.Escape(TurnHintLead) + ".*?" + Regex.Escape(TurnHintTail), "", RegexOptions.Singleline);
        return value.Trim();
    }
}

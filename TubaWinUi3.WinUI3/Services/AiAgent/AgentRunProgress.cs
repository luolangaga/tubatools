namespace TubaWinUi3.Services.AiAgent;

public sealed record AgentRunProgress(string Phase, string Detail = "");

/// <summary>Measures actual stream changes; time spent answering/approving is excluded.</summary>
internal sealed class AgentRunMonitor
{
    private string _revision = "";
    private TimeSpan _lastChange;
    private long _signalRevision;
    internal void Resume(TimeSpan elapsed) => _lastChange = elapsed;
    internal AgentRunProgress Observe(IReadOnlyList<AgentMessage> messages, TimeSpan elapsed, AgentSignal? signal = null)
    {
        var revision = string.Join("|", messages.Select(m => m.Id + ":" + m.Revision));
        if (revision != _revision) { _revision = revision; _lastChange = elapsed; }
        if (signal is not null && signal.Revision != _signalRevision) { _signalRevision = signal.Revision; _lastChange = elapsed; }
        var last = messages.LastOrDefault();
        var tool = messages.TakeLast(5).SelectMany(m => m.Content ?? []).Select(p => p.Operation).LastOrDefault(o => o is not null && o.Status is not ("completed" or "error" or "cancelled"));
        // A real running command has its own tool timeout; don't confuse it with a silent model.
        if (tool?.Status == "running") { _lastChange = elapsed; return new("tool", tool.Label + " · " + tool.Target); }
        // V2 message snapshots can lag streamed reasoning and compaction. Silence is a status, never an interrupt.
        if (elapsed - _lastChange >= TimeSpan.FromMinutes(3)) return new("stalled");
        if (signal is { Revision: > 0 } && signal.Phase is "thinking" or "responding" or "compacting" or "retrying" or "preparing-tool") return new(signal.Phase, signal.Detail);
        if (last?.Role != "assistant") return new("waiting");
        if (tool is not null) return new("tool", tool.Label + (tool.Target.Length > 0 ? " · " + tool.Target : ""));
        return new(last.Thinking ? "thinking" : last.Completed ? "waiting" : "responding");
    }
}

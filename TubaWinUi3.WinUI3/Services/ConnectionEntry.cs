namespace TubaWinUi3.Services;

public sealed class ConnectionEntry
{
    public string ProcessName { get; init; } = "";
    public int ProcessId { get; init; }
    public string LocalAddress { get; init; } = "";
    public int LocalPort { get; init; }
    internal (int Pid, string Local, int LocalPort, string Remote, int RemotePort) Identity
        => (ProcessId, LocalAddress, LocalPort, RemoteAddress, RemotePort);
    internal bool SameDisplay(ConnectionEntry other) => Identity == other.Identity &&
        ProcessName == other.ProcessName && AdapterName == other.AdapterName && AdapterType == other.AdapterType;
    public string RemoteAddress { get; init; } = "";
    public int RemotePort { get; init; }
    public string AdapterName { get; init; } = "";
    public string AdapterType { get; init; } = "";
}


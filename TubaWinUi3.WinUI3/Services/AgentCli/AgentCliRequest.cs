namespace TubaWinUi3.Services.AgentCli;

internal sealed record AgentCliRequest(string Command, string Output, string[] Arguments)
{
    internal const string Switch = "--agent-cli";
    internal static AgentCliRequest Parse(string[] args)
    {
        if (args.Length < 4 || !args[0].Equals(Switch, StringComparison.OrdinalIgnoreCase)
            || args[2] != "--output" || !Path.IsPathFullyQualified(args[3]) || args[3].StartsWith("\\\\"))
            throw new ArgumentException("用法：--agent-cli <help|hardware|sensors|disks|cpuz|tools|run> --output <本地绝对路径.json> [参数]");
        var command = args[1].ToLowerInvariant();
        if (command is not ("help" or "hardware" or "sensors" or "disks" or "cpuz" or "tools" or "run"))
            throw new ArgumentException("未知命令。请运行 help。");
        var rest = args.Skip(4).ToArray();
        if (command != "run" && rest.Length != 0) throw new ArgumentException("该命令不接受额外参数。");
        if (command == "run") ValidateRun(rest);
        return new(command, Path.GetFullPath(args[3]), rest);
    }

    internal static (string Tool, int Timeout, string[] Args) ValidateRun(string[] args)
    {
        if (args.Length < 5 || args[1] != "--allow-execution" || args[2] != "--timeout"
            || !int.TryParse(args[3], out var seconds) || seconds < 1 || seconds > 300 || args[4] != "--")
            throw new ArgumentException("用法：run --output <路径> <工具ID> --allow-execution --timeout <1..300秒> -- <工具参数...>。执行前需获得用户对该操作的授权。");
        return (args[0].ToLowerInvariant(), seconds, args.Skip(5).ToArray());
    }
}

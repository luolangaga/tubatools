namespace TubaWinUi3.Services.AgentCli;

internal sealed record AgentCliRequest(string Command, string Output, string[] Arguments)
{
    internal const string Switch = "--agent-cli";
    internal static AgentCliRequest Parse(string[] args)
    {
        if (args.Length < 4 || !args[0].Equals(Switch, StringComparison.OrdinalIgnoreCase)
            || args[2] != "--output" || !Path.IsPathFullyQualified(args[3]) || args[3].StartsWith("\\\\"))
            throw new ArgumentException("用法：--agent-cli <help|hardware> --output <本地绝对路径.json> [参数]");
        var command = args[1].ToLowerInvariant();
        if (command is not ("help" or "hardware"))
            throw new ArgumentException("未知命令。请运行 help。");
        var rest = args.Skip(4).ToArray();
        if (rest.Length != 0) throw new ArgumentException("该命令不接受额外参数。");
        return new(command, Path.GetFullPath(args[3]), rest);
    }

}

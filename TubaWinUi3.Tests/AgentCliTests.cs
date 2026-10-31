using TubaWinUi3.Services.AgentCli;

namespace TubaWinUi3.Tests;

public sealed class AgentCliTests
{
    [Fact]
    public async Task BrokerEofDoesNotTurnSuccessfulReportIntoBrokenPipeError()
    {
        var name = "Tuba.QueryResponseTest." + Guid.NewGuid().ToString("N");
        var server = AgentQueryBroker.CreateServer(name);
        var serve = Task.Run(async () =>
        {
            using (server)
            {
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server, leaveOpen: true);
                Assert.Equal("hardware", await reader.ReadLineAsync());
                var bytes = System.Text.Encoding.UTF8.GetBytes("{\"ok\":true,\"data\":{\"value\":42}}");
                await server.WriteAsync(bytes);
                await server.FlushAsync();
            }
        });
        using var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        var data = await AgentQueryBroker.ExchangeAsync(client, "hardware");
        Assert.Equal(42, data.GetProperty("value").GetInt32());
        await serve;
    }
    [Fact]
    public async Task QueryPipeUsesUserSidAndAcceptsLimitedClient()
    {
        var name = "Tuba.QueryTest." + Guid.NewGuid().ToString("N");
        using var server = AgentQueryBroker.CreateServer(name);
        using var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
        await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync(3000));
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        Assert.Equal(identity.User, System.IO.Pipes.PipesAclExtensions.GetAccessControl(client).GetOwner(typeof(System.Security.Principal.SecurityIdentifier)));
    }
    [Theory]
    [InlineData("cpuz", true)]
    [InlineData("sensors", true)]
    [InlineData("disks", true)]
    [InlineData("hardware", true)]
    [InlineData("run", false)]
    [InlineData("powershell", false)]
    [InlineData("cpuz --output C:\\Windows\\out", false)]
    public void ElevatedBrokerOnlyAcceptsFixedReadOnlyCommands(string command, bool allowed)
        => Assert.Equal(allowed, AgentQueryBroker.IsReadOnlyCommand(command));

    [Fact]
    public void BrokerWorkerUsesHeadlessLaunchPath()
        => Assert.True(AgentCliService.IsInvocation(["toolbox.exe", AgentQueryBroker.WorkerSwitch]));

    [Fact]
    public void ParsesChineseOutputAndPreservesArgumentBoundaries()
    {
        var request = AgentCliRequest.Parse(["--agent-cli", "RUN", "--output", @"C:\测试 结果\输出.json", "furmark", "--allow-execution", "--timeout", "45", "--", "--name", "含 空格", "$(whoami)"]);
        var run = AgentCliRequest.ValidateRun(request.Arguments);
        Assert.Equal("run", request.Command);
        Assert.Equal("含 空格", run.Args[1]);
        Assert.Equal("$(whoami)", run.Args[2]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("301")]
    [InlineData("-1")]
    [InlineData("oops")]
    public void RejectsUnboundedRuns(string seconds)
        => Assert.Throws<ArgumentException>(() => AgentCliRequest.ValidateRun(["furmark", "--allow-execution", "--timeout", seconds, "--"]));

    [Fact]
    public void RejectsImplicitExecutionAndNetworkOutput()
    {
        Assert.Throws<ArgumentException>(() => AgentCliRequest.ValidateRun(["furmark", "--timeout", "30", "--"]));
        Assert.Throws<ArgumentException>(() => AgentCliRequest.Parse(["--agent-cli", "sensors", "--output", @"\\server\share\out.json"]));
        Assert.Throws<ArgumentException>(() => AgentCliRequest.Parse(["--agent-cli", "sensors", "--output", "out.json"]));
        Assert.Throws<ArgumentException>(() => AgentCliRequest.Parse(["--agent-cli", "sensors", "--output", @"C:\out.json", "unexpected"]));
    }

    [Fact]
    public void RejectsUnknownExecutableAndAgent()
    {
        Assert.Throws<ArgumentException>(() => AgentCliService.ResolveTool("powershell"));
        Assert.Throws<ArgumentException>(() => AgentSkillInstaller.InstallCommand(@"C:\skill", "codex;whoami"));
        Assert.Contains(@"'C:\O''Brien\skill'", AgentSkillInstaller.InstallCommand(@"C:\O'Brien\skill", "codex"));
    }

    [Fact]
    public async Task InvalidCommandReturnsJsonFailure()
    {
        var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            Assert.Equal(1, await AgentCliService.ExecuteAsync(["toolbox.exe", "--agent-cli", "bogus", "--output", output]));
            using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(output));
            Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("ArgumentException", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally { File.Delete(output); }
    }
}

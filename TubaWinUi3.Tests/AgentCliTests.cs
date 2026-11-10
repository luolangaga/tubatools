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
    [InlineData("cpuz", false)]
    [InlineData("sensors", false)]
    [InlineData("disks", false)]
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
    public void HardwareQuery_PreservesChineseOutputPath_AndRejectsExtraArguments()
    {
        var request = AgentCliRequest.Parse(["--agent-cli", "HARDWARE", "--output", @"C:\测试 结果\输出.json"]);
        Assert.Equal("hardware", request.Command); Assert.Empty(request.Arguments);
        Assert.Throws<ArgumentException>(() => AgentCliRequest.Parse(["--agent-cli", "hardware", "--output", @"C:\out.json", "unexpected"]));
        Assert.Throws<ArgumentException>(() => AgentCliRequest.Parse(["--agent-cli", "hardware", "--output", @"\\server\share\out.json"]));
    }
    [Theory]
    [InlineData("sensors")][InlineData("disks")][InlineData("cpuz")][InlineData("tools")][InlineData("run")]
    public void RemovedDiagnosticApis_AreRejected(string command)
        => Assert.Throws<ArgumentException>(() => AgentCliRequest.Parse(["--agent-cli", command, "--output", @"C:\out.json"]));

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

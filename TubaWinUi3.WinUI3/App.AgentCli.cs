namespace TubaWinUi3;

public partial class App
{
    private async void RunAgentCliAndExit(string[] args)
    {
        if (args.Length == 2 && args[1].Equals(Services.AgentCli.AgentQueryBroker.WorkerSwitch, StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => Services.AgentCli.AgentQueryBroker.ServeAsync(standalone: true));
            Exit();
            return;
        }
        Environment.ExitCode = await Services.AgentCli.AgentCliService.ExecuteAsync(args);
        Exit();
    }
}

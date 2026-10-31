using System.Reflection;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Telemetry;

namespace TubaWinUi3.Tests;

public class ToolLaunchTelemetryTests
{
    [Theory]
    [InlineData(@"C:\Tools\处理器工具\CPUZ\x64\cpuz.exe", @"C:\Tools", "CPUZ")]
    [InlineData(@"C:\Tools\处理器工具\CPUZ\x86\cpuz.exe", @"C:\Tools", "CPUZ")]
    [InlineData(@"C:\Tools\网络工具\测速.cmd", @"C:\Tools", "测速")]
    [InlineData(@"C:\Users\luolan\Desktop\MyTool.exe", @"C:\Tools", "MyTool")]
    [InlineData(@"D:\私人目录\MyTool.exe", @"C:\Tools", "MyTool")]
    [InlineData(@"C:\Toolset\用户目录\MyTool.exe", @"C:\Tools", "MyTool")]
    [InlineData("", @"C:\Tools", "")]
    public void ExternalToolId_GroupsArchitecturesAndNeverUploadsPaths(string file, string root, string expected)
    {
        var id = TelemetryService.BuildExternalToolId(file, root);
        Assert.Equal(expected, id);
        Assert.DoesNotContain('\\', id);
        Assert.DoesNotContain('/', id);
        Assert.DoesNotContain("luolan", id);
    }

    [Fact]
    public void BuiltinOpen_ReportsExactlyOnceWithoutWaitingForWindowToClose()
    {
        Capture(events =>
        {
            var pending = new TaskCompletionSource();
            var tool = new FakeTool(() => pending.Task);
            Assert.Same(pending.Task, tool.ExecuteTrackedAsync(new()));
            var report = Assert.IsType<EventTelemetry>(Assert.Single(events));
            Assert.Equal("tool_launch", report.Name);
            Assert.Equal("screen-test", report.Properties["tool"]);
            Assert.Equal("1", report.Properties["builtin"]);
            Assert.Equal(1, tool.Calls);
            var transmission = new Transmission(new Uri(AxiomTelemetryConfig.Endpoint), [report], TimeSpan.FromSeconds(10));
            using var raw = new MemoryStream(AxiomTelemetryPayload.ConvertBatch(transmission.Content, transmission.ContentEncoding));
            using var gzip = new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress);
            using var payload = System.Text.Json.JsonDocument.Parse(gzip);
            Assert.Equal("tool_launch", payload.RootElement[0].GetProperty("name").GetString());
            Assert.Equal("screen-test", payload.RootElement[0].GetProperty("tool").GetString());
            pending.SetResult();
        });
    }

    [Fact]
    public void FailedOrCancelledOpen_DoesNotCreateUsageRecord()
    {
        Capture(events =>
        {
            var failed = Task.FromException(new InvalidOperationException("failed"));
            new FakeTool(() => failed).ExecuteTrackedAsync(new());
            _ = failed.Exception;
            new FakeTool(() => Task.FromCanceled(new CancellationToken(true))).ExecuteTrackedAsync(new());
            Exception? thrown = null;
            try { new FakeTool(() => throw new InvalidOperationException()).ExecuteTrackedAsync(new()); }
            catch (Exception error) { thrown = error; }
            Assert.IsType<InvalidOperationException>(thrown);
            Assert.Empty(events);
        });
    }

    [Fact]
    public void DisabledTelemetry_DoesNotPreventToolOpening()
    {
        Capture(events =>
        {
            var tool = new FakeTool(() => Task.CompletedTask);
            tool.ExecuteTrackedAsync(new());
            Assert.Equal(1, tool.Calls);
            Assert.Empty(events);
        }, enabled: false);
    }

    private static void Capture(Action<List<ITelemetry>> action, bool enabled = true)
    {
        var field = typeof(TelemetryService).GetField("_client", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var channel = new CaptureChannel();
        using var config = new TelemetryConfiguration
        {
            ConnectionString = "InstrumentationKey=ff9e238f-9642-4aa9-a217-734434e79872",
            TelemetryChannel = channel,
        };
        try
        {
            field.SetValue(null, enabled ? new TelemetryClient(config) : null);
            action(channel.Events);
        }
        finally { field.SetValue(null, previous); }
    }

    private sealed class CaptureChannel : ITelemetryChannel
    {
        public List<ITelemetry> Events { get; } = [];
        public bool? DeveloperMode { get; set; }
        public string EndpointAddress { get; set; } = "";
        public void Send(ITelemetry item) => Events.Add(item);
        public void Flush() { }
        public void Dispose() { }
    }

    private sealed class FakeTool(Func<Task> execute) : IBuiltinTool
    {
        public int Calls { get; private set; }
        public string Id => "screen-test";
        public string Name => "屏幕测试";
        public string Description => "";
        public string Glyph => "";
        public string Category => "测试";
        public BuiltinToolKind Kind => BuiltinToolKind.Dialog;
        public Task ExecuteAsync(BuiltinToolContext context) { Calls++; return execute(); }
    }
}

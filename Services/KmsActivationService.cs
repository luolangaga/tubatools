using System.Text.Json;

namespace TubaWinUi3.Services;

public sealed class KmsServerInfo
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 1688;
    public string Country { get; init; } = "";
    public int ConnectCount { get; init; }
    public int ActivateCount { get; init; }
    public int FailedCount { get; init; }
    public double AverageTime { get; init; }
    public string LastCheckDate { get; init; } = "";
    public List<KmsCheckResult> Results { get; init; } = [];

    public double SuccessRate => ConnectCount > 0 ? (double)ActivateCount / ConnectCount * 100 : 0;
    public double RecentSuccessRate
    {
        get
        {
            if (Results.Count == 0) return 0;
            return (double)Results.Count(r => r.Result) / Results.Count * 100;
        }
    }
    public double Score
    {
        get
        {
            var successWeight = SuccessRate * 0.4;
            var recentWeight = RecentSuccessRate * 0.4;
            var latencyScore = AverageTime > 0 ? Math.Max(0, 100 - AverageTime / 30.0) : 0;
            var latencyWeight = latencyScore * 0.2;
            return successWeight + recentWeight + latencyWeight;
        }
    }
}

public sealed class KmsCheckResult
{
    public string Address { get; init; } = "";
    public string Country { get; init; } = "";
    public double Time { get; init; }
    public bool Result { get; init; }
}

public static class KmsActivationService
{
    private const string ApiUrl = "https://monitor.yerong.org/assets/data/?type=kms";

    public static async Task<List<KmsServerInfo>> FetchKmsServersAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(15);
        var json = await http.GetStringAsync(ApiUrl, ct);
        var rawList = JsonSerializer.Deserialize<List<KmsRawItem>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (rawList is null) return [];

        var servers = new List<KmsServerInfo>();
        foreach (var item in rawList)
        {
            var results = new List<KmsCheckResult>();
            if (item.Results != null)
            {
                foreach (var r in item.Results)
                {
                    results.Add(new KmsCheckResult
                    {
                        Address = r.Address ?? "",
                        Country = r.Country ?? "",
                        Time = r.Time,
                        Result = r.Result
                    });
                }
            }

            servers.Add(new KmsServerInfo
            {
                Host = item.Host ?? "",
                Port = item.Port,
                Country = item.Country ?? "",
                ConnectCount = item.ConnectCount,
                ActivateCount = item.ActivateCount,
                FailedCount = item.FailedCount,
                AverageTime = item.AverageTime,
                LastCheckDate = item.LastCheckDate ?? "",
                Results = results
            });
        }

        return servers
            .Where(s => s.AverageTime > 0 && s.SuccessRate >= 80)
            .OrderByDescending(s => s.Score)
            .ToList();
    }

    public static async Task<int> ActivateWindowsAsync(string kmsHost, int kmsPort, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        var psScript = $@"
Set-ExecutionPolicy Bypass -Scope Process -Force
slmgr /ipk W269N-WFGWX-YVC9B-4J6C9-T83GX
slmgr /skms {kmsHost}:{kmsPort}
slmgr /ato
slmgr /xpr
";

        var tempFile = Path.Combine(Path.GetTempPath(), $"kms_activate_{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(tempFile, psScript, ct);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempFile}\"",
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
            };

            var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                onOutput?.Invoke("无法启动管理员 PowerShell 进程。");
                return -1;
            }

            onOutput?.Invoke($"已启动管理员 PowerShell 执行激活，KMS 服务器: {kmsHost}:{kmsPort}");
            onOutput?.Invoke("请在弹出的 UAC 提示中点击「是」以允许管理员权限...");

            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    public static void ScheduleReboot()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/r /t 10 /c \"Windows 激活完成，10 秒后重启计算机...\"",
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true
        };
        System.Diagnostics.Process.Start(psi);
    }

    public static void CancelReboot()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/a",
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true
        };
        System.Diagnostics.Process.Start(psi);
    }

    private sealed class KmsRawItem
    {
        public string? Host { get; set; }
        public int Port { get; set; }
        public string? Country { get; set; }
        public int ConnectCount { get; set; }
        public int ActivateCount { get; set; }
        public int FailedCount { get; set; }
        public double AverageTime { get; set; }
        public string? LastCheckDate { get; set; }
        public List<KmsRawResult>? Results { get; set; }
    }

    private sealed class KmsRawResult
    {
        public string? Address { get; set; }
        public string? Country { get; set; }
        public double Time { get; set; }
        public bool Result { get; set; }
    }
}

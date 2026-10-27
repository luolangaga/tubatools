using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>
/// 一个运行中的 EasyTier 虚拟网络实例，通过本机管理端口（easytier-cli）控制。
/// 只负责起进程、跑命令、杀进程；会话语义在 <see cref="TunnelSession"/>。
/// </summary>
public sealed class EasyTierInstance : IDisposable
{
    private const int OutputLines = 300;
    private static readonly TimeSpan CliTimeout = TimeSpan.FromSeconds(10);

    private readonly Process _process;
    private readonly string _configPath;
    private readonly int _rpcPort;
    private readonly Queue<string> _output = new();
    private int _disposed;

    private EasyTierInstance(Process process, string configPath, int rpcPort)
    {
        _process = process;
        _configPath = configPath;
        _rpcPort = rpcPort;
    }

    /// <summary>进程自行退出时触发（工作线程）。</summary>
    public event EventHandler? Exited;

    public bool IsAlive
    {
        get
        {
            try
            {
                return !_process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public static EasyTierInstance Start(EasyTierConfig config)
    {
        StopLeftovers();

        var runtimeDirectory = Path.Combine(GameTunnelCatalog.DataDir, "EasyTier", "runtime");
        Directory.CreateDirectory(runtimeDirectory);
        var configPath = Path.Combine(runtimeDirectory, Guid.NewGuid().ToString("N") + ".toml");
        File.WriteAllText(configPath, config.ToToml(), new UTF8Encoding(false));

        var rpcPort = FindFreePort();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(EasyTierPackage.CorePath)
            {
                Arguments = $"-c \"{configPath}\" -r 127.0.0.1:{rpcPort} --multi-thread",
                WorkingDirectory = EasyTierPackage.InstallDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
            EnableRaisingEvents = true,
        };

        var instance = new EasyTierInstance(process, configPath, rpcPort);
        process.OutputDataReceived += (_, args) => instance.Capture(args.Data);
        process.ErrorDataReceived += (_, args) => instance.Capture(args.Data);
        process.Exited += (_, _) => instance.OnExited();

        TunnelLog.Info($"启动联机组件（rpc {rpcPort}，主机名 {config.Hostname}，隐藏地址 {config.HideAddress}）");
        process.Start();
        ChildProcessJob.Assign(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return instance;
    }

    /// <summary>网络成员（含本机）。进程还没就绪时返回空列表。</summary>
    public async Task<IReadOnlyList<EasyTierPeer>> GetPeersAsync(CancellationToken cancellation)
    {
        var json = await RunCliAsync(cancellation, "-o", "json", "peer").ConfigureAwait(false);
        return EasyTierPeer.ParseList(json);
    }

    /// <summary>当前连上的发现节点数；未知时返回 -1。</summary>
    public async Task<int> CountConnectedNodesAsync(CancellationToken cancellation)
    {
        var json = await RunCliAsync(cancellation, "-o", "json", "connector").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return -1;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return -1;

            var count = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                if (item.TryGetProperty("status", out var status)
                    && status.ValueKind == System.Text.Json.JsonValueKind.Number
                    && status.TryGetInt32(out var value) && value == 0)
                {
                    count++;
                }
            }
            return count;
        }
        catch (System.Text.Json.JsonException)
        {
            return -1;
        }
    }

    /// <summary>把本机的 <paramref name="bind"/> 转发到虚拟网络里的 <paramref name="target"/>。</summary>
    public async Task<bool> AddPortForwardAsync(string protocol, IPEndPoint bind, IPEndPoint target,
        CancellationToken cancellation)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var output = await RunCliAsync(cancellation,
                "port-forward", "add", protocol, Format(bind), Format(target)).ConfigureAwait(false);
            if (output is not null) return true;
            try
            {
                await Task.Delay(500 + attempt * 1000, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        TunnelLog.Warn($"端口转发失败：{protocol} {Format(bind)} -> {Format(target)}");
        return false;
    }

    /// <summary>进程最近输出的若干行，用于错误提示。</summary>
    public string GetRecentOutput()
    {
        lock (_output) return string.Join(Environment.NewLine, _output);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        _process.Dispose();

        try
        {
            File.Delete(_configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        TunnelLog.Info("联机组件已停止");
    }

    // ══════════════════ 内部 ══════════════════

    private async Task<string?> RunCliAsync(CancellationToken cancellation, params string[] arguments)
    {
        if (!IsAlive) return null;

        var start = new ProcessStartInfo(EasyTierPackage.CliPath)
        {
            Arguments = $"-p 127.0.0.1:{_rpcPort} " + string.Join(" ", arguments.Select(QuoteArgument)),
            WorkingDirectory = EasyTierPackage.InstallDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        return await Task.Run(() =>
        {
            using var cli = new Process { StartInfo = start };
            try
            {
                cli.Start();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                TunnelLog.Warn("联机命令无法启动：" + ex.Message);
                return null;
            }

            var stdout = cli.StandardOutput.ReadToEndAsync();
            var stderr = cli.StandardError.ReadToEndAsync();
            if (!cli.WaitForExit((int)CliTimeout.TotalMilliseconds))
            {
                try { cli.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            cli.WaitForExit();

            if (cli.ExitCode != 0)
            {
                var error = SafeResult(stderr);
                TunnelLog.Info($"命令 '{string.Join(" ", arguments)}' 退出码 {cli.ExitCode}：{error.Trim()}");
                return null;
            }
            return SafeResult(stdout);
        }, cancellation).ConfigureAwait(false);
    }

    private static string SafeResult(Task<string> task)
    {
        try { return task.Result; }
        catch { return ""; }
    }

    private void Capture(string? line)
    {
        if (line is null) return;
        lock (_output)
        {
            _output.Enqueue(line);
            while (_output.Count > OutputLines) _output.Dequeue();
        }
    }

    private void OnExited()
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        int code;
        try { code = _process.ExitCode; }
        catch (InvalidOperationException) { code = -1; }

        TunnelLog.Error($"联机组件意外退出（代码 {code}）。最近输出：{Environment.NewLine}{GetRecentOutput()}");
        Exited?.Invoke(this, EventArgs.Empty);
    }

    private static string Format(IPEndPoint endPoint)
        => endPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{endPoint.Address}]:{endPoint.Port}"
            : $"{endPoint.Address}:{endPoint.Port}";

    /// <summary>命令行参数转义（含空格/引号时加引号）。</summary>
    internal static string QuoteArgument(string argument)
        => argument.IndexOfAny([' ', '"']) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";

    /// <summary>本机当前空闲的 TCP 端口（用于管理端口/转发绑定）。</summary>
    internal static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// 结束父进程已经不在了的残留组件（崩溃/强杀留下的）。
    /// 父进程还活着的属于另一个联机会话，保持不动。
    /// </summary>
    private static void StopLeftovers()
    {
        var root = Path.Combine(GameTunnelCatalog.DataDir, "EasyTier");
        if (!Directory.Exists(root)) return;

        var othersAlive = false;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(EasyTierPackage.CorePath)))
        {
            using (process)
            {
                string? image = null;
                try { image = process.MainModule?.FileName; } catch { }
                if (image is null || !image.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                if (HasLivingParent(process, out var parentId) && parentId != Environment.ProcessId)
                {
                    othersAlive = true;
                    continue;
                }

                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                    TunnelLog.Info("已清理残留的联机组件进程 " + process.Id);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
            }
        }

        var runtime = Path.Combine(root, "runtime");
        if (othersAlive || !Directory.Exists(runtime)) return;

        // 崩溃的会话留下的配置文件里还有网络密钥，其它会话不用时一并清掉。
        foreach (var file in Directory.GetFiles(runtime, "*.toml"))
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static bool HasLivingParent(Process process, out int parentId)
    {
        parentId = 0;
        try
        {
            var info = new ProcessBasicInformation();
            if (NtQueryInformationProcess(process.Handle, 0, ref info, Marshal.SizeOf(info), out _) != 0)
                return true;

            parentId = info.InheritedFromUniqueProcessId.ToInt32();
            using var parent = Process.GetProcessById(parentId);
            // 进程号会复用：启动时间晚于子进程的「父进程」是另一个程序。
            return parent.StartTime <= process.StartTime;
        }
        catch (ArgumentException)
        {
            // 找不到父进程 = 启动它的程序已经退出。
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass,
        ref ProcessBasicInformation info, int size, out int returned);
}

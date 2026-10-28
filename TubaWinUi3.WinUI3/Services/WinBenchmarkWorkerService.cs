using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>Keep the controller responsive while the unchanged XAML workload owns its own process.</summary>
internal static class WinBenchmarkWorkerService
{
    internal const string WorkerArgument = "--win-benchmark-worker";

    internal sealed class Response
    {
        public WinPerformanceResult? Result { get; set; }
        public string Error { get; set; } = "";
        public bool Cancelled { get; set; }
    }

    internal static async Task<WinPerformanceResult> RunAsync(bool dark, IProgress<BenchmarkProgress> progress, CancellationToken ct)
    {
        string directory = Path.Combine(Path.GetTempPath(), "TubaWinUi3_WinBenchmark", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string resultPath = Path.Combine(directory, "result.json");
        string progressPath = Path.Combine(directory, "progress.json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        var token = timeout.Token;
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "TubaWinUi3.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var arg in new[] { WorkerArgument, directory, Environment.ProcessId.ToString(), dark ? "dark" : "light" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动界面测试进程。");
        void KillWorker() { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
        using var cancelProcess = token.Register(KillWorker);
        try
        {
            var exit = process.WaitForExitAsync(token);
            string lastProgress = "";
            while (!exit.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(progressPath))
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(progressPath, token);
                        if (json != lastProgress && JsonSerializer.Deserialize<BenchmarkProgress>(json) is { } update)
                        { progress.Report(update); lastProgress = json; }
                    }
                    catch (IOException) { } // Atomic writer can briefly hold the file on Windows.
                    catch (JsonException) { }
                }
                await Task.WhenAny(exit, Task.Delay(250, token));
            }
            await exit;
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || !File.Exists(resultPath))
                throw new InvalidDataException("界面测试进程提前关闭或异常退出，本次结果无效。");
            var response = JsonSerializer.Deserialize<Response>(await File.ReadAllTextAsync(resultPath, token))
                ?? throw new InvalidDataException("界面测试没有返回有效结果。");
            if (response.Cancelled) throw new OperationCanceledException("界面测试已取消。");
            if (response.Result == null) throw new InvalidDataException("界面测试失败：" + response.Error);
            BenchmarkResultValidation.Win(response.Result);
            return response.Result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("界面测试超时，已停止测试进程，本次结果无效。");
        }
        finally
        {
            KillWorker();
            // This directory belongs only to this invocation. Never recursively delete a caller-supplied path.
            try { foreach (string file in new[] { resultPath, progressPath, resultPath + ".tmp", progressPath + ".tmp" }) File.Delete(file); Directory.Delete(directory); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static async void StartWorker(string directory, int parentId, bool dark)
    {
        using var lifetime = new CancellationTokenSource();
        using var parent = Process.GetProcessById(parentId);
        parent.EnableRaisingEvents = true;
        // The worker never starts monitoring/ETW. A vanished controller must not leave a hung XAML worker behind.
        parent.Exited += (_, _) => Environment.Exit(2);
        var response = new Response();
        try
        {
            response.Result = await PerformanceBenchmarkPage.RunWinWorkloadAsync(dark,
                update => WriteAtomic(Path.Combine(directory, "progress.json"), update), lifetime.Token);
        }
        catch (OperationCanceledException) { response.Cancelled = true; }
        catch (Exception ex) { response.Error = ex.Message; }
        try { WriteAtomic(Path.Combine(directory, "result.json"), response); }
        finally { Application.Current.Exit(); }
    }

    private static void WriteAtomic<T>(string path, T data)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

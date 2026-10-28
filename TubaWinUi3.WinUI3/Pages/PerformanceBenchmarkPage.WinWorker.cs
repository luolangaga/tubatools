using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;

namespace TubaWinUi3.Pages;

public sealed partial class PerformanceBenchmarkPage
{
    // A worker owns only the original benchmark controls; it never constructs the toolbox controller.
    private PerformanceBenchmarkPage(bool worker) { _cts = new CancellationTokenSource(); }

    internal static async Task<WinPerformanceResult> RunWinWorkloadAsync(bool dark, Action<BenchmarkProgress> report, CancellationToken ct)
    {
        var runner = new PerformanceBenchmarkPage(worker: true);
        runner._winIconPaths = await Task.Run(() => WinPerformanceService.CollectIconImagePaths(), ct);
        runner._winSortData = await Task.Run(() => WinPerformanceService.GenerateSortData(), ct);
        var status = runner.Label("正在准备测试…", 13, true);
        var controls = runner.BuildWinTestControls();
        using var local = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(16), RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light };
        panel.Children.Add(status); panel.Children.Add(controls);
        var window = new Window { Title = "WinUI 性能测试工作区 · 可在工具箱中停止", Content = panel };
        window.AppWindow.IsShownInSwitchers = false;
        // Show without activation so the controller and its Stop action stay in the foreground.
        window.AppWindow.Resize(new SizeInt32(1040, 1400));
        window.AppWindow.Show(activateWindow: false);
        bool finished = false;
        window.Closed += (_, _) => { if (!finished) local.Cancel(); };
        try
        {
            await Task.Delay(150, local.Token);
            var result = new WinPerformanceResult { TestTime = DateTime.Now, RunCount = 5, DroppedRunCount = 1 };
            for (int round = 1; round <= result.RunCount; round++)
            {
                local.Token.ThrowIfCancellationRequested();
                report(new BenchmarkProgress { Phase = "WinUI", SubPhase = $"第 {round} / {result.RunCount} 轮", Detail = "独立进程正在测量真实控件；可随时停止。", Progress = (round - 1d) / result.RunCount });
                var run = await runner.RunWinRoundAsync(local.Token, status);
                local.Token.ThrowIfCancellationRequested();
                result.Runs.Add(run);
                report(new BenchmarkProgress { Phase = "WinUI", SubPhase = $"已完成 {round} / {result.RunCount} 轮", Detail = $"本轮 {run.TotalMs:F0} ms", Progress = (double)round / result.RunCount });
            }
            BenchmarkResultValidation.Win(result);
            WinPerformanceService.FinalizeResult(result);
            return result;
        }
        finally
        {
            finished = true; window.Close(); runner._cts.Dispose();
        }
    }

    private async Task RunWinBenchmarkAsync(PerformanceBenchmarkResult result, CancellationToken ct)
    {
        int generation = _uiGeneration;
        var progress = new Progress<BenchmarkProgress>(update =>
        {
            if (generation == _uiGeneration && _isRunning && !ct.IsCancellationRequested)
                _statusText.Text = update.SubPhase + " · " + update.Detail;
        });
        var win = await WinBenchmarkWorkerService.RunAsync(ActualTheme == ElementTheme.Dark, progress, ct);
        ct.ThrowIfCancellationRequested();
        result.Win = win;
        WinPerformanceService.SaveHistory(win);
        _winListLoadText.Text = $"{win.AvgListLoadMs:F0} ms";
        _winImageListText.Text = $"{win.AvgImageListMs:F0} ms";
        _winTabSwitchText.Text = $"{win.AvgTabSwitchMs:F0} ms";
        _winScrollText.Text = $"{win.AvgScrollMs:F0} ms";
        _winTreeExpandText.Text = $"{win.AvgTreeExpandMs:F0} ms";
        _winSortFilterText.Text = $"{win.AvgSortFilterMs:F0} ms";
        _winTextRenderText.Text = $"{win.AvgTextRenderMs:F0} ms";
        _winTotalText.Text = $"{win.BestAvgMs:F0} ms";
    }
}

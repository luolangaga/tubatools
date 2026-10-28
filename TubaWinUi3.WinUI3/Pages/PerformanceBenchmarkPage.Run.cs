using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class PerformanceBenchmarkPage
{
    private int _activeStage = -1;
    private readonly bool[] _completedStages = new bool[StageCount];
    private readonly bool[] _failedStages = new bool[StageCount];

    private void SetRunning(bool running)
    {
        _isRunning = running;
        _startBtn.IsEnabled = !running;
        _startBtn.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        _stopBtn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _stopBtn.IsEnabled = running;
        _historyBtn.IsEnabled = _historyOverflow.IsEnabled = !running;
        _latencyOnlyBtn.IsEnabled = !running;
        _rankingBtn.IsEnabled = !running;
        SetCheckboxesEnabled(!running);
        if (running) { _runClock.Restart(); _runTimer.Start(); }
        else { _runTimer.Stop(); _runClock.Stop(); }
    }

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;
        bool[] selected = StageChecks.Select(c => c.IsChecked == true).ToArray();
        int selectedCount = selected.Count(x => x);
        if (selectedCount == 0) { _statusText.Text = "请至少选择一个测试项目。"; return; }
        if (!PerformanceBenchmarkService.TryBeginSession()) { _statusText.Text = "另一个测试正在释放资源，请稍后再试。"; return; }
        int generation = ++_uiGeneration;
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _result = null;
        ResetUI();
        Array.Clear(_completedStages); Array.Clear(_failedStages);
        _exportBtn.IsEnabled = false;
        SetRunning(true);
        _globalProgress.Maximum = selectedCount;
        _phaseTitle.Text = "准备测试环境";
        _statusText.Text = "正在识别硬件与测试工具…";
        _resultNote.Text = "测量完成且数据校验通过后，才会显示分数。";
        for (int i = 0; i < StageCount; i++)
        {
            _stageLabels[i].Text = selected[i] ? "等待中" : "未选择";
            _stageSummaries[i].Text = selected[i] ? "等待测量" : "本次未选择";
            _stageRings[i].IsActive = false; _stageRings[i].Visibility = Visibility.Collapsed;
        }
        var result = new PerformanceBenchmarkResult { TestTime = DateTime.Now, DurationMode = "Deep" };
        bool cancelled = false;
        int settled = 0;
        try
        {
            int gpuIndex = 0; string gpuName = "";
            if (selected[4])
            {
                var picked = await ShowGpuSelectDialogAsync();
                if (picked == null) throw new OperationCanceledException("已取消显卡选择");
                gpuIndex = picked.Value.Index; gpuName = picked.Value.Name;
            }
            ct.ThrowIfCancellationRequested();
            await PerformanceBenchmarkService.PopulateHardwareInfoAsync(result);
            ct.ThrowIfCancellationRequested();
            for (int stage = 0; stage < StageCount; stage++)
            {
                if (!selected[stage]) continue;
                ct.ThrowIfCancellationRequested();
                _activeStage = stage;
                int current = stage;
                _phaseTitle.Text = $"正在测试 · {_stageNames[stage]}";
                _statusText.Text = _stageDescriptions[stage];
                _stageLabels[stage].Text = "测试中";
                _stageRings[stage].Visibility = Visibility.Visible; _stageRings[stage].IsActive = true;
                var progress = new Progress<BenchmarkProgress>(p =>
                {
                    // Reports from a finished/cancelled stage must not overwrite its terminal state.
                    if (!_isRunning || ct.IsCancellationRequested || generation != _uiGeneration || _activeStage != current) return;
                    _statusText.Text = string.Join(" · ", new[] { p.SubPhase, p.Detail }.Where(s => !string.IsNullOrWhiteSpace(s)));
                });
                var stageClock = Stopwatch.StartNew();
                try
                {
                    switch (stage)
                    {
                        case 0:
                            var cpu = await Task.Run(() => PerformanceBenchmarkService.RunCpuBenchmark(60, progress, ct), ct);
                            BenchmarkResultValidation.Cpu(cpu);
                            ct.ThrowIfCancellationRequested();
                            result.Cpu = cpu; UpdateCpuUI(result);
                            break;
                        case 1:
                            string? latencyExe = PerformanceBenchmarkService.FindCoreToCoreLatencyExe();
                            if (latencyExe == null) throw new InvalidOperationException("未找到核间延迟工具，请安装完整工具内核后重试。");
                            var (csv, _) = await ShowCoreToCoreLatencyDialog(latencyExe, ct);
                            var matrix = PerformanceBenchmarkService.ParseCoreToCoreCsv(csv, Math.Min(Environment.ProcessorCount, 64));
                            BenchmarkResultValidation.Latency(matrix);
                            ct.ThrowIfCancellationRequested();
                            PerformanceBenchmarkService.ApplyLatencyResult(result.Cpu, matrix);
                            _latencyHeatmapPath = await Task.Run(() => PerformanceBenchmarkService.GenerateLatencyHeatmap(matrix), ct);
                            ct.ThrowIfCancellationRequested();
                            UpdateScoreRow(_cpuLatencyScoreText, result.Cpu.LatencyScore); ShowLatencyHeatmap(_latencyHeatmapPath);
                            break;
                        case 2:
                            var memory = await Task.Run(() => PerformanceBenchmarkService.RunMemoryBenchmark(1, progress, ct), ct);
                            BenchmarkResultValidation.RequirePositive(memory.TotalCapacityGB, "内存容量");
                            ct.ThrowIfCancellationRequested(); result.Memory = memory; UpdateMemoryUI(result);
                            break;
                        case 3:
                            var disk = await Task.Run(() => PerformanceBenchmarkService.RunDiskBenchmark(20, progress, ct), ct);
                            BenchmarkResultValidation.Disk(disk);
                            ct.ThrowIfCancellationRequested(); result.Disk = disk; UpdateDiskUI(result);
                            break;
                        case 4:
                            var gpu = await Task.Run(() => PerformanceBenchmarkService.RunGpuBenchmarkFurMark(60, progress, ct, gpuIndex, gpuName), ct);
                            BenchmarkResultValidation.Gpu(gpu);
                            ct.ThrowIfCancellationRequested(); result.Gpu = gpu;
                            if (!string.IsNullOrWhiteSpace(gpu.GpuName)) result.GpuName = gpu.GpuName;
                            UpdateGpuUI(result);
                            break;
                        case 5: await RunBrowserTestsAsync(result, 60, ct); break;
                        case 6: await RunWinBenchmarkAsync(result, ct); break;
                    }
                    ct.ThrowIfCancellationRequested();
                    _completedStages[stage] = true;
                    _stageLabels[stage].Text = $"已完成 · {stageClock.Elapsed.TotalSeconds:F0} 秒";
                    _stageSummaries[stage].Text = "测量完成 · 数据有效";
                }
                catch (OperationCanceledException)
                {
                    _stageLabels[stage].Text = "已取消 · 不计分";
                    _stageSummaries[stage].Text = "测试中断，未采用本项结果";
                    throw;
                }
                catch (Exception ex)
                {
                    _failedStages[stage] = true;
                    _stageLabels[stage].Text = "失败 · 不计分";
                    _stageSummaries[stage].Text = ex.Message;
                    _detailSections[stage].IsExpanded = false;
                }
                finally
                {
                    _stageRings[stage].IsActive = false; _stageRings[stage].Visibility = Visibility.Collapsed;
                    _activeStage = -1;
                }
                settled++;
                _globalProgress.Value = settled;
                _completionText.Text = $"已处理 {settled} / {selectedCount} 项 · 成功 {_completedStages.Count(x => x)} · 失败 {_failedStages.Count(x => x)}";
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            for (int i = 0; i < StageCount; i++)
                if (selected[i] && !_completedStages[i] && !_failedStages[i]) _stageLabels[i].Text = "已取消 · 不计分";
        }
        catch (Exception ex)
        {
            _statusText.Text = "准备测试失败：" + ex.Message;
            _phaseTitle.Text = "测试未能开始";
            return;
        }
        finally
        {
            SetRunning(false);
            PerformanceBenchmarkService.EndSession();
            _activeStage = -1;
        }
        _completionText.Text = $"完成 {_completedStages.Count(x => x)} / {selectedCount} 项 · 失败 {_failedStages.Count(x => x)}";
        result.TotalDuration = _runClock.Elapsed;
        bool fullHardware = new[] { 0, 2, 3, 4, 5 }.All(i => _completedStages[i]);
        if (fullHardware)
        {
            // Keep the existing scoring formulas and their weights exactly as they are.
            result.GamingScore = PerformanceBenchmarkService.ComputeGamingScore(result);
            result.GamingGrade = PerformanceBenchmarkService.ComputeGrade(result.GamingScore);
            result.OfficeScore = PerformanceBenchmarkService.ComputeOfficeScore(result);
            result.OfficeGrade = PerformanceBenchmarkService.ComputeGrade(result.OfficeScore);
            UpdateTopCard(_gamingScoreText, _gamingGradeText, _gamingBar, result.GamingScore, result.GamingGrade);
            UpdateTopCard(_officeScoreText, _officeGradeText, _officeBar, result.OfficeScore, result.OfficeGrade);
        }
        else { _gamingGradeText.Text = _officeGradeText.Text = "项目不完整"; }
        if (_completedStages[6]) UpdateTopCard(_winScoreText, _winGradeText, _winBar, result.Win.FinalScore, result.Win.Grade);
        bool failed = _failedStages.Any(x => x);
        _phaseTitle.Text = cancelled ? "测试已停止" : failed ? "测试结束 · 部分项目失败" : "测试完成";
        _statusText.Text = cancelled ? "已完成的有效单项仍可查看，未完成项目不计分。" : failed ? "展开结果明细查看失败原因，可重新测试。" : "测量结果已就绪，展开项目查看详细数据。";
        _resultNote.Text = fullHardware ? "综合分沿用原有权重与算法。100 分为参考基准。" : "综合分需要处理器、内存、硬盘、显卡及浏览器全部完成；核间延迟和界面响应可独立选测。";
        if (fullHardware && !cancelled && !failed)
        {
            _result = result;
            PerformanceBenchmarkService.SaveHistory(result);
            _exportBtn.IsEnabled = true;
        }
    }
}

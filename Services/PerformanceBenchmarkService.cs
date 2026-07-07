using System.Diagnostics.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "WMI requires reflection")]
public static class PerformanceBenchmarkService
{
	private static CancellationTokenSource? _cts;
	private static volatile bool _running;
	private const double CpuSingleBaseline = 728.0;
	private const double CpuMultiBaseline = 8351.0;

	public static bool IsRunning => _running;

	public static void Cancel()
	{
		_cts?.Cancel();
	}

	public static async Task<PerformanceBenchmarkResult> RunAllAsync(IProgress<BenchmarkProgress>? progress, CancellationToken ct)
	{
		if (_running)
		{
			throw new InvalidOperationException("测试已在运行中");
		}
		_running = true;
		var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		try
		{
			_cts = cts;
			var sw = Stopwatch.StartNew();
			var result = new PerformanceBenchmarkResult
			{
				TestTime = DateTime.Now,
				DurationMode = "Deep"
			};
			try
			{
				var monitor = LiteMonitorService.Instance;
				monitor.EnsureInit();
				var sample = monitor.Read();
				result.CpuName = sample.CpuName;
				result.GpuName = sample.GpuName;
				result.OsName = GetOsName();
				result.Cpu = await Task.Run(() => RunCpuBenchmark(60, progress, cts.Token), cts.Token);
				ct.ThrowIfCancellationRequested();
				result.Memory = await Task.Run(() => RunMemoryBenchmark(1, progress, cts.Token), cts.Token);
				ct.ThrowIfCancellationRequested();
				result.Disk = await Task.Run(() => RunDiskBenchmark(20, progress, cts.Token), cts.Token);
				ct.ThrowIfCancellationRequested();
				result.Gpu = await Task.Run(() => RunGpuBenchmarkFurMark(60, progress, cts.Token), cts.Token);
				ct.ThrowIfCancellationRequested();
				result.Browser = new BrowserBenchmarkResult();
				result.GamingScore = ComputeGamingScore(result);
				result.GamingGrade = ComputeGrade(result.GamingScore);
				result.OfficeScore = ComputeOfficeScore(result);
				result.OfficeGrade = ComputeGrade(result.OfficeScore);
				sw.Stop();
				result.TotalDuration = sw.Elapsed;
				SaveHistory(result);
			}
			catch (OperationCanceledException)
			{
				sw.Stop();
				result.TotalDuration = sw.Elapsed;
			}
			finally
			{
				_running = false;
				_cts = null;
			}
			return result;
		}
		finally
		{
			cts.Dispose();
		}
	}

	public static CpuBenchmarkResult RunCpuBenchmark(int durationSec, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
	{
		var sw = Stopwatch.StartNew();
		var cpu = new CpuBenchmarkResult();
		progress?.Report(new BenchmarkProgress
		{
			Phase = "CPU",
			SubPhase = "CPU-Z 基准测试",
			Progress = 0.0
		});
		var (singleRaw, multiRaw) = RunCpuzBenchmark(progress, ct);
		cpu.SingleCoreScore = NormalizeCpuSingle(singleRaw);
		cpu.MultiCoreScore = NormalizeCpuMulti(multiRaw);
		cpu.SingleCoreIterations = singleRaw;
		cpu.MultiCoreIterations = multiRaw;
		ct.ThrowIfCancellationRequested();
		cpu.LatencyMatrix = null;
		cpu.LatencyScore = 500;
		cpu.TotalScore = (int)((double)cpu.SingleCoreScore * 0.5 + (double)cpu.MultiCoreScore * 0.5);
		cpu.Grade = ComputeGrade(cpu.TotalScore);
		sw.Stop();
		cpu.Duration = sw.Elapsed;
		return cpu;
	}

	private static (int singleCore, int multiCore) RunCpuzBenchmark(IProgress<BenchmarkProgress>? progress, CancellationToken ct)
	{
		string cpuzExe = CpuzInfoService.FindCpuzExe();
		if (cpuzExe == null)
		{
			throw new InvalidOperationException("未找到 CPU-Z，无法运行 CPU 基准测试。请确保 Tools/处理器工具/CPUZ/ 目录下存在 cpuz_x64.exe。");
		}
		string cpuzDir = Path.GetDirectoryName(cpuzExe);
		string benchFile = Path.Combine(cpuzDir, Environment.MachineName + ".txt");
		try
		{
			CleanCpuzBenchFiles(cpuzDir);
			using var process = Process.Start(new ProcessStartInfo
			{
				FileName = cpuzExe,
				Arguments = "-bench",
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Hidden,
				WorkingDirectory = cpuzDir
			});
			if (process == null)
			{
				throw new InvalidOperationException("CPU-Z 进程启动失败");
			}
			var timeoutCts = new CancellationTokenSource(300000);
			try
			{
				while (!process.WaitForExit(2000))
				{
					ct.ThrowIfCancellationRequested();
					if (timeoutCts.Token.IsCancellationRequested)
					{
						try { process.Kill(entireProcessTree: true); } catch { }
						throw new TimeoutException("CPU-Z 基准测试超时");
					}
					progress?.Report(new BenchmarkProgress
					{
						Phase = "CPU",
						SubPhase = "CPU-Z 基准测试中...",
						Progress = 0.05
					});
				}
			}
			catch (OperationCanceledException)
			{
				try { process.Kill(entireProcessTree: true); } catch { }
				throw;
			}
			int waitMs = 15000;
			int waited = 0;
			while (!File.Exists(benchFile) && waited < waitMs)
			{
				Thread.Sleep(500);
				waited += 500;
			}
			if (!File.Exists(benchFile))
			{
				throw new InvalidOperationException("CPU-Z 基准测试结果文件未生成: " + benchFile);
			}
			Thread.Sleep(500);
			string content;
			try
			{
				content = File.ReadAllText(benchFile);
			}
			catch (Exception ex2)
			{
				throw new InvalidOperationException("读取 CPU-Z 基准测试结果失败: " + ex2.Message);
			}
			return ParseCpuzBenchCsv(content);
		}
		finally
		{
			try
			{
				if (File.Exists(benchFile)) File.Delete(benchFile);
			}
			catch { }
			CpuzInfoService.KillCpuzProcesses();
		}
	}

	private static void CleanCpuzBenchFiles(string dir)
	{
		try
		{
			string path = Path.Combine(dir, Environment.MachineName + ".txt");
			if (File.Exists(path))
			{
				try { File.Delete(path); return; }
				catch { return; }
			}
		}
		catch { }
	}

	private static (int singleCore, int multiCore) ParseCpuzBenchCsv(string content)
	{
		string[] parts = content.Trim().Trim('"').Split("\",\"", 2);
		if (parts.Length == 2)
		{
			string s1 = parts[0].Trim('"');
			string s2 = parts[1].Trim('"');
			if (double.TryParse(s1, NumberStyles.Float, CultureInfo.InvariantCulture, out var v1) &&
			    double.TryParse(s2, NumberStyles.Float, CultureInfo.InvariantCulture, out var v2))
			{
				return (singleCore: (int)Math.Round(v1), multiCore: (int)Math.Round(v2));
			}
		}
		var matches = Regex.Matches(content, @"[\d.]+");
		if (matches.Count >= 2 &&
		    double.TryParse(matches[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r1) &&
		    double.TryParse(matches[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r2))
		{
			return (singleCore: (int)Math.Round(r1), multiCore: (int)Math.Round(r2));
		}
		throw new InvalidOperationException("无法解析 CPU-Z 基准测试结果: " + content);
	}

	public static void ApplyLatencyResult(CpuBenchmarkResult cpu, InterCoreLatencyMatrix latency)
	{
		cpu.LatencyMatrix = latency;
		cpu.LatencyScore = NormalizeLatency(latency.AverageNs);
		cpu.TotalScore = (int)((double)cpu.SingleCoreScore * 0.5 + (double)cpu.MultiCoreScore * 0.5);
		cpu.Grade = ComputeGrade(cpu.TotalScore);
	}

	public static GpuBenchmarkResult RunGpuBenchmarkFurMark(int durationSec, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
	{
		var sw = Stopwatch.StartNew();
		var gpu = new GpuBenchmarkResult();
		string furMarkExe = FindFurMarkExe();
		if (furMarkExe == null)
		{
			progress?.Report(new BenchmarkProgress
			{
				Phase = "GPU",
				SubPhase = "FurMark",
				Detail = "未找到FurMark (烤鸡工具/FurMark_win64/furmark.exe)",
				Progress = 0.6
			});
			gpu.FurMarkScore = 0;
			gpu.AvgFps = 0.0;
			gpu.TotalScore = 0;
			gpu.Grade = "E";
			gpu.Duration = TimeSpan.Zero;
			return gpu;
		}
		progress?.Report(new BenchmarkProgress
		{
			Phase = "GPU",
			SubPhase = "FurMark 基准测试",
			Detail = "路径: " + furMarkExe,
			Progress = 0.55
		});
		string furMarkDir = Path.GetDirectoryName(furMarkExe);
		string logFile = Path.Combine(furMarkDir, "_furmark_log.txt");
		if (File.Exists(logFile))
		{
			try { File.Delete(logFile); } catch { }
		}
		int durationMs = durationSec * 1000;
		string arguments = "--demo furmark-vk --width 1920 --height 1080 --fullscreen --benchmark --duration-ms " + durationMs + " --print-render-speed";
		using var process = Process.Start(new ProcessStartInfo(furMarkExe, arguments)
		{
			WorkingDirectory = furMarkDir,
			UseShellExecute = false,
			CreateNoWindow = true
		});
		if (process == null)
		{
			progress?.Report(new BenchmarkProgress
			{
				Phase = "GPU",
				SubPhase = "FurMark",
				Detail = "FurMark进程启动失败",
				Progress = 0.6
			});
			gpu.TotalScore = 0;
			gpu.Grade = "E";
			gpu.Duration = TimeSpan.Zero;
			return gpu;
		}
		progress?.Report(new BenchmarkProgress
		{
			Phase = "GPU",
			SubPhase = "FurMark",
			Detail = "等待FurMark运行...",
			Progress = 0.57
		});
		int timeoutMs = (durationSec + 90) * 1000;
		while (!process.WaitForExit(2000))
		{
			ct.ThrowIfCancellationRequested();
			if (File.Exists(logFile))
			{
				try
				{
					string logContent = File.ReadAllText(logFile);
					if (logContent.Contains("- SCORE") || logContent.Contains("Quick Stats"))
					{
						progress?.Report(new BenchmarkProgress
						{
							Phase = "GPU",
							SubPhase = "FurMark",
							Detail = "检测到分数，等待退出...",
							Progress = 0.58
						});
					}
				}
				catch { }
			}
			if (sw.ElapsedMilliseconds > timeoutMs)
			{
				try { process.Kill(entireProcessTree: true); } catch { }
				break;
			}
		}
		try
		{
			if (!process.HasExited) process.Kill(entireProcessTree: true);
		}
		catch { }
		for (int i = 0; i < 10; i++)
		{
			try { Task.Delay(500, ct).Wait(ct); } catch { }
			if (!File.Exists(logFile)) continue;
			try
			{
				using var stream = File.Open(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using var sr = new StreamReader(stream);
				if (sr.ReadToEnd().Contains("SCORE")) break;
			}
			catch { }
		}
		if (File.Exists(logFile))
		{
			try
			{
				string input;
				using (var stream2 = File.Open(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				{
					using var sr2 = new StreamReader(stream2);
					input = sr2.ReadToEnd();
				}
				var scoreMatch = Regex.Match(input, @"SCORE\s*:\s*(\d+)");
				if (scoreMatch.Success && int.TryParse(scoreMatch.Groups[1].Value, out var furmarkScore))
				{
					gpu.FurMarkScore = furmarkScore;
				}
				var fpsMatch = Regex.Match(input, @"FPS \(min/avg/max\)\s*:\s*([\d.]+)\s*/\s*([\d.]+)\s*/\s*([\d.]+)");
				if (fpsMatch.Success)
				{
					if (double.TryParse(fpsMatch.Groups[1].Value, out var minFps)) gpu.MinFps = minFps;
					if (double.TryParse(fpsMatch.Groups[2].Value, out var avgFps)) gpu.AvgFps = avgFps;
					if (double.TryParse(fpsMatch.Groups[3].Value, out var maxFps)) gpu.MaxFps = maxFps;
				}
				progress?.Report(new BenchmarkProgress
				{
					Phase = "GPU",
					SubPhase = "FurMark",
					Detail = "分数:" + gpu.FurMarkScore + " FPS:" + string.Format("{0:F0}", gpu.AvgFps),
					Progress = 0.6
				});
			}
			catch (Exception ex)
			{
				progress?.Report(new BenchmarkProgress
				{
					Phase = "GPU",
					SubPhase = "FurMark",
					Detail = "解析日志失败: " + ex.Message,
					Progress = 0.6
				});
			}
		}
		else
		{
			progress?.Report(new BenchmarkProgress
			{
				Phase = "GPU",
				SubPhase = "FurMark",
				Detail = "日志文件不存在: " + logFile,
				Progress = 0.6
			});
		}
		gpu.RenderScore = NormalizeFurMarkScore(gpu.FurMarkScore);
		gpu.RenderFps = gpu.AvgFps;
		gpu.TotalScore = gpu.RenderScore;
		gpu.Grade = ComputeGrade(gpu.TotalScore);
		sw.Stop();
		gpu.Duration = sw.Elapsed;
		return gpu;
	}

	public static string? FindFurMarkExe()
	{
		try
		{
			string toolsRoot = ToolCatalog.ToolsRoot;
			if (!string.IsNullOrEmpty(toolsRoot))
			{
				string dir = Path.Combine(toolsRoot, "烤鸡工具", "FurMark_win64");
				if (Directory.Exists(dir))
				{
					string exe = Path.Combine(dir, "furmark.exe");
					if (File.Exists(exe)) return exe;
				}
				dir = Path.Combine(toolsRoot, "烤鸡工具", "FurMark");
				if (Directory.Exists(dir))
				{
					string exe = Path.Combine(dir, "FurMark.exe");
					if (File.Exists(exe)) return exe;
				}
			}
			string baseDir = Path.Combine(AppContext.BaseDirectory, "Tools", "烤鸡工具", "FurMark_win64");
			if (Directory.Exists(baseDir))
			{
				string exe = Path.Combine(baseDir, "furmark.exe");
				if (File.Exists(exe)) return exe;
			}
		}
		catch { }
		return null;
	}

	public static MemoryBenchmarkResult RunMemoryBenchmark(int durationSec, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
	{
		var sw = Stopwatch.StartNew();
		var mem = new MemoryBenchmarkResult();
		var monitor = LiteMonitorService.Instance;
		monitor.EnsureInit();
		var sample = monitor.Read();
		mem.TotalCapacityGB = sample.MemTotalGB > 0f ? sample.MemTotalGB : GetTotalMemoryGB();
		mem.CapacityScore = NormalizeMemCapacity(mem.TotalCapacityGB);
		mem.TotalScore = mem.CapacityScore;
		mem.Grade = ComputeGrade(mem.TotalScore);
		sw.Stop();
		mem.Duration = sw.Elapsed;
		return mem;
	}

	public static DiskBenchmarkResult RunDiskBenchmark(int durationSec, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
	{
		var sw = Stopwatch.StartNew();
		var disk = new DiskBenchmarkResult();
		string diskSpdExe = FindDiskSpdExe();
		if (diskSpdExe == null)
		{
			disk.SeqReadMBs = 0.0;
			disk.SeqWriteMBs = 0.0;
			disk.Random4KReadIops = 0.0;
			disk.Random4KWriteIops = 0.0;
			disk.TotalScore = 0;
			disk.Grade = "E";
			return disk;
		}
		string tempDir = Path.Combine(Path.GetTempPath(), "TubaWinUi3_Benchmark");
		Directory.CreateDirectory(tempDir);
		try
		{
			string filePath = Path.Combine(tempDir, "bench_temp.dat");
			int halfDuration = Math.Max(durationSec / 2, 3);
			string fileSize = "1G";
			progress?.Report(new BenchmarkProgress { Phase = "硬盘", SubPhase = "顺序读取", Progress = 0.6 });
			disk.SeqReadMBs = RunDiskSpd(diskSpdExe, filePath, fileSize, "1M", 1, 1, 0, halfDuration, ct);
			disk.SeqReadScore = NormalizeDiskSeq(disk.SeqReadMBs);
			ct.ThrowIfCancellationRequested();
			progress?.Report(new BenchmarkProgress { Phase = "硬盘", SubPhase = "顺序写入", Progress = 0.65 });
			disk.SeqWriteMBs = RunDiskSpd(diskSpdExe, filePath, fileSize, "1M", 1, 1, 100, halfDuration, ct);
			disk.SeqWriteScore = NormalizeDiskSeq(disk.SeqWriteMBs);
			ct.ThrowIfCancellationRequested();
			progress?.Report(new BenchmarkProgress { Phase = "硬盘", SubPhase = "4K随机读取", Progress = 0.7 });
			disk.Random4KReadIops = RunDiskSpdIops(diskSpdExe, filePath, fileSize, "4K", 1, 32, 0, halfDuration, ct);
			disk.Random4KReadScore = NormalizeDisk4K(disk.Random4KReadIops);
			ct.ThrowIfCancellationRequested();
			progress?.Report(new BenchmarkProgress { Phase = "硬盘", SubPhase = "4K随机写入", Progress = 0.75 });
			disk.Random4KWriteIops = RunDiskSpdIops(diskSpdExe, filePath, fileSize, "4K", 1, 32, 100, halfDuration, ct);
			disk.Random4KWriteScore = NormalizeDisk4K(disk.Random4KWriteIops);
			progress?.Report(new BenchmarkProgress { Phase = "硬盘", SubPhase = "温度", Progress = 0.78 });
			var temps = LiteMonitorService.ReadDiskTemperatures();
			disk.Temperature = temps.Count > 0 ? temps.Values.Max() : -1f;
			disk.TempScore = disk.Temperature > 0f ? NormalizeDiskTemp(disk.Temperature) : 500;
		}
		finally
		{
			try
			{
				if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
			}
			catch { }
		}
		disk.TotalScore = (int)((double)disk.SeqReadScore * 0.25 + (double)disk.SeqWriteScore * 0.2 + (double)disk.Random4KReadScore * 0.25 + (double)disk.Random4KWriteScore * 0.2 + (double)disk.TempScore * 0.1);
		disk.Grade = ComputeGrade(disk.TotalScore);
		sw.Stop();
		disk.Duration = sw.Elapsed;
		return disk;
	}

	public static string? FindDiskSpdExe()
	{
		try
		{
			string archSuffix = RuntimeInformation.ProcessArchitecture switch
			{
				Architecture.Arm64 => "A64",
				Architecture.X86 => "32",
				_ => "64",
			};
			string toolsRoot = ToolCatalog.ToolsRoot;
			if (!string.IsNullOrEmpty(toolsRoot))
			{
				string dir = Path.Combine(toolsRoot, "硬盘工具", "CrystalDiskMark", "CdmResource", "DiskSpd");
				if (Directory.Exists(dir))
				{
					string exe = Path.Combine(dir, "DiskSpd" + archSuffix + ".exe");
					if (File.Exists(exe)) return exe;
					exe = Path.Combine(dir, "DiskSpd" + archSuffix + "L.exe");
					if (File.Exists(exe)) return exe;
					exe = Path.Combine(dir, "DiskSpd64.exe");
					if (File.Exists(exe)) return exe;
				}
			}
			string baseDir = Path.Combine(AppContext.BaseDirectory, "Tools", "硬盘工具", "CrystalDiskMark", "CdmResource", "DiskSpd");
			if (Directory.Exists(baseDir))
			{
				string exe = Path.Combine(baseDir, "DiskSpd" + archSuffix + ".exe");
				if (File.Exists(exe)) return exe;
				exe = Path.Combine(baseDir, "DiskSpd64.exe");
				if (File.Exists(exe)) return exe;
			}
		}
		catch { }
		return null;
	}

	private static double RunDiskSpd(string diskSpdPath, string filePath, string fileSize, string blockSize, int threads, int outstanding, int writePct, int durationSec, CancellationToken ct)
	{
		try
		{
			string arguments = "-c" + fileSize + " -b" + blockSize + " -t" + threads + " -o" + outstanding + " -d" + durationSec + " -w" + writePct + " -Sh \"" + filePath + "\"";
			using var process = Process.Start(new ProcessStartInfo(diskSpdPath, arguments)
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			});
			if (process == null) return 0.0;
			string output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();
			string ioLabel = writePct == 0 ? "Read IO" : "Write IO";
			int idx = output.IndexOf(ioLabel, StringComparison.Ordinal);
			if (idx < 0) idx = output.IndexOf("Total IO", StringComparison.Ordinal);
			if (idx < 0) return 0.0;
			var match = Regex.Match(output.Substring(idx), @"total:\s+[\d]+\s+\|\s+[\d]+\s+\|\s+([\d.]+)\s+\|");
			if (match.Success && double.TryParse(match.Groups[1].Value, out var result)) return result;
			return 0.0;
		}
		catch { return 0.0; }
	}

	private static double RunDiskSpdIops(string diskSpdPath, string filePath, string fileSize, string blockSize, int threads, int outstanding, int writePct, int durationSec, CancellationToken ct)
	{
		try
		{
			string arguments = "-c" + fileSize + " -b" + blockSize + " -t" + threads + " -o" + outstanding + " -r -d" + durationSec + " -w" + writePct + " -Sh \"" + filePath + "\"";
			using var process = Process.Start(new ProcessStartInfo(diskSpdPath, arguments)
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			});
			if (process == null) return 0.0;
			string output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();
			string ioLabel = writePct == 0 ? "Read IO" : "Write IO";
			int idx = output.IndexOf(ioLabel, StringComparison.Ordinal);
			if (idx < 0) idx = output.IndexOf("Total IO", StringComparison.Ordinal);
			if (idx < 0) return 0.0;
			var match = Regex.Match(output.Substring(idx), @"total:\s+[\d]+\s+\|\s+[\d]+\s+\|\s+[\d.]+\s+\|\s+([\d.]+)");
			if (match.Success && double.TryParse(match.Groups[1].Value, out var result)) return result;
			return 0.0;
		}
		catch { return 0.0; }
	}

	public static string? FindCoreToCoreLatencyExe()
	{
		try
		{
			string toolsRoot = ToolCatalog.ToolsRoot;
			if (!string.IsNullOrEmpty(toolsRoot))
			{
				string exe = Path.Combine(toolsRoot, "处理器工具", "C2CLatency", "core-to-core-latency.exe");
				if (File.Exists(exe)) return exe;
			}
			string exe2 = Path.Combine(AppContext.BaseDirectory, "Tools", "处理器工具", "C2CLatency", "core-to-core-latency.exe");
			if (File.Exists(exe2)) return exe2;
		}
		catch { }
		return null;
	}

	public static InterCoreLatencyMatrix ParseCoreToCoreCsv(string csv, int maxCores)
	{
		int coreCount = Math.Min(maxCores, Environment.ProcessorCount);
		if (coreCount < 2) coreCount = 2;
		var latencies = new double[coreCount, coreCount];
		var values = new List<double>();
		for (int i = 0; i < coreCount; i++)
			for (int j = 0; j < coreCount; j++)
				latencies[i, j] = -1.0;
		string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		int lineCount = Math.Min(lines.Length, maxCores);
		if (lineCount > coreCount) coreCount = lineCount;
		for (int k = 0; k < Math.Min(lines.Length, coreCount); k++)
		{
			string[] cols = lines[k].Split(',');
			for (int l = 0; l < Math.Min(cols.Length, coreCount); l++)
			{
				string val = cols[l].Trim();
				if (!string.IsNullOrEmpty(val) && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
				{
					latencies[k, l] = num;
					if (k != l) values.Add(num);
				}
			}
		}
		for (int m = 0; m < coreCount; m++)
			latencies[m, m] = 0.0;
		return new InterCoreLatencyMatrix
		{
			CoreCount = coreCount,
			Latencies = latencies,
			AverageNs = values.Count > 0 ? values.Average() : 0.0,
			MinNs = values.Count > 0 ? values.Min() : 0.0,
			MaxNs = values.Count > 0 ? values.Max() : 0.0
		};
	}

	public static string? GenerateLatencyHeatmap(InterCoreLatencyMatrix lm)
	{
		try
		{
			int coreCount = lm.CoreCount;
			if (coreCount < 2) return null;
			int cellSize = Math.Max(28, Math.Min(48, 600 / coreCount));
			int leftMargin = 36;
			int topMargin = 24;
			int rightMargin = 80;
			int width = leftMargin + coreCount * cellSize + rightMargin;
			int height = topMargin + leftMargin + coreCount * cellSize;
			using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
			using var g = Graphics.FromImage(bitmap);
			g.SmoothingMode = SmoothingMode.HighQuality;
			g.TextRenderingHint = TextRenderingHint.AntiAlias;
			bool isDark = ThemeService.CurrentTheme == AppTheme.Dark;
			var bgColor = isDark ? Color.FromArgb(255, 30, 30, 30) : Color.FromArgb(255, 255, 255, 255);
			var titleColor = isDark ? Color.FromArgb(255, 200, 200, 200) : Color.FromArgb(255, 40, 40, 40);
			var labelColor = isDark ? Color.FromArgb(255, 140, 140, 140) : Color.FromArgb(255, 120, 120, 120);
			g.Clear(bgColor);
			using var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
			using var labelFont = new Font("Segoe UI", 7f);
			using var cellFont = new Font("Segoe UI", 6.5f);
			using var legendFont = new Font("Segoe UI", 7f);
			g.DrawString("Core-to-Core Latency (ns)", titleFont, new SolidBrush(titleColor), leftMargin, 4f);
			double maxVal = 0.0;
			for (int i = 0; i < coreCount; i++)
				for (int j = 0; j < coreCount; j++)
					if (lm.Latencies[i, j] > maxVal) maxVal = lm.Latencies[i, j];
			if (maxVal <= 0.0) maxVal = 200.0;
			for (int k = 0; k < coreCount; k++)
			{
				int x = leftMargin + k * cellSize;
				int y = topMargin + leftMargin + k * cellSize;
				var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
				g.DrawString(k.ToString(), labelFont, new SolidBrush(labelColor), x + cellSize / 2, topMargin + leftMargin / 2, format);
				g.DrawString(k.ToString(), labelFont, new SolidBrush(labelColor), leftMargin / 2, y + cellSize / 2, format);
			}
			for (int l = 0; l < coreCount; l++)
			{
				for (int m = 0; m < coreCount; m++)
				{
					int x2 = leftMargin + m * cellSize;
					int y2 = topMargin + leftMargin + l * cellSize;
					double val = lm.Latencies[l, m];
					Color cellColor;
					if (l == m)
					{
						cellColor = isDark ? Color.FromArgb(255, 20, 60, 20) : Color.FromArgb(255, 200, 240, 200);
					}
					else if (val < 0.0)
					{
						cellColor = isDark ? Color.FromArgb(255, 50, 50, 50) : Color.FromArgb(255, 230, 230, 230);
					}
					else
					{
						double ratio = Math.Min(val / maxVal, 1.0);
						if (ratio < 0.5)
						{
							double t = ratio * 2.0;
							cellColor = Color.FromArgb(255, (int)(30.0 + t * 200.0), (int)(180.0 - t * 80.0), (int)(30.0 + t * 20.0));
						}
						else
						{
							double t = (ratio - 0.5) * 2.0;
							cellColor = Color.FromArgb(255, (int)(230.0 + t * 25.0), (int)(100.0 - t * 80.0), (int)(50.0 - t * 30.0));
						}
					}
					using var brush = new SolidBrush(cellColor);
					g.FillRectangle(brush, x2 + 1, y2 + 1, cellSize - 2, cellSize - 2);
					if (val >= 0.0 && l != m)
					{
						var textColor = val / maxVal > 0.6 ? Color.White : Color.Black;
						var format2 = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
						g.DrawString(string.Format("{0:F0}", val), cellFont, new SolidBrush(textColor), x2 + cellSize / 2, y2 + cellSize / 2, format2);
					}
				}
			}
			int legendX = leftMargin + coreCount * cellSize + 8;
			int legendY = topMargin + leftMargin;
			int legendH = coreCount * cellSize;
			for (int n = 0; n < legendH; n++)
			{
				double ratio2 = 1.0 - (double)n / (double)legendH;
				Color legendColor;
				if (ratio2 < 0.5)
				{
					double t = ratio2 * 2.0;
					legendColor = Color.FromArgb(255, (int)(30.0 + t * 200.0), (int)(180.0 - t * 80.0), (int)(30.0 + t * 20.0));
				}
				else
				{
					double t = (ratio2 - 0.5) * 2.0;
					legendColor = Color.FromArgb(255, (int)(230.0 + t * 25.0), (int)(100.0 - t * 80.0), (int)(50.0 - t * 30.0));
				}
				using var legendBrush = new SolidBrush(legendColor);
				g.FillRectangle(legendBrush, legendX, legendY + n, 16, 1);
			}
			g.DrawString(string.Format("{0:F0}", maxVal), legendFont, new SolidBrush(labelColor), legendX + 20, legendY - 2);
			g.DrawString("0", legendFont, new SolidBrush(labelColor), legendX + 20, legendY + legendH - 12);
			g.DrawString("ns", legendFont, new SolidBrush(labelColor), legendX + 20, legendY + legendH / 2 - 6);
			string cacheDir = Path.Combine(ConfigManager.GetDataDir(), "BenchmarkCache");
			Directory.CreateDirectory(cacheDir);
			string outputPath = Path.Combine(cacheDir, "latency_heatmap_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
			bitmap.Save(outputPath, ImageFormat.Png);
			return outputPath;
		}
		catch { return null; }
	}

	private static float GetTotalMemoryGB()
	{
		try
		{
			var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
			try
			{
				using var enumerator = searcher.Get().GetEnumerator();
				if (enumerator.MoveNext())
				{
					return (float)Convert.ToInt64(enumerator.Current["TotalVisibleMemorySize"]) / 1024f / 1024f;
				}
			}
			finally
			{
				searcher.Dispose();
			}
		}
		catch { }
		return -1f;
	}

	private static int NormalizeCpuSingle(double rawScore)
	{
		if (rawScore <= 0.0) return 0;
		return Math.Max(0, (int)(rawScore / 728.0 * 100.0));
	}

	private static int NormalizeCpuMulti(double rawScore)
	{
		if (rawScore <= 0.0) return 0;
		return Math.Max(0, (int)(rawScore / 8351.0 * 100.0));
	}

	private static int NormalizeLatency(double ns)
	{
		if (ns <= 0.0) return 50;
		if (ns <= 20.0) return 100;
		if (ns >= 300.0) return 0;
		return Math.Max(0, (int)((300.0 - ns) / 280.0 * 100.0));
	}

	private static int NormalizeMemCapacity(float gb)
	{
		if (gb <= 0f) return 0;
		return (int)((double)gb / 32.0 * 100.0);
	}

	private static int NormalizeDiskSeq(double mbs)
	{
		if (mbs <= 0.0) return 0;
		return (int)(mbs / 5911.0 * 100.0);
	}

	private static int NormalizeDisk4K(double iops)
	{
		if (iops <= 0.0) return 0;
		return (int)(iops / 99000.0 * 100.0);
	}

	private static int NormalizeDiskTemp(float temp)
	{
		if (temp <= 0f) return 50;
		if (temp <= 30f) return 100;
		if (temp >= 70f) return 0;
		return Math.Max(0, (int)((double)(70f - temp) / 40.0 * 100.0));
	}

	private static int NormalizeFurMarkScore(int furmarkScore)
	{
		if (furmarkScore <= 0) return 0;
		return (int)((double)furmarkScore / 4000.0 * 100.0);
	}

	public static int ComputeGamingScore(PerformanceBenchmarkResult r)
	{
		return (int)((double)r.Cpu.SingleCoreScore * 0.25 + (double)r.Cpu.MultiCoreScore * 0.1 + (double)r.Gpu.RenderScore * 0.35 + (double)r.Memory.CapacityScore * 0.05 + (double)r.Disk.SeqReadScore * 0.05 + (double)r.Disk.SeqWriteScore * 0.03 + (double)r.Disk.Random4KReadScore * 0.05 + (double)r.Disk.Random4KWriteScore * 0.02 + (double)r.Browser.TotalScore * 0.1);
	}

	public static int ComputeOfficeScore(PerformanceBenchmarkResult r)
	{
		return (int)((double)r.Cpu.SingleCoreScore * 0.2 + (double)r.Cpu.MultiCoreScore * 0.05 + (double)r.Gpu.RenderScore * 0.05 + (double)r.Memory.CapacityScore * 0.12 + (double)r.Disk.SeqReadScore * 0.1 + (double)r.Disk.SeqWriteScore * 0.08 + (double)r.Disk.Random4KReadScore * 0.06 + (double)r.Disk.Random4KWriteScore * 0.05 + (double)r.Browser.TotalScore * 0.29);
	}

	public static string ComputeGrade(int score)
	{
		if (score >= 55)
		{
			if (score >= 100)
			{
				if (score >= 130) return "S";
				return "A+";
			}
			if (score >= 75) return "A";
			return "B+";
		}
		if (score >= 20)
		{
			if (score >= 40) return "B";
			return "C";
		}
		if (score >= 10) return "D";
		return "E";
	}

	public static List<PerformanceBenchmarkResult> LoadHistory()
	{
		string path = Path.Combine(ConfigManager.GetDataDir(), "BenchmarkHistory.json");
		if (!File.Exists(path)) return [];
		try
		{
			return JsonSerializer.Deserialize(File.ReadAllText(path), TubaDefaultContext.Default.ListPerformanceBenchmarkResult) ?? [];
		}
		catch { return []; }
	}

	public static void SaveHistory(PerformanceBenchmarkResult result)
	{
		try
		{
			var list = LoadHistory();
			list.Add(result);
			if (list.Count > 20)
			{
				list = list.TakeLast(20).ToList();
			}
			string path = Path.Combine(ConfigManager.GetDataDir(), "BenchmarkHistory.json");
			File.WriteAllText(path, JsonSerializer.Serialize(list, TubaDefaultContext.Default.ListPerformanceBenchmarkResult));
		}
		catch { }
	}

	public static void DeleteHistory(int index)
	{
		try
		{
			var list = LoadHistory();
			if (index >= 0 && index < list.Count)
			{
				list.RemoveAt(index);
				string path = Path.Combine(ConfigManager.GetDataDir(), "BenchmarkHistory.json");
				File.WriteAllText(path, JsonSerializer.Serialize(list, TubaDefaultContext.Default.ListPerformanceBenchmarkResult));
			}
		}
		catch { }
	}

	public static void ClearHistory()
	{
		try
		{
			string path = Path.Combine(ConfigManager.GetDataDir(), "BenchmarkHistory.json");
			if (File.Exists(path)) File.Delete(path);
		}
		catch { }
	}

	private static string GetOsName()
	{
		try
		{
			var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
			try
			{
				using var enumerator = searcher.Get().GetEnumerator();
				if (enumerator.MoveNext())
				{
					return enumerator.Current["Caption"]?.ToString() ?? "";
				}
			}
			finally
			{
				searcher.Dispose();
			}
		}
		catch { }
		return Environment.OSVersion.VersionString;
	}

	public static void PopulateHardwareInfo(PerformanceBenchmarkResult result)
	{
		result.OsName = GetOsName();
		try
		{
			var monitor = LiteMonitorService.Instance;
			monitor.EnsureInit();
			var sample = monitor.Read();
			result.CpuName = sample.CpuName;
			result.GpuName = sample.GpuName;
		}
		catch { }
		try
		{
			var detail = HardwareInfoService.LoadDetailAsync().GetAwaiter().GetResult();
			if (detail.Motherboard != null)
			{
				var mb = detail.Motherboard;
				result.MotherboardName = (mb.Manufacturer + " " + mb.Model).Trim();
				if (!string.IsNullOrWhiteSpace(mb.Chipset))
				{
					result.MotherboardName = result.MotherboardName + " (" + mb.Chipset + ")";
				}
				if (!string.IsNullOrWhiteSpace(mb.BiosVersion))
				{
					result.MotherboardName = result.MotherboardName + " BIOS " + mb.BiosVersion;
				}
			}
			if (detail.Memory != null)
			{
				var mem = detail.Memory;
				var memParts = new List<string>();
				if (!string.IsNullOrWhiteSpace(mem.TotalCapacity)) memParts.Add(mem.TotalCapacity);
				if (!string.IsNullOrWhiteSpace(mem.MemoryType)) memParts.Add(mem.MemoryType);
				if (!string.IsNullOrWhiteSpace(mem.ChannelMode)) memParts.Add(mem.ChannelMode);
				if (mem.Modules.Count > 0)
				{
					foreach (var module in mem.Modules)
					{
						var modParts = new List<string>();
						if (!string.IsNullOrWhiteSpace(module.Manufacturer)) modParts.Add(module.Manufacturer);
						if (!string.IsNullOrWhiteSpace(module.PartNumber)) modParts.Add(module.PartNumber);
						if (!string.IsNullOrWhiteSpace(module.Capacity)) modParts.Add(module.Capacity);
						if (!string.IsNullOrWhiteSpace(module.Speed)) modParts.Add(module.Speed);
						if (modParts.Count > 0) memParts.Add(string.Join(" ", modParts));
					}
				}
				result.MemoryInfo = string.Join(" | ", memParts);
			}
			if (detail.Disks.Count > 0)
			{
				var diskParts = new List<string>();
				foreach (var disk in detail.Disks)
				{
					var dParts = new List<string>();
					if (!string.IsNullOrWhiteSpace(disk.Model)) dParts.Add(disk.Model);
					if (!string.IsNullOrWhiteSpace(disk.MediaType)) dParts.Add(disk.MediaType);
					if (!string.IsNullOrWhiteSpace(disk.Size)) dParts.Add(disk.Size);
					if (!string.IsNullOrWhiteSpace(disk.InterfaceType)) dParts.Add(disk.InterfaceType);
					if (dParts.Count > 0) diskParts.Add(string.Join(" ", dParts));
				}
				result.DiskInfo = string.Join(" | ", diskParts);
			}
			if (detail.Displays.Count > 0)
			{
				var displayParts = new List<string>();
				foreach (var display in detail.Displays)
				{
					var dParts = new List<string>();
					if (!string.IsNullOrWhiteSpace(display.Name)) dParts.Add(display.Name);
					if (!string.IsNullOrWhiteSpace(display.Resolution)) dParts.Add(display.Resolution);
					if (!string.IsNullOrWhiteSpace(display.RefreshRate)) dParts.Add(display.RefreshRate);
					if (display.IsPrimary) dParts.Add("主显");
					if (dParts.Count > 0) displayParts.Add(string.Join(" ", dParts));
				}
				result.DisplayInfo = string.Join(" | ", displayParts);
			}
			if (string.IsNullOrWhiteSpace(result.CpuName) && detail.Cpu != null)
			{
				result.CpuName = detail.Cpu.Name ?? "";
			}
			if (string.IsNullOrWhiteSpace(result.GpuName) && detail.Gpus.Count > 0)
			{
				result.GpuName = detail.Gpus[0].Name ?? "";
			}
		}
		catch { }
	}

	public static string BuildReportJson(PerformanceBenchmarkResult result, string? latencyHeatmapPath = null)
	{
		var lm = result.Cpu.LatencyMatrix;
		double[][]? latArray = null;
		if (lm != null)
		{
			latArray = new double[lm.CoreCount][];
			for (int i = 0; i < lm.CoreCount; i++)
			{
				latArray[i] = new double[lm.CoreCount];
				for (int j = 0; j < lm.CoreCount; j++)
				{
					latArray[i][j] = lm.Latencies[i, j];
				}
			}
		}

		var root = new System.Text.Json.Nodes.JsonObject
		{
			["testTime"] = result.TestTime.ToString("yyyy-MM-dd HH:mm:ss"),
			["durationMode"] = result.DurationMode,
			["cpuName"] = result.CpuName,
			["gpuName"] = result.GpuName,
			["osName"] = result.OsName,
			["motherboardName"] = result.MotherboardName,
			["memoryInfo"] = result.MemoryInfo,
			["diskInfo"] = result.DiskInfo,
			["displayInfo"] = result.DisplayInfo,
			["gamingScore"] = result.GamingScore,
			["gamingGrade"] = result.GamingGrade,
			["officeScore"] = result.OfficeScore,
			["officeGrade"] = result.OfficeGrade
		};

		var cpuObj = new System.Text.Json.Nodes.JsonObject
		{
			["singleCoreScore"] = result.Cpu.SingleCoreScore,
			["singleCoreIterations"] = result.Cpu.SingleCoreIterations,
			["multiCoreScore"] = result.Cpu.MultiCoreScore,
			["multiCoreIterations"] = result.Cpu.MultiCoreIterations,
			["latencyScore"] = result.Cpu.LatencyScore
		};
		if (lm != null)
		{
			var latObj = new System.Text.Json.Nodes.JsonObject
			{
				["coreCount"] = lm.CoreCount,
				["averageNs"] = lm.AverageNs,
				["minNs"] = lm.MinNs,
				["maxNs"] = lm.MaxNs
			};
			var latArr = new System.Text.Json.Nodes.JsonArray();
			foreach (var row in latArray!)
			{
				var rowArr = new System.Text.Json.Nodes.JsonArray();
				foreach (var v in row) rowArr.Add(v);
				latArr.Add(rowArr);
			}
			latObj["latencies"] = latArr;
			if (latencyHeatmapPath != null && File.Exists(latencyHeatmapPath))
				latObj["heatmapBase64"] = Convert.ToBase64String(File.ReadAllBytes(latencyHeatmapPath));
			cpuObj["latencyMatrix"] = latObj;
		}
		root["cpu"] = cpuObj;

		root["gpu"] = new System.Text.Json.Nodes.JsonObject
		{
			["renderScore"] = result.Gpu.RenderScore,
			["renderFps"] = result.Gpu.RenderFps,
			["furMarkScore"] = result.Gpu.FurMarkScore,
			["avgFps"] = result.Gpu.AvgFps,
			["minFps"] = result.Gpu.MinFps,
			["maxFps"] = result.Gpu.MaxFps
		};

		root["memory"] = new System.Text.Json.Nodes.JsonObject
		{
			["capacityScore"] = result.Memory.CapacityScore,
			["totalCapacityGB"] = result.Memory.TotalCapacityGB
		};

		root["disk"] = new System.Text.Json.Nodes.JsonObject
		{
			["seqReadScore"] = result.Disk.SeqReadScore,
			["seqReadMBs"] = result.Disk.SeqReadMBs,
			["seqWriteScore"] = result.Disk.SeqWriteScore,
			["seqWriteMBs"] = result.Disk.SeqWriteMBs,
			["random4KReadScore"] = result.Disk.Random4KReadScore,
			["random4KReadIops"] = result.Disk.Random4KReadIops,
			["random4KWriteScore"] = result.Disk.Random4KWriteScore,
			["random4KWriteIops"] = result.Disk.Random4KWriteIops,
			["tempScore"] = result.Disk.TempScore,
			["temperature"] = result.Disk.Temperature
		};

		root["browser"] = new System.Text.Json.Nodes.JsonObject
		{
			["totalScore"] = result.Browser.TotalScore,
			["jsScore"] = result.Browser.JsScore,
			["domScore"] = result.Browser.DomScore,
			["cardScore"] = result.Browser.CardScore,
			["cssScore"] = result.Browser.CssScore,
			["layoutScore"] = result.Browser.LayoutScore,
			["eventScore"] = result.Browser.EventScore
		};

		return root.ToJsonString();
	}
}

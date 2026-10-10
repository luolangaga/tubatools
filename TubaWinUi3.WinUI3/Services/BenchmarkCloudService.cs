using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public enum LeaderboardSource
{
	GitHub,
	GitCode
}

public static class BenchmarkCloudService
{
	private const string UpstreamOwner = "luolangaga";
	private const string UpstreamRepo = "tubatoolsPlugin";
	private const string ReportsPath = "reports";
	private const string LatencyImagesPath = "reports/latency-images";
	private const string GitHubApiBase = "https://api.github.com/repos/luolangaga/tubatoolsPlugin";
	private const string LatencyImagesRawBase = "https://raw.githubusercontent.com/luolangaga/tubatoolsPlugin/main/reports/latency-images";
	private const string GitCodeApiBase = "https://api.gitcode.com/api/v5/repos/luolangaga/tubatoolsPlugin";
	
	private static readonly string GitHubLeaderboardUrl = "https://raw.githubusercontent.com/luolangaga/tubatoolsPlugin/main/leaderboard.json";
	private static readonly string GitCodeApiUrl = "https://api.gitcode.com/api/v5/repos/luolangaga/tubatoolsPlugin/contents/leaderboard.json";
	private static readonly string GitHubLeaderboardDetailsBase = "https://raw.githubusercontent.com/luolangaga/tubatoolsPlugin/main/leaderboard/details";
	private static readonly string GitCodeLeaderboardDetailsApiBase = "https://api.gitcode.com/api/v5/repos/luolangaga/tubatoolsPlugin/contents/leaderboard/details";
	private static readonly string GitHubPagedBase = "https://raw.githubusercontent.com/luolangaga/tubatoolsPlugin/main/leaderboard";
	private static readonly string GitCodePagedApiBase = "https://api.gitcode.com/api/v5/repos/luolangaga/tubatoolsPlugin/contents/leaderboard";
	
	private static LeaderboardSource _currentSource = LeaderboardSource.GitCode;
	public static LeaderboardSource CurrentSource
	{
		get => _currentSource;
		set
		{
			if (_currentSource != value)
			{
				_currentSource = value;
				InvalidateCache();
			}
		}
	}
	
	public static string CurrentSourceName => _currentSource switch
	{
		LeaderboardSource.GitCode => "GitCode",
		_ => "GitHub"
	};

	private static readonly HttpClient _apiClient;
	// UI 刷新与后台分页会并发读写这些缓存：普通 Dictionary 并发写会抛异常，统一加锁/换并发容器
	private static readonly object CacheLock = new();
	private static List<BenchmarkReportEntry>? _cache;
	private static DateTimeOffset _cacheTime;
	internal const int LeaderboardPageSize = 50;
	internal static readonly TimeSpan CacheDuration = TimeSpan.FromDays(6);
	private static BenchmarkLeaderboardData? _leaderboardCache;
	private static readonly ConcurrentDictionary<string, BenchmarkReportEntry> _reportDetailCache = new(StringComparer.OrdinalIgnoreCase);
	private static DateTimeOffset _leaderboardCacheTime;
	private static readonly ConcurrentDictionary<string, int> _pagedTotalPages = new();
	private static readonly ConcurrentDictionary<string, int> _pagedTotalEntries = new();
	private static readonly TimeSpan LatencyListCacheTtl = TimeSpan.FromMinutes(10);
	private static List<LatencyImageInfo>? _latencyListCache;
	private static DateTimeOffset _latencyListCacheTime;
	private static readonly ConcurrentDictionary<string, Task<byte[]>> _latencyInflight = new();

	private static string LatencyImageCacheDir => Path.Combine(ConfigManager.GetDataDir(), "Cache", "latency-images");

	static BenchmarkCloudService()
	{
		_apiClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(60)
		};
		_apiClient.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
	}

	internal static bool IsCacheFresh(DateTimeOffset cachedAt, DateTimeOffset now)
	{
		var age = now - cachedAt;
		return age >= TimeSpan.Zero && age < CacheDuration;
	}

	internal static List<T> GetLeaderboardPageSlice<T>(IReadOnlyList<T> entries, int page)
	{
		long start = (long)page * LeaderboardPageSize;
		if (page < 0 || start >= entries.Count) return [];

		int count = Math.Min(LeaderboardPageSize, entries.Count - (int)start);
		var result = new List<T>(count);
		for (int i = 0; i < count; i++)
			result.Add(entries[(int)start + i]);
		return result;
	}

	public static void InvalidateCache()
	{
		lock (CacheLock)
		{
			_cache = null;
			_cacheTime = DateTimeOffset.MinValue;
			_leaderboardCache = null;
			_leaderboardCacheTime = DateTimeOffset.MinValue;
			_reportDetailCache.Clear();
			_latencyListCache = null;
			_latencyListCacheTime = DateTimeOffset.MinValue;
			_pagedTotalPages.Clear();
			_pagedTotalEntries.Clear();
		}
	}

	public static List<BenchmarkReportEntry> LoadLocalCacheOnly()
	{
		return LoadLocalCache();
	}

	public static void SaveToCache(List<BenchmarkReportEntry> reports)
	{
		lock (CacheLock)
		{
			_cache = reports.OrderByDescending(r => r.GamingScore).ToList();
			_cacheTime = DateTimeOffset.UtcNow;
		}
		SaveLocalCache(reports);
	}


	public sealed class LatencyImageInfo
	{
		public string Name { get; init; } = "";
		public string RawUrl { get; init; } = "";
		public string HtmlUrl { get; init; } = "";
		public string Sha { get; init; } = "";
	}

	/// <summary>Uploads a standalone core-to-core latency heatmap image.</summary>
	public static async Task<string> UploadLatencyImageOnlyAsync(string cpuName, string latencyImagePath, IProgress<string>? progress, CancellationToken ct)
	{
		if (!GitHubAuthService.IsLoggedIn)
			throw new InvalidOperationException("请先登录 GitHub 账号");
		string token = GitHubAuthService.GetToken() ?? throw new InvalidOperationException("GitHub Token 无效");
		var user = (await GitHubAuthService.GetCurrentUserAsync(ct)) ?? throw new InvalidOperationException("无法获取 GitHub 用户信息");
		progress?.Report("正在 Fork 仓库...");
		string forkOwner = await EnsureForkAsync(token, ct);
		progress?.Report("正在同步 Fork...");
		await SyncForkWithUpstreamAsync(forkOwner, token, ct);
		string branchName = $"latency/{user.Login}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
		progress?.Report("正在创建分支...");
		string mainSha = (await GetRefShaAsync(forkOwner, "tubatoolsPlugin", "heads/main", token, ct))!;
		if (mainSha == null)
			throw new InvalidOperationException("无法获取 main 分支 SHA");
		await CreateRefAsync(forkOwner, "tubatoolsPlugin", "refs/heads/" + branchName, mainSha, token, ct);
		progress?.Report("正在上传核间延迟热力图...");
		string safeCpu = SanitizeFileName(cpuName);
		if (string.IsNullOrEmpty(safeCpu)) safeCpu = "Unknown-CPU";
		string imgName = await UploadLatencyImageWithRetryAsync(forkOwner, branchName, $"{safeCpu}-{user.Login}", latencyImagePath, token, ct);
		progress?.Report("正在创建 PR...");
		return await CreateLatencyPullRequestAsync(branchName, forkOwner, cpuName, user.Login, imgName, token, ct);
	}


	/// <summary>Lists all uploaded core-to-core latency heatmap images, following the current data source (GitHub / GitCode). Results are cached for 10 minutes.</summary>
	public static async Task<List<LatencyImageInfo>> GetLatencyImagesAsync(CancellationToken ct, bool refresh = false)
	{
		if (!refresh)
		{
			lock (CacheLock)
			{
				if (_latencyListCache != null && DateTimeOffset.UtcNow - _latencyListCacheTime < LatencyListCacheTtl)
				{
					return _latencyListCache;
				}
			}
		}
		var result = await FetchLatencyImagesAsync(ct);
		lock (CacheLock)
		{
			_latencyListCache = result;
			_latencyListCacheTime = DateTimeOffset.UtcNow;
		}
		return result;
	}

	private static async Task<List<LatencyImageInfo>> FetchLatencyImagesAsync(CancellationToken ct)
	{
		var result = new List<LatencyImageInfo>();
		if (_currentSource == LeaderboardSource.GitCode)
		{
			using var gcResp = await _apiClient.GetAsync($"{GitCodeApiBase}/contents/{LatencyImagesPath}", ct);
			if (gcResp.StatusCode == System.Net.HttpStatusCode.NotFound)
			{
				return [];
			}
			if (!gcResp.IsSuccessStatusCode)
			{
				throw new InvalidOperationException($"获取核间延迟图片列表失败：{(int)gcResp.StatusCode}");
			}
			using var gcDoc = JsonDocument.Parse(await gcResp.Content.ReadAsStringAsync(ct));
			foreach (var item in gcDoc.RootElement.EnumerateArray())
			{
				string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
				if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
				string downloadUrl = item.TryGetProperty("download_url", out var d) ? d.GetString() ?? "" : "";
				string htmlUrl = item.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
				string sha = item.TryGetProperty("sha", out var s) ? s.GetString() ?? "" : "";
				result.Add(new LatencyImageInfo
				{
					Name = name,
					RawUrl = downloadUrl,
					HtmlUrl = string.IsNullOrEmpty(htmlUrl) ? $"https://gitcode.com/luolangaga/tubatoolsPlugin/blob/main/{LatencyImagesPath}/{name}" : htmlUrl,
					Sha = sha
				});
			}
		}
		else
		{
			using var resp = await _apiClient.GetAsync($"{GitHubApiBase}/contents/{LatencyImagesPath}", ct);
			if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
			{
				return [];
			}
			if (!resp.IsSuccessStatusCode)
			{
				throw new InvalidOperationException($"获取核间延迟图片列表失败：{(int)resp.StatusCode}");
			}
			using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
			foreach (var item in doc.RootElement.EnumerateArray())
			{
				string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
				if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
				string downloadUrl = item.TryGetProperty("download_url", out var d) ? d.GetString() ?? "" : "";
				string htmlUrl = item.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
				string sha = item.TryGetProperty("sha", out var s) ? s.GetString() ?? "" : "";
				result.Add(new LatencyImageInfo
				{
					Name = name,
					RawUrl = string.IsNullOrEmpty(downloadUrl) ? $"{LatencyImagesRawBase}/{name}" : downloadUrl,
					HtmlUrl = htmlUrl,
					Sha = sha
				});
			}
		}
		return result.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary>
	/// Downloads a latency heatmap image with fallbacks for the current data source.
	/// GitHub — direct raw, GitHub mirror proxies, then the official git/blobs API.
	/// GitCode — raw blob URL, the official git/blobs API (docs.atomgit.com), then a fresh
	/// download URL re-resolved from the contents API. All candidates race; the first success wins.
	/// </summary>
	public static async Task<byte[]> DownloadLatencyImageAsync(LatencyImageInfo info, CancellationToken ct)
	{
		var candidates = new List<(string Label, Func<CancellationToken, Task<byte[]?>> Fetch)>();
		if (_currentSource == LeaderboardSource.GitCode)
		{
			candidates.Add(("GitCode 直连", token => DownloadBytesAsync(info.RawUrl, token)));
			if (!string.IsNullOrEmpty(info.Sha))
			{
				candidates.Add(("GitCode Blob 接口", token => FetchBlobApiBytesAsync($"{GitCodeApiBase}/git/blobs/{info.Sha}", token)));
			}
			candidates.Add(("GitCode 接口重试", async token =>
			{
				string? fresh = await ResolveGitCodeLatencyImageUrlAsync(info.Name, token);
				return string.IsNullOrEmpty(fresh) ? null : await DownloadBytesAsync(fresh, token);
			}));
		}
		else
		{
			candidates.Add(("GitHub 直连", token => DownloadBytesAsync(info.RawUrl, token)));
			foreach (var proxy in GitHubReleaseService.GitHubProxies.Take(10))
			{
				candidates.Add((new Uri(proxy).Host, token => DownloadBytesAsync(GitHubReleaseService.ProxyUrl(proxy, info.RawUrl), token)));
			}
			if (!string.IsNullOrEmpty(info.Sha))
			{
				candidates.Add(("GitHub Blob 接口", token => FetchBlobApiBytesAsync($"{GitHubApiBase}/git/blobs/{info.Sha}", token)));
			}
		}

		var tasks = candidates.Select(async c =>
		{
			try
			{
				byte[]? bytes = await c.Fetch(ct);
				return (c.Label, bytes, (string?)null);
			}
			catch (Exception ex)
			{
				return (c.Label, (byte[]?)null, ex.Message);
			}
		}).ToList();

		if (tasks.Count == 0)
		{
			throw new InvalidOperationException("图片下载失败：无法获取图片下载地址");
		}

		var failures = new List<string>();
		while (tasks.Count > 0)
		{
			var done = await Task.WhenAny(tasks);
			tasks.Remove(done);
			var (label, bytes, error) = await done;
			if (bytes is not null) return bytes;
			failures.Add($"{label}: {error ?? "失败"}");
		}
		throw new InvalidOperationException("图片下载失败：" + string.Join("；", failures.Take(3)) + (failures.Count > 3 ? "…" : ""));
	}

	/// <summary>
	/// Returns a latency heatmap image's bytes, reading from the local disk cache
	/// (keyed by the blob SHA) when available, otherwise downloading through
	/// <see cref="DownloadLatencyImageAsync"/> and caching the result. Concurrent
	/// requests for the same image share a single in-flight download.
	/// </summary>
	public static async Task<byte[]> GetLatencyImageBytesAsync(LatencyImageInfo info, CancellationToken ct)
	{
		string key = !string.IsNullOrEmpty(info.Sha) ? info.Sha : ComputeSha(info.RawUrl);
		string cachedPath = Path.Combine(LatencyImageCacheDir, key + ".png");
		try
		{
			if (File.Exists(cachedPath))
			{
				return await File.ReadAllBytesAsync(cachedPath, ct);
			}
		}
		catch { }

		if (_latencyInflight.TryGetValue(key, out var inflight))
		{
			return await inflight;
		}
		var task = DownloadLatencyImageAndCacheAsync(info, cachedPath, ct);
		_latencyInflight[key] = task;
		try
		{
			return await task;
		}
		finally
		{
			_latencyInflight.TryRemove(key, out _);
		}
	}

	private static async Task<byte[]> DownloadLatencyImageAndCacheAsync(LatencyImageInfo info, string cachedPath, CancellationToken ct)
	{
		byte[] bytes = await DownloadLatencyImageAsync(info, ct);
		try
		{
			var dir = Path.GetDirectoryName(cachedPath);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			await File.WriteAllBytesAsync(cachedPath, bytes, ct);
		}
		catch { }
		return bytes;
	}

	private static string ComputeSha(string value)
	{
		return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
	}

	private static async Task<byte[]?> DownloadBytesAsync(string url, CancellationToken ct)
	{
		if (string.IsNullOrEmpty(url)) return null;
		using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
		client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
		return await client.GetByteArrayAsync(new Uri(url), ct);
	}

	private static async Task<byte[]?> FetchBlobApiBytesAsync(string url, CancellationToken ct)
	{
		using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
		client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
		string json = await client.GetStringAsync(url, ct);
		using var doc = JsonDocument.Parse(json);
		if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return Convert.FromBase64String(content.GetString()!);
	}

	private static async Task<string?> ResolveGitCodeLatencyImageUrlAsync(string name, CancellationToken ct)
	{
		try
		{
			using var resp = await _apiClient.GetAsync($"{GitCodeApiBase}/contents/{LatencyImagesPath}/{Uri.EscapeDataString(name)}", ct);
			if (!resp.IsSuccessStatusCode) return null;
			using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
			return doc.RootElement.TryGetProperty("download_url", out var d) ? d.GetString() : null;
		}
		catch { return null; }
	}

	private static async Task<int> GetLatencyImageNextSeqAsync(string prefix, CancellationToken ct)
	{
		try
		{
			using var client = GitHubAuthService.IsLoggedIn ? GitHubAuthService.CreateAuthenticatedClient() : _apiClient;
			using var resp = await client.GetAsync($"{GitHubApiBase}/contents/{LatencyImagesPath}", ct);
			if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return 1;
			if (!resp.IsSuccessStatusCode) return 0;
			using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
			int maxSeq = 0;
			foreach (var item in doc.RootElement.EnumerateArray())
			{
				string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
				if (!name.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)) continue;
				string numPart = name[prefix.Length..];
				if (!numPart.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
				numPart = numPart[..^4];
				if (int.TryParse(numPart, out int seq) && seq > maxSeq) maxSeq = seq;
			}
			return maxSeq + 1;
		}
		catch { return 0; }
	}

	/// <summary>Uploads a latency heatmap PNG, retrying with the next sequence number when the target file already exists (422).</summary>
	private static async Task<string> UploadLatencyImageWithRetryAsync(string forkOwner, string branchName, string prefix, string localPath, string token, CancellationToken ct)
	{
		int seq = await GetLatencyImageNextSeqAsync(prefix, ct);
		if (seq < 1) seq = 1;
		for (int attempt = 0; attempt < 30; attempt++)
		{
			string imgName = $"{prefix}-{seq}.png";
			try
			{
				await CreateBinaryFileAsync(forkOwner, "tubatoolsPlugin", $"{LatencyImagesPath}/{imgName}", branchName, localPath, token, ct);
				return imgName;
			}
			catch (Exception ex) when (ex.Message.Contains("422", StringComparison.Ordinal))
			{
				seq++;
			}
		}
		throw new InvalidOperationException("上传核间延迟热力图失败：无法分配唯一的文件名，请稍后重试");
	}

	private static string SanitizeFileName(string name)
	{
		var chars = name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray();
		return new string(chars).Trim();
	}

	private static BenchmarkReportEntry ToReportEntry(PerformanceBenchmarkResult result, string author)
	{
		string id = $"{author}-{result.TestTime:yyyyMMdd-HHmmss}";
		return new BenchmarkReportEntry
		{
			Id = id,
			Author = author,
			SubmittedAt = DateTimeOffset.UtcNow,
			CpuName = result.CpuName,
			GpuName = result.GpuName,
			OsName = result.OsName,
			MotherboardName = result.MotherboardName,
			MemoryInfo = result.MemoryInfo,
			DiskInfo = result.DiskInfo,
			DisplayInfo = result.DisplayInfo,
			GamingScore = result.GamingScore,
			GamingGrade = result.GamingGrade,
			OfficeScore = result.OfficeScore,
			OfficeGrade = result.OfficeGrade,
			CpuSingleCoreScore = result.Cpu.SingleCoreScore,
			CpuMultiCoreScore = result.Cpu.MultiCoreScore,
			GpuRenderScore = result.Gpu.RenderScore,
			MemoryCapacityScore = result.Memory.CapacityScore,
			DiskSeqReadScore = result.Disk.SeqReadScore,
			DiskSeqWriteScore = result.Disk.SeqWriteScore,
			Disk4KReadScore = result.Disk.Random4KReadScore,
			Disk4KWriteScore = result.Disk.Random4KWriteScore,
			BrowserTotalScore = result.Browser.TotalScore
		};
	}

	public static async Task<string> DeleteReportAsync(BenchmarkReportEntry entry, IProgress<string>? progress, CancellationToken ct)
	{
		if (!GitHubAuthService.IsLoggedIn)
		{
			throw new InvalidOperationException("请先登录 GitHub 账号");
		}
		string token = GitHubAuthService.GetToken() ?? throw new InvalidOperationException("GitHub Token 无效");
		if ((await GitHubAuthService.GetCurrentUserAsync(ct) ?? throw new InvalidOperationException("无法获取 GitHub 用户信息")).Login != entry.Author)
		{
			throw new InvalidOperationException("只能删除自己上传的报告");
		}
		progress?.Report("正在 Fork 仓库...");
		string forkOwner = await EnsureForkAsync(token, ct);
		progress?.Report("正在同步 Fork...");
		await SyncForkWithUpstreamAsync(forkOwner, token, ct);
		string branchName = "delete/" + entry.Id;
		string? existingPrUrl = await FindOpenPullRequestUrlAsync(forkOwner, branchName, ct);
		if (existingPrUrl != null)
		{
			InvalidateCache();
			return existingPrUrl;
		}
		progress?.Report("正在创建分支...");
		string mainSha = (await GetRefShaAsync(forkOwner, "tubatoolsPlugin", "heads/main", token, ct))!;
		if (mainSha == null)
		{
			throw new InvalidOperationException("无法获取 main 分支 SHA");
		}
		if (await CheckRefExistsAsync(forkOwner, "tubatoolsPlugin", "heads/" + branchName, token, ct))
		{
			branchName = $"delete/{entry.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
		}
		await CreateRefAsync(forkOwner, "tubatoolsPlugin", "refs/heads/" + branchName, mainSha, token, ct);
		progress?.Report("正在删除文件...");
		await DeleteFileAsync(forkOwner, "tubatoolsPlugin", entry.RepoPath, branchName, token, ct);
		progress?.Report("正在创建 PR...");
		string prUrl = await CreateDeletePullRequestAsync(branchName, forkOwner, entry, token, ct);
		InvalidateCache();
		return prUrl;
	}

	public static async Task<List<BenchmarkReportEntry>> GetAllReportsAsync(CancellationToken ct)
	{
		lock (CacheLock)
		{
			if (_cache != null && IsCacheFresh(_cacheTime, DateTimeOffset.UtcNow))
			{
				return _cache;
			}
		}
		Exception? lastError = null;
		try
		{
			var leaderboardData = await GetLeaderboardDataAsync(ct);
			if (leaderboardData != null)
			{
				List<BenchmarkReportEntry>? reports = null;

				// 新结构（单列）：reports 就是全部报告集合（服务器已按综合榜降序）
				if (leaderboardData.Reports is { Count: > 0 })
				{
					reports = leaderboardData.Reports.Select(e => e.ToReportEntry()).ToList();
				}
				// 兼容中间版结构：reports 摘要 + boards id 列表
				else if (leaderboardData.Boards is not null && leaderboardData.Reports is not null &&
					leaderboardData.Boards.TryGetValue("gaming", out var gamingIds))
				{
					var byId = leaderboardData.Reports.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
					reports = gamingIds
						.Select(id => byId.TryGetValue(id, out var e) ? e : null)
						.Where(e => e is not null)
						.Select(e => e!.ToReportEntry())
						.ToList();
				}
				// 旧结构：gaming 榜条目即全量报告
				else if (leaderboardData.Leaderboards?.TryGetValue("gaming", out var gamingList) == true)
				{
					reports = gamingList.Select(e => e.ToReportEntry()).ToList();
				}

				if (reports is not null)
				{
					SaveLocalCache(reports);
					lock (CacheLock)
					{
						_cache = reports;
						_cacheTime = DateTimeOffset.UtcNow;
					}
					return reports;
				}
			}
		}
		catch (Exception ex)
		{
			lastError = ex;
		}
		try
		{
			return await GetAllReportsFallbackAsync(ct);
		}
		catch (Exception ex)
		{
			if (lastError != null)
				throw new AggregateException(lastError, ex);
			throw;
		}
	}

	public static async Task<BenchmarkLeaderboardData?> GetLeaderboardDataAsync(CancellationToken ct)
	{
		lock (CacheLock)
		{
			if (_leaderboardCache != null && IsCacheFresh(_leaderboardCacheTime, DateTimeOffset.UtcNow))
			{
				return _leaderboardCache;
			}
		}
		using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
		client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
		
		string json;
		if (_currentSource == LeaderboardSource.GitCode)
		{
			var apiResp = await client.GetAsync(GitCodeApiUrl, ct);
			if (!apiResp.IsSuccessStatusCode)
				throw new HttpRequestException($"GitCode API 请求失败: HTTP {(int)apiResp.StatusCode} {apiResp.StatusCode}");
			var apiJson = await apiResp.Content.ReadAsStringAsync(ct);
			using var apiDoc = JsonDocument.Parse(apiJson);
			var downloadUrl = apiDoc.RootElement.GetProperty("download_url").GetString();
			if (string.IsNullOrEmpty(downloadUrl))
				throw new Exception("GitCode API 未返回 download_url");
			json = await client.GetStringAsync(downloadUrl, ct);
		}
		else
		{
			var resp = await client.GetAsync(GitHubLeaderboardUrl, ct);
			if (!resp.IsSuccessStatusCode)
				throw new HttpRequestException($"GitHub 数据加载失败: HTTP {(int)resp.StatusCode} {resp.StatusCode}");
			json = await resp.Content.ReadAsStringAsync(ct);
		}
		
		var data = JsonSerializer.Deserialize<BenchmarkLeaderboardData>(json, new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});
		if (data == null)
			throw new JsonException("排行榜数据解析失败");
		lock (CacheLock)
		{
			_leaderboardCache = data;
			_leaderboardCacheTime = DateTimeOffset.UtcNow;
		}
		return data;
	}

	public static async Task<List<BenchmarkLeaderboardEntry>> GetLeaderboardAsync(string sortBy, string? cpuFilter = null, string? gpuFilter = null, CancellationToken ct = default)
	{
		var data = await GetLeaderboardDataAsync(ct);
		if (data == null)
		{
			var allReports = await GetAllReportsAsync(ct);
			return ComputeLeaderboard(allReports, sortBy, cpuFilter, gpuFilter);
		}

		IEnumerable<BenchmarkLeaderboardRankEntry> source;

		// 新结构（单列）：reports 只存一份信息，各维度排序由客户端本地算
		if (data.Reports is { Count: > 0 })
		{
			Func<BenchmarkLeaderboardRankEntry, int> scoreOf = sortBy switch
			{
				"office" => static e => e.OfficeScore,
				"cpu" => static e => e.CpuMultiCoreScore,
				"gpu" => static e => e.GpuRenderScore,
				"disk" => static e => e.DiskSeqReadScore,
				"browser" => static e => e.BrowserTotalScore,
				_ => static e => e.GamingScore,
			};
			source = data.Reports.OrderByDescending(scoreOf);
		}
		// 兼容中间版结构：boards 存 id 有序列表，按 id 回查 reports 摘要
		else if (data.Boards is not null && data.Reports is not null &&
			data.Boards.TryGetValue(sortBy, out var boardIds))
		{
			var byId = data.Reports.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
			source = boardIds
				.Select(id => byId.TryGetValue(id, out var e) ? e : null)
				.Where(e => e is not null)
				.Select(e => e!);
		}
		// 旧结构：每条自带全量字段
		else if (data.Leaderboards?.TryGetValue(sortBy, out var entries) == true)
		{
			source = entries;
		}
		else
		{
			source = data.Leaderboards?.GetValueOrDefault("gaming", []) ?? [];
		}

		if (!string.IsNullOrWhiteSpace(cpuFilter))
		{
			source = source.Where(e => e.CpuName.Contains(cpuFilter, StringComparison.OrdinalIgnoreCase));
		}
		if (!string.IsNullOrWhiteSpace(gpuFilter))
		{
			source = source.Where(e => e.GpuName.Contains(gpuFilter, StringComparison.OrdinalIgnoreCase));
		}
		return source.Select((e, i) => new BenchmarkLeaderboardEntry
		{
			Rank = i + 1,
			Report = e.ToReportEntry()
		}).ToList();
	}

	/// <summary>
	/// 按需加载报告详情（leaderboard/details/{id}.json）。
	/// 新 leaderboard.json 只含摘要，硬件详情（OS/主板/内存/硬盘/显示器）在详情文件里。
	/// 失败返回 null，调用方回退到摘要字段。
	/// </summary>
	public static async Task<BenchmarkReportEntry?> GetReportDetailAsync(BenchmarkReportEntry report, CancellationToken ct = default)
	{
		if (report is null || string.IsNullOrWhiteSpace(report.Id)) return null;
		if (_reportDetailCache.TryGetValue(report.Id, out var cached)) return cached;

		try
		{
			var id = Uri.EscapeDataString(report.Id);
			string json;

			if (_currentSource == LeaderboardSource.GitCode)
			{
				using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
				client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
				var apiResp = await client.GetAsync($"{GitCodeLeaderboardDetailsApiBase}/{id}.json", ct);
				if (!apiResp.IsSuccessStatusCode) return null;
				var apiJson = await apiResp.Content.ReadAsStringAsync(ct);
				using var apiDoc = JsonDocument.Parse(apiJson);
				if (!apiDoc.RootElement.TryGetProperty("download_url", out var du) ||
					du.GetString() is not { Length: > 0 } downloadUrl)
					return null;
				json = await client.GetStringAsync(downloadUrl, ct);
			}
			else
			{
				using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
				client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
				var resp = await client.GetAsync($"{GitHubLeaderboardDetailsBase}/{id}.json", ct);
				if (!resp.IsSuccessStatusCode) return null;
				json = await resp.Content.ReadAsStringAsync(ct);
			}

			var detail = JsonSerializer.Deserialize<BenchmarkReportEntry>(json, new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});
			if (detail is null || string.IsNullOrWhiteSpace(detail.Id)) return null;

			// 详情文件里没有 DetailsPath，补上以免二次判空
			detail.DetailsPath = report.DetailsPath;
			_reportDetailCache[report.Id] = detail;
			return detail;
		}
		catch (OperationCanceledException) { throw; }
		catch
		{
			return null;
		}
	}

	public static async Task<BenchmarkLeaderboardPage?> GetLeaderboardPageAsync(string sortBy, int page, CancellationToken ct)
	{
		string relativePath = page == 0
			? $"leaderboard/{sortBy}/{sortBy}.json"
			: $"leaderboard/{sortBy}/{sortBy}_{page}.json";

		using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
		client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");

		string json;
		if (_currentSource == LeaderboardSource.GitCode)
		{
			var apiResp = await client.GetAsync($"{GitCodePagedApiBase}/{sortBy}/{Path.GetFileName(relativePath)}", ct);
			if (!apiResp.IsSuccessStatusCode)
				throw new HttpRequestException($"GitCode API 请求失败: HTTP {(int)apiResp.StatusCode}");
			var apiJson = await apiResp.Content.ReadAsStringAsync(ct);
			using var apiDoc = JsonDocument.Parse(apiJson);
			var downloadUrl = apiDoc.RootElement.GetProperty("download_url").GetString();
			if (string.IsNullOrEmpty(downloadUrl))
				throw new Exception("GitCode API 未返回 download_url");
			json = await client.GetStringAsync(downloadUrl, ct);
		}
		else
		{
			var url = $"{GitHubPagedBase}/{sortBy}/{Path.GetFileName(relativePath)}";
			var resp = await client.GetAsync(url, ct);
			if (!resp.IsSuccessStatusCode)
				throw new HttpRequestException($"GitHub 分页数据加载失败: HTTP {(int)resp.StatusCode}");
			json = await resp.Content.ReadAsStringAsync(ct);
		}

		var data = JsonSerializer.Deserialize<BenchmarkLeaderboardPage>(json, new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});
		if (data == null)
			throw new JsonException("分页数据解析失败");

		_pagedTotalPages[sortBy] = data.TotalPages;
		_pagedTotalEntries[sortBy] = data.TotalEntries;

		return data;
	}

	public static bool HasMorePages(string sortBy, int currentPage)
	{
		if (_pagedTotalPages.TryGetValue(sortBy, out var totalPages))
			return currentPage < totalPages - 1;
		return false;
	}

	public static int GetTotalPages(string sortBy)
	{
		return _pagedTotalPages.GetValueOrDefault(sortBy, 0);
	}

	public static int GetTotalEntries(string sortBy)
	{
		return _pagedTotalEntries.GetValueOrDefault(sortBy, 0);
	}

	private static async Task<List<BenchmarkReportEntry>> GetAllReportsFallbackAsync(CancellationToken ct)
	{
		var reports = new ConcurrentBag<BenchmarkReportEntry>();
		var localCache = LoadLocalCache();
		string? treeSha = null;
		try
		{
			treeSha = await GetLatestTreeShaAsync(ct);
		}
		catch (Exception ex)
		{
			if (localCache.Count > 0)
			{
				var fallbackReports = localCache.OrderByDescending(r => r.GamingScore).ToList();
				lock (CacheLock)
				{
					_cache = fallbackReports;
					_cacheTime = DateTimeOffset.UtcNow;
				}
				return fallbackReports;
			}
			throw new Exception("无法获取仓库信息: " + ex.Message, ex);
		}
		if (treeSha == null)
		{
			if (localCache.Count > 0)
			{
				var fallbackReports = localCache.OrderByDescending(r => r.GamingScore).ToList();
				lock (CacheLock)
				{
					_cache = fallbackReports;
					_cacheTime = DateTimeOffset.UtcNow;
				}
				return fallbackReports;
			}
			throw new Exception("无法获取仓库树信息");
		}
		var allFiles = await GetRecursiveTreeBlobsAsync(treeSha, "reports", ct);
		if (allFiles.Count == 0)
		{
			if (localCache.Count > 0)
			{
				var fallbackReports = localCache.OrderByDescending(r => r.GamingScore).ToList();
				lock (CacheLock)
				{
					_cache = fallbackReports;
					_cacheTime = DateTimeOffset.UtcNow;
				}
				return fallbackReports;
			}
			throw new Exception("仓库中暂无报告数据");
		}
		var toDownload = new List<(string Path, string Sha)>();
		var cachedSet = new HashSet<string>(localCache.Select(r => r.RepoPath));
		foreach (var (path, sha) in allFiles)
		{
			if (cachedSet.Contains(path))
			{
				var cached = localCache.FirstOrDefault(r => r.RepoPath == path);
				if (cached != null) reports.Add(cached);
			}
			else
			{
				toDownload.Add((path, sha));
			}
		}
		if (toDownload.Count > 0)
		{
			await Parallel.ForEachAsync(toDownload, new ParallelOptions
			{
				MaxDegreeOfParallelism = 6,
				CancellationToken = ct
			}, async (item, token) =>
			{
				try
				{
					string content = (await DownloadBlobAsync(item.Sha, token))!;
					if (content != null)
					{
						var entry = JsonSerializer.Deserialize<BenchmarkReportEntry>(content, new JsonSerializerOptions
						{
							PropertyNamingPolicy = JsonNamingPolicy.CamelCase
						});
						if (entry != null)
						{
							entry.RepoPath = item.Path;
							reports.Add(entry);
						}
					}
				}
				catch
				{
				}
			});
		}
		SaveLocalCache(reports.ToList());
		var allReports = reports.OrderByDescending(r => r.GamingScore).ToList();
		lock (CacheLock)
		{
			_cache = allReports;
			_cacheTime = DateTimeOffset.UtcNow;
		}
		return allReports;
	}

	private static string LocalCachePath => Path.Combine(
		RuntimeHelper.GetLocalAppDataRoot(),
		"TubaWinUi3", "benchmark_cache.json");

	private static List<BenchmarkReportEntry> LoadLocalCache()
	{
		try
		{
			if (!File.Exists(LocalCachePath)) return [];
			var cachedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(LocalCachePath), TimeSpan.Zero);
			if (!IsCacheFresh(cachedAt, DateTimeOffset.UtcNow)) return [];
			string json = File.ReadAllText(LocalCachePath);
			return JsonSerializer.Deserialize<List<BenchmarkReportEntry>>(json, new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			}) ?? [];
		}
		catch
		{
			return [];
		}
	}

	private static void SaveLocalCache(List<BenchmarkReportEntry> reports)
	{
		try
		{
			var dir = Path.GetDirectoryName(LocalCachePath);
			if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			string json = JsonSerializer.Serialize(reports, new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				WriteIndented = false
			});
			File.WriteAllText(LocalCachePath, json);
		}
		catch
		{
		}
	}

	private static async Task<List<(string Path, string Sha)>> GetRecursiveTreeBlobsAsync(string treeSha, string prefix, CancellationToken ct)
	{
		var result = new List<(string, string)>();
		using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
		client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
		string url = $"https://api.github.com/repos/luolangaga/tubatoolsPlugin/git/trees/{treeSha}?recursive=1";
		string json = await client.GetStringAsync(url, ct);
		using var doc = JsonDocument.Parse(json);
		if (!doc.RootElement.TryGetProperty("tree", out var tree)) return result;
		bool inReports = false;
		foreach (var item in tree.EnumerateArray())
		{
			string path = item.GetProperty("path").GetString() ?? "";
			string type = item.GetProperty("type").GetString() ?? "";
			if (!inReports && (path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal)))
			{
				inReports = true;
			}
			if (inReports && type == "blob" && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				string sha = item.GetProperty("sha").GetString() ?? "";
				result.Add((path, sha));
			}
			if (inReports && !path.StartsWith(prefix + "/", StringComparison.Ordinal) && path != prefix)
			{
				break;
			}
		}
		return result;
	}

	public static List<BenchmarkLeaderboardEntry> ComputeLeaderboard(List<BenchmarkReportEntry> reports, string sortBy, string? cpuFilter = null, string? gpuFilter = null)
	{
		IEnumerable<BenchmarkReportEntry> source = reports.AsEnumerable();
		if (!string.IsNullOrWhiteSpace(cpuFilter))
		{
			source = source.Where(r => r.CpuName.Contains(cpuFilter, StringComparison.OrdinalIgnoreCase));
		}
		if (!string.IsNullOrWhiteSpace(gpuFilter))
		{
			source = source.Where(r => r.GpuName.Contains(gpuFilter, StringComparison.OrdinalIgnoreCase));
		}
		return (sortBy switch
		{
			"gaming" => source.OrderByDescending(r => r.GamingScore),
			"office" => source.OrderByDescending(r => r.OfficeScore),
			"cpu" => source.OrderByDescending(r => r.CpuMultiCoreScore),
			"gpu" => source.OrderByDescending(r => r.GpuRenderScore),
			"disk" => source.OrderByDescending(r => r.DiskSeqReadScore),
			"browser" => source.OrderByDescending(r => r.BrowserTotalScore),
			_ => source.OrderByDescending(r => r.GamingScore),
		}).ToList().Select((r, i) => new BenchmarkLeaderboardEntry
		{
			Rank = i + 1,
			Report = r
		}).ToList();
	}

	public static List<BenchmarkLeaderboardEntry> ComputeSameHardwareLeaderboard(List<BenchmarkReportEntry> reports, string cpuName, string gpuName)
	{
		return (from r in reports
			where IsSameHardware(r.CpuName, cpuName) && IsSameHardware(r.GpuName, gpuName)
			orderby r.GamingScore descending
			select r).ToList().Select((r, i) => new BenchmarkLeaderboardEntry
		{
			Rank = i + 1,
			Report = r
		}).ToList();
	}

	private static bool IsSameHardware(string a, string b)
	{
		if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
		{
			return false;
		}
		string sa = Simplify(a);
		string sb = Simplify(b);
		if (sa == sb || sa.Contains(sb, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return sb.Contains(sa, StringComparison.OrdinalIgnoreCase);
	}

	private static string Simplify(string s)
	{
		return s.Replace("(R)", "").Replace("(TM)", "").Replace("(C)", "")
			.Replace("@", "")
			.Replace("CPU", "")
			.Replace("Processor", "")
			.Replace("Graphics", "")
			.Replace("GPU", "")
			.Trim();
	}

	public static async Task<List<BenchmarkReportEntry>> GetMyReportsAsync(CancellationToken ct)
	{
		if (!GitHubAuthService.IsLoggedIn)
		{
			return [];
		}
		var user = await GitHubAuthService.GetCurrentUserAsync(ct);
		if (user == null)
		{
			return [];
		}
		return (from r in await GetAllReportsAsync(ct)
			where r.Author == user.Login
			orderby r.SubmittedAt descending
			select r).ToList();
	}

	private static async Task<string> EnsureForkAsync(string token, CancellationToken ct)
	{
		string forkOwner = (await GitHubAuthService.GetCurrentUserAsync(ct))?.Login ?? throw new InvalidOperationException("无法获取用户名");
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		try
		{
			if ((await client.GetAsync($"https://api.github.com/repos/{forkOwner}/tubatoolsPlugin", ct)).IsSuccessStatusCode)
			{
				return forkOwner;
			}
		}
		catch
		{
		}
		var forkResp = await client.PostAsync("https://api.github.com/repos/luolangaga/tubatoolsPlugin/forks", new StringContent("{}", Encoding.UTF8, "application/json"), ct);
		if (!forkResp.IsSuccessStatusCode)
		{
			string body = await forkResp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"Fork 失败：{(int)forkResp.StatusCode} {forkResp.StatusCode}\n{body}");
		}
		await Task.Delay(3000, ct);
		return forkOwner;
	}

	private static async Task SyncForkWithUpstreamAsync(string forkOwner, string token, CancellationToken ct)
	{
		string upstreamMainSha = (await GetRefShaAsync("luolangaga", "tubatoolsPlugin", "heads/main", token, ct))!;
		if (upstreamMainSha == null || await GetRefShaAsync(forkOwner, "tubatoolsPlugin", "heads/main", token, ct) == upstreamMainSha)
		{
			return;
		}
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		var content = new StringContent(JsonSerializer.Serialize(new
		{
			sha = upstreamMainSha,
			force = true
		}), Encoding.UTF8, "application/json");
		await client.PatchAsync($"https://api.github.com/repos/{forkOwner}/{UpstreamRepo}/git/refs/heads/main", content, ct);
	}

	private static async Task<string?> GetLatestTreeShaAsync(CancellationToken ct)
	{
		using var client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(60)
		};
		client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
		return JsonDocument.Parse(await client.GetStringAsync("https://api.github.com/repos/luolangaga/tubatoolsPlugin/git/ref/heads/main", ct)).RootElement.GetProperty("object").GetProperty("sha").GetString();
	}

	private static async Task<string?> DownloadBlobAsync(string sha, CancellationToken ct)
	{
		using var client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(60)
		};
		client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Benchmark");
		if (JsonDocument.Parse(await client.GetStringAsync("https://api.github.com/repos/luolangaga/tubatoolsPlugin/git/blobs/" + sha, ct)).RootElement.TryGetProperty("content", out var content))
		{
			byte[] bytes = Convert.FromBase64String((content.GetString() ?? "").Replace("\n", "").Replace("\r", ""));
			return Encoding.UTF8.GetString(bytes);
		}
		return null;
	}

	private static async Task<string?> GetRefShaAsync(string owner, string repo, string refPath, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		try
		{
			return JsonDocument.Parse(await client.GetStringAsync($"https://api.github.com/repos/{owner}/{repo}/git/ref/{refPath}", ct)).RootElement.GetProperty("object").GetProperty("sha").GetString();
		}
		catch
		{
			return null;
		}
	}

	private static async Task<bool> CheckRefExistsAsync(string owner, string repo, string refPath, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		try
		{
			return (await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/git/ref/{refPath}", ct)).IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	private static async Task CreateRefAsync(string owner, string repo, string refName, string sha, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		var content = new StringContent(JsonSerializer.Serialize(new
		{
			@ref = refName,
			sha = sha
		}), Encoding.UTF8, "application/json");
		var resp = await client.PostAsync($"https://api.github.com/repos/{owner}/{repo}/git/refs", content, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string body = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"创建分支失败：{(int)resp.StatusCode}\n{body}");
		}
	}

	private static async Task CreateFileAsync(string owner, string repo, string path, string branch, string content, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		string base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
		var requestContent = new StringContent(JsonSerializer.Serialize(new
		{
			message = "benchmark: upload report - " + Path.GetFileNameWithoutExtension(path),
			content = base64Content,
			branch = branch
		}), Encoding.UTF8, "application/json");
		var resp = await client.PutAsync($"https://api.github.com/repos/{owner}/{repo}/contents/{path}", requestContent, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string body = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"上传报告失败：{(int)resp.StatusCode}\n{body}");
		}
	}

	private static async Task CreateBinaryFileAsync(string owner, string repo, string path, string branch, string localFilePath, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		byte[] bytes = await File.ReadAllBytesAsync(localFilePath, ct);
		string base64Content = Convert.ToBase64String(bytes);
		var requestContent = new StringContent(JsonSerializer.Serialize(new
		{
			message = "benchmark: upload latency image - " + Path.GetFileName(path),
			content = base64Content,
			branch = branch
		}), Encoding.UTF8, "application/json");
		var resp = await client.PutAsync($"https://api.github.com/repos/{owner}/{repo}/contents/{path}", requestContent, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string body = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"上传核间延迟热力图失败：{(int)resp.StatusCode}\n{body}");
		}
	}

	private static async Task<string> CreatePullRequestAsync(string branch, string forkOwner, BenchmarkReportEntry entry, string token, CancellationToken ct, string? latencyImageName = null)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		string body = $"## 性能测试报告上传\n\n- **CPU**：{entry.CpuName}\n- **GPU**：{entry.GpuName}\n- **游戏性能**：{entry.GamingScore} ({entry.GamingGrade})\n- **办公性能**：{entry.OfficeScore} ({entry.OfficeGrade})\n- **提交者**：@{entry.Author}\n";
		if (!string.IsNullOrEmpty(latencyImageName))
		{
			body += $"- **核间延迟热力图**：[{latencyImageName}]({LatencyImagesRawBase}/{latencyImageName})\n";
		}
		var content = new StringContent(JsonSerializer.Serialize(new
		{
			title = "[性能报告] " + entry.CpuName + " / " + entry.GpuName,
			head = forkOwner + ":" + branch,
			@base = "main",
			body = body
		}), Encoding.UTF8, "application/json");
		var resp = await client.PostAsync("https://api.github.com/repos/luolangaga/tubatoolsPlugin/pulls", content, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string respBody = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"创建 PR 失败：{(int)resp.StatusCode}\n{respBody}");
		}
		return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("html_url").GetString() ?? "";
	}

	private static async Task<string> CreateLatencyPullRequestAsync(string branch, string forkOwner, string cpuName, string author, string imgName, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		string body = $"## 核间延迟热力图上传\n\n- **CPU**：{cpuName}\n- **热力图**：[{imgName}]({LatencyImagesRawBase}/{imgName})\n- **提交者**：@{author}\n";
		var content = new StringContent(JsonSerializer.Serialize(new
		{
			title = "[核间延迟] " + cpuName,
			head = forkOwner + ":" + branch,
			@base = "main",
			body = body
		}), Encoding.UTF8, "application/json");
		var resp = await client.PostAsync("https://api.github.com/repos/luolangaga/tubatoolsPlugin/pulls", content, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string respBody = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"创建 PR 失败：{(int)resp.StatusCode}\n{respBody}");
		}
		return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("html_url").GetString() ?? "";
	}

	private static async Task DeleteFileAsync(string owner, string repo, string path, string branch, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		var getResp = await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}", ct);
		if (!getResp.IsSuccessStatusCode)
		{
			string body = await getResp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"获取文件信息失败：{(int)getResp.StatusCode}\n{body}");
		}
		string fileSha = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("sha").GetString() ?? "";
		var content = new StringContent(JsonSerializer.Serialize(new
		{
			message = "benchmark: delete report - " + Path.GetFileNameWithoutExtension(path),
			sha = fileSha,
			branch = branch
		}), Encoding.UTF8, "application/json");
		var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.github.com/repos/{owner}/{repo}/contents/{path}")
		{
			Content = content
		};
		var resp = await client.SendAsync(request, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string body2 = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"删除文件失败：{(int)resp.StatusCode}\n{body2}");
		}
	}

	private static async Task<string?> FindOpenPullRequestUrlAsync(string forkOwner, string branch, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		try
		{
			string url = $"https://api.github.com/repos/luolangaga/tubatoolsPlugin/pulls?state=open&head={Uri.EscapeDataString(forkOwner + ":" + branch)}&per_page=1";
			using var doc = JsonDocument.Parse(await client.GetStringAsync(url, ct));
			if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
			{
				return doc.RootElement[0].TryGetProperty("html_url", out var u) ? u.GetString() : null;
			}
		}
		catch
		{
		}
		return null;
	}

	private static async Task<string> CreateDeletePullRequestAsync(string branch, string forkOwner, BenchmarkReportEntry entry, string token, CancellationToken ct)
	{
		using var client = GitHubAuthService.CreateAuthenticatedClient();
		string body = $"## 删除性能测试报告\n\n- **报告ID**：{entry.Id}\n- **CPU**：{entry.CpuName}\n- **GPU**：{entry.GpuName}\n- **提交者**：@{entry.Author}\n";
		var content = new StringContent(JsonSerializer.Serialize(new
		{
			title = "[删除报告] " + entry.CpuName + " / " + entry.GpuName,
			head = forkOwner + ":" + branch,
			@base = "main",
			body = body
		}), Encoding.UTF8, "application/json");
		var resp = await client.PostAsync("https://api.github.com/repos/luolangaga/tubatoolsPlugin/pulls", content, ct);
		if (!resp.IsSuccessStatusCode)
		{
			string respBody = await resp.Content.ReadAsStringAsync(ct);
			throw new InvalidOperationException($"创建 PR 失败：{(int)resp.StatusCode}\n{respBody}");
		}
		return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("html_url").GetString() ?? "";
	}
}

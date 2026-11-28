using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Tubatools.MinecraftLauncher.Services;

internal sealed record McPreparedLaunch(
    string JavaExe,
    string Arguments,
    string WorkingDirectory,
    string NativesDirectory,
    string ClassPath,
    string GameDirectory,
    int JavaMajor,
    IReadOnlyList<string> Notes,
    string ScriptPath);

/// <summary>启动准备与进程启动。步骤与 PCL 一致：校验 → Natives → 参数 → 进程。</summary>
internal static class McGameLauncher
{
    public static async Task<McPreparedLaunch> PrepareAsync(
        McVersion version,
        McAccount account,
        JavaRuntime java,
        LauncherSettings settings,
        IProgress<string>? stage,
        Action<string> log,
        CancellationToken ct)
    {
        var notes = new List<string>();
        var minecraftDirectory = Path.GetFullPath(settings.GameDirectory);
        var gameDirectory = Path.GetFullPath(McVersionStore.ResolveGameDirectory(version, settings));
        ApplyGameLanguage(gameDirectory, settings.GameLanguage, log);

        stage?.Report("检查游戏文件");
        if (version.Error is { Length: > 0 })
            throw new InvalidOperationException($"版本无法启动：{version.Error}");
        if (!File.Exists(version.ClientJarPath))
            throw new FileNotFoundException($"缺少客户端 JAR：{version.ClientJarPath}\n请先在「下载」页安装该版本。");
        if (string.IsNullOrWhiteSpace(version.MainClass))
            throw new InvalidDataException("版本 JSON 缺少 mainClass。");

        var os = McOsInfo.Current;
        var libraries = McLibraryResolver.Resolve(version.Json, minecraftDirectory, os);
        var missingLibraries = McLibraryResolver.FindMissing(libraries);
        if (missingLibraries.Count > 0)
            throw new FileNotFoundException(
                "缺少支持库文件：\n" + string.Join("\n", missingLibraries) +
                "\n请重新安装该版本（版本继承的原版也需要完整安装）。");

        if (string.IsNullOrWhiteSpace(version.AssetIndexId))
            notes.Add("版本没有资源索引，游戏可能缺少声音或语言文件。");
        var assetIndexPath = version.AssetIndexId is null
            ? null
            : Path.Combine(minecraftDirectory, "assets", "indexes", version.AssetIndexId + ".json");
        if (assetIndexPath is not null && !File.Exists(assetIndexPath))
            throw new FileNotFoundException($"缺少资源索引：{assetIndexPath}\n请先下载该版本的资源文件。");
        await EnsureAssetObjectsAsync(assetIndexPath, minecraftDirectory, settings.Mirror, stage, log, ct);

        stage?.Report("解压 Natives");
        var nativesDirectory = GetNativesDirectory(version);
        var natives = libraries.Where(library => library.IsNatives).ToList();
        await Task.Run(() => ExtractNatives(natives, nativesDirectory, log, ct), ct);

        stage?.Report("构建启动参数");
        var classPath = BuildClassPath(libraries, Path.GetFullPath(version.ClientJarPath));
        var loggingConfig = version.Json.Obj("logging")?.Obj("client")?.Obj("file")?.Str("id");
        var loggingPath = loggingConfig is null
            ? null
            : Path.Combine(minecraftDirectory, "assets", "log_configs", loggingConfig);
        if (loggingPath is not null && !File.Exists(loggingPath)) loggingPath = null;

        var context = new McLaunchContext(
            gameDirectory,
            minecraftDirectory,
            Path.Combine(minecraftDirectory, "assets"),
            version.AssetIndexId ?? version.Assets ?? "legacy",
            version.Assets,
            version.Id,
            version.Type,
            account.Name,
            account.Uuid,
            account.AccessToken ?? account.Uuid,
            classPath,
            nativesDirectory,
            Path.Combine(minecraftDirectory, "libraries"),
            settings.WindowWidth,
            settings.WindowHeight,
            loggingPath,
            settings.GameArgs,
            []);

        var jvmArguments = McArguments.BuildJvm(version.Json, context, settings.MemoryMb, java.Major);
        var gameArguments = McArguments.BuildGame(version.Json, context, version.ReleaseTime);
        if (!string.IsNullOrWhiteSpace(settings.JvmArgs))
        {
            var custom = McArguments.SplitCommandLine(settings.JvmArgs);
            jvmArguments.InsertRange(0, custom);
        }

        var allArguments = McArguments.ApplyPlaceholders(jvmArguments.Concat(gameArguments), context);
        if (!string.IsNullOrWhiteSpace(context.Server)) notes.Add($"启动后进入服务器：{context.Server}");

        var arguments = McCommandLine.Join(allArguments);
        var scriptPath = WriteLaunchScript(settings.DataDirectory, java.JavaExe, arguments, gameDirectory, version.Id);

        log($"[启动] 版本：{version.Id}（{version.VersionLabel}）");
        log($"[启动] Java：{java.Detail}");
        log($"[启动] 游戏目录：{gameDirectory}");
        log($"[启动] Natives：{nativesDirectory}");
        log($"[启动] 分配内存：{settings.MemoryMb} MB");
        foreach (var note in notes) log("[提示] " + note);

        return new McPreparedLaunch(java.JavaExe, arguments, gameDirectory, nativesDirectory,
            classPath, gameDirectory, java.Major, notes, scriptPath);
    }

    public static void ApplyGameLanguage(string gameDirectory, string? configured, Action<string>? log = null)
    {
        var language = (configured ?? "auto").Trim().ToLowerInvariant();
        if (language == "auto")
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            language = culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? (culture.Name.Contains("TW", StringComparison.OrdinalIgnoreCase) ||
                   culture.Name.Contains("HK", StringComparison.OrdinalIgnoreCase)
                    ? "zh_tw"
                    : "zh_cn")
                : "en_us";
        }

        if (language is not ("zh_cn" or "zh_tw" or "en_us")) language = "en_us";
        try
        {
            Directory.CreateDirectory(gameDirectory);
            var path = Path.Combine(gameDirectory, "options.txt");
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
            var index = lines.FindIndex(line => line.StartsWith("lang:", StringComparison.OrdinalIgnoreCase));
            var line = "lang:" + language;
            if (index >= 0) lines[index] = line;
            else lines.Add(line);
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(false));
            log?.Invoke($"[启动] 游戏语言：{language}");
        }
        catch (Exception ex)
        {
            log?.Invoke("[启动] 无法写入游戏语言设置：" + ex.Message);
        }
    }

    private static async Task EnsureAssetObjectsAsync(string? assetIndexPath, string minecraftDirectory,
        string mirror, IProgress<string>? stage, Action<string> log, CancellationToken ct)
    {
        if (assetIndexPath is null) return;
        JsonObject index;
        try { index = McJson.ParseObject(await File.ReadAllTextAsync(assetIndexPath, ct)); }
        catch (Exception ex) { throw new InvalidDataException("资源索引损坏，请重新安装该版本。", ex); }

        var assets = McInstallPlanner.PlanAssets(index, minecraftDirectory, mirror);
        var missing = assets.Where(asset => !File.Exists(asset.TargetPath)).ToList();
        if (missing.Count == 0) return;

        stage?.Report($"补齐资源文件（{missing.Count}）");
        log($"[资源] 检测到 {missing.Count} 个资源文件缺失，正在自动补齐…");
        var progress = new Progress<McDownloadProgress>(value =>
        {
            if (value.Done == value.Total || value.Done % 50 == 0)
                stage?.Report($"补齐资源文件 {value.Done}/{value.Total}");
        });
        var result = await McDownloader.DownloadAsync(McHttp.Client, missing, 24, progress, log, ct);
        if (result.Failures.Count > 0)
            throw new IOException($"仍有 {result.Failures.Count} 个资源文件下载失败，请检查网络后重试。\n" +
                                  result.Failures[0].Error);
    }

    public static Process Start(McPreparedLaunch plan, Action<string> onLine)
    {
        var startInfo = new ProcessStartInfo(plan.JavaExe)
        {
            Arguments = plan.Arguments,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = plan.JavaMajor > 8 ? Encoding.UTF8 : Encoding.Default,
            StandardErrorEncoding = plan.JavaMajor > 8 ? Encoding.UTF8 : Encoding.Default
        };
        startInfo.Environment["appdata"] = plan.GameDirectory;
        var path = startInfo.Environment.TryGetValue("Path", out var existing) ? existing : "";
        var javaBin = Path.GetDirectoryName(plan.JavaExe) ?? "";
        startInfo.Environment["Path"] = string.Join(';',
            new[] { javaBin, path }.Where(part => !string.IsNullOrWhiteSpace(part)));

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) onLine(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) onLine(args.Data); };
        if (!process.Start()) throw new InvalidOperationException("无法启动 Java 进程。");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public static string BuildClassPath(IReadOnlyList<McLibrary> libraries, string clientJar)
    {
        var paths = libraries
            .Where(library => !library.IsNatives && File.Exists(library.LocalPath))
            .Select(library => library.LocalPath)
            .ToList();
        paths.Add(clientJar);
        return string.Join(';', paths);
    }

    public static string GetNativesDirectory(McVersion version)
    {
        var preferred = Path.Combine(version.Directory, version.Id + "-natives");
        if (IsAscii(preferred)) return preferred;
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var fallback = Path.Combine(programData, "TubaWinUi3", "Minecraft", "natives",
            HashFolderName(version.Id));
        return IsAscii(fallback) ? fallback : Path.Combine(Path.GetTempPath(), "tuba-minecraft-natives", HashFolderName(version.Id));
    }

    private static void ExtractNatives(IReadOnlyList<McLibrary> natives, string target, Action<string> log,
        CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in natives)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(library.LocalPath))
            {
                log($"[提示] Natives 缺失：{library.Name}");
                continue;
            }

            try
            {
                using var archive = ZipFile.OpenRead(library.LocalPath);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
                    var extension = Path.GetExtension(entry.Name);
                    if (!extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".so", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var filePath = Path.Combine(target, entry.Name);
                    expected.Add(filePath);
                    var existing = new FileInfo(filePath);
                    if (existing.Exists && existing.Length == entry.Length) continue;
                    if (existing.Exists)
                    {
                        try { existing.Delete(); }
                        catch (UnauthorizedAccessException)
                        {
                            log("[提示] Natives 文件被占用，跳过解压（可能有一个游戏正在运行）。");
                            continue;
                        }
                    }

                    entry.ExtractToFile(filePath, true);
                }
            }
            catch (InvalidDataException)
            {
                log($"[错误] Natives 压缩包损坏：{library.LocalPath}");
            }
        }

        foreach (var file in Directory.GetFiles(target))
        {
            if (expected.Contains(file)) continue;
            try { File.Delete(file); }
            catch { }
        }
    }

    private static string WriteLaunchScript(string dataDirectory, string javaExe, string arguments,
        string gameDirectory, string versionName)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            var path = Path.Combine(dataDirectory, "last-launch.bat");
            var text = "@echo off\r\nchcp 65001>nul\r\n" +
                       $"title 启动 - {versionName}\r\n" +
                       $"cd /D \"{gameDirectory}\"\r\n" +
                       $"\"{javaExe}\" {arguments}\r\n" +
                       "echo 游戏已退出。\r\npause\r\n";
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }
        catch
        {
            return "";
        }
    }

    public static bool IsAscii(string value) => value.All(c => c <= 0x7F);

    private static string HashFolderName(string value)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}

/// <summary>启动日志：同时写入内存列表与插件数据目录。</summary>
internal sealed class McLaunchLog
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];
    private readonly string? _file;
    private readonly StreamWriter? _writer;

    public McLaunchLog(string dataDirectory)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(dataDirectory, "logs"));
            _file = Path.Combine(dataDirectory, "logs", $"launch-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _writer = new StreamWriter(_file, false, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch
        {
            _writer = null;
        }
    }

    public string? FilePath => _file;

    public IReadOnlyList<string> Snapshot
    {
        get { lock (_gate) return _lines.ToArray(); }
    }

    public void Write(string line)
    {
        var text = $"[{DateTime.Now:HH:mm:ss}] {line}";
        lock (_gate)
        {
            _lines.Add(text);
            if (_lines.Count > 4000) _lines.RemoveRange(0, _lines.Count - 4000);
            try { _writer?.WriteLine(text); } catch { }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _writer?.Dispose(); } catch { }
        }
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text;

namespace Tubatools.MacOSInstaller.Core;

internal sealed class RunResult
{
    public int ExitCode { get; set; }
    public bool Canceled { get; set; }
    public bool TimedOut { get; set; }
    public string Output { get; set; } = "";
    public string StdErr { get; set; } = "";
    public string LogPath { get; set; } = "";
    public bool Success => !Canceled && !TimedOut && ExitCode == 0;
}

/// <summary>
/// 所有外部命令的唯一入口。非管理员时用 UAC 提升包装脚本，输出写文件后实时回读。
/// </summary>
internal sealed class ProcessRunner
{
    private readonly string _logDirectory;
    private readonly object _gate = new();
    private readonly List<Process> _running = [];

    public ProcessRunner(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);
    }

    public bool IsAdministrator { get; } = CheckAdministrator();

    private static bool CheckAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public async Task<RunResult> RunPowerShellScriptAsync(
        string script,
        string phase,
        bool requireAdmin,
        bool killOnCancel,
        Action<string>? onLine,
        CancellationToken ct)
    {
        string dir = Path.Combine(Path.GetTempPath(), "TubatoolsMacOS", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "run.ps1");
        await File.WriteAllTextAsync(path, script, new UTF8Encoding(true), ct);
        return await RunAsync(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path],
            null, phase, requireAdmin, killOnCancel, onLine, ct);
    }

    public async Task<RunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string phase,
        bool requireAdmin,
        bool killOnCancel,
        Action<string>? onLine,
        CancellationToken ct)
    {
        if (requireAdmin && !IsAdministrator)
            return await RunElevatedAsync(fileName, arguments, workingDirectory, phase, killOnCancel, onLine, ct);

        string logPath = NewLogPath(phase);
        var output = new StringBuilder();
        var error = new StringBuilder();
        using var logWriter = new StreamWriter(logPath, append: false, new UTF8Encoding(false));
        logWriter.AutoFlush = true;

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string a in arguments) psi.ArgumentList.Add(a);
        if (!string.IsNullOrWhiteSpace(workingDirectory)) psi.WorkingDirectory = workingDirectory;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            logWriter.WriteLine(ex.ToString());
            return new RunResult { ExitCode = -1, Output = "", StdErr = ex.Message, LogPath = logPath };
        }

        lock (_gate) _running.Add(process);
        bool canceled = false;
        try
        {
            Task pumpOut = PumpAsync(process.StandardOutput, "  ", output, logWriter, onLine);
            Task pumpErr = PumpAsync(process.StandardError, "! ", error, logWriter, onLine);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
                if (killOnCancel)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                }
                else
                {
                    // 不允许中途杀进程的阶段（例如 diskpart）必须等它自己结束，
                    // 否则返回时进程仍在写磁盘，后续操作会与它竞争。
                    try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                }
            }
            await Task.WhenAll(pumpOut, pumpErr);
        }
        finally
        {
            lock (_gate) _running.Remove(process);
        }

        int exitCode = canceled ? -2 : SafeExitCode(process);
        logWriter.WriteLine($"\n[exit] {exitCode}{(canceled ? " (cancelled)" : "")}");
        return new RunResult
        {
            ExitCode = exitCode,
            Canceled = canceled,
            Output = output.ToString(),
            StdErr = error.ToString(),
            LogPath = logPath
        };
    }

    private async Task<RunResult> RunElevatedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string phase,
        bool killOnCancel,
        Action<string>? onLine,
        CancellationToken ct)
    {
        string dir = Path.Combine(Path.GetTempPath(), "TubatoolsMacOS", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string outFile = Path.Combine(dir, "out.txt");
        string errFile = Path.Combine(dir, "err.txt");
        string exitFile = Path.Combine(dir, "exit.txt");
        string scriptPath = Path.Combine(dir, "elevated.ps1");

        string argArray = string.Join(", ", arguments.Select(a => "'" + EscapeSingle(QuoteArgument(a)) + "'"));
        string work = workingDirectory ?? "";
        string workClause = string.IsNullOrWhiteSpace(work)
            ? ""
            : $" -WorkingDirectory '{EscapeSingle(work)}'";
        string script = $"""
            $ErrorActionPreference = 'Continue'
            [Console]::OutputEncoding = [Text.Encoding]::UTF8
            chcp 65001 > $null
            $out = '{EscapeSingle(outFile)}'
            $err = '{EscapeSingle(errFile)}'
            $exitFile = '{EscapeSingle(exitFile)}'
            $p = Start-Process -FilePath '{EscapeSingle(fileName)}' -ArgumentList @({argArray}){workClause} -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
            $p.WaitForExit()
            [IO.File]::WriteAllText($exitFile, [string]$p.ExitCode)
            """;
        await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(true), ct);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process shell;
        try
        {
            shell = Process.Start(psi) ?? throw new InvalidOperationException("无法启动提权进程。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new RunResult { ExitCode = -1223, StdErr = "用户取消了管理员授权。", LogPath = "" };
        }
        catch (Exception ex)
        {
            return new RunResult { ExitCode = -1, StdErr = ex.Message, LogPath = "" };
        }

        string logPath = NewLogPath(phase);
        var output = new StringBuilder();
        var error = new StringBuilder();
        using var logWriter = new StreamWriter(logPath, append: false, new UTF8Encoding(false));
        logWriter.AutoFlush = true;

        long outPos = 0, errPos = 0;
        bool canceled = false;
        int exitCode = -1;
        try
        {
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    canceled = true;
                    if (killOnCancel)
                    {
                        try { shell.Kill(entireProcessTree: true); } catch { }
                    }
                    break;
                }
                ReadNewLines(outFile, ref outPos, output, logWriter, onLine, "  ");
                ReadNewLines(errFile, ref errPos, error, logWriter, onLine, "! ");
                if (File.Exists(exitFile)) break;
                if (shell.HasExited && !File.Exists(exitFile))
                {
                    await Task.Delay(300, CancellationToken.None);
                    ReadNewLines(outFile, ref outPos, output, logWriter, onLine, "  ");
                    ReadNewLines(errFile, ref errPos, error, logWriter, onLine, "! ");
                    if (File.Exists(exitFile)) break;
                    exitCode = -1;
                    break;
                }
                await Task.Delay(250, CancellationToken.None);
            }

            if (File.Exists(exitFile))
            {
                try { exitCode = int.Parse(await File.ReadAllTextAsync(exitFile, CancellationToken.None)); }
                catch { exitCode = -1; }
            }
        }
        finally
        {
            try { if (!shell.HasExited) shell.WaitForExit(2000); } catch { }
        }

        ReadNewLines(outFile, ref outPos, output, logWriter, onLine, "  ");
        ReadNewLines(errFile, ref errPos, error, logWriter, onLine, "! ");
        logWriter.WriteLine($"\n[exit] {exitCode}{(canceled ? " (cancelled)" : "")}");
        return new RunResult
        {
            ExitCode = canceled ? -2 : exitCode,
            Canceled = canceled,
            Output = output.ToString(),
            StdErr = error.ToString(),
            LogPath = logPath
        };
    }

    private static async Task PumpAsync(
        StreamReader reader,
        string prefix,
        StringBuilder sink,
        StreamWriter log,
        Action<string>? onLine)
    {
        try
        {
            while (true)
            {
                string? line = await reader.ReadLineAsync();
                if (line is null) break;
                lock (sink) sink.AppendLine(line);
                string shown = prefix + line;
                log.WriteLine(shown);
                try { onLine?.Invoke(shown); } catch { }
            }
        }
        catch
        {
            // 进程被终止时流会关闭，正常结束。
        }
    }

    private static void ReadNewLines(string path, ref long position, StringBuilder sink, StreamWriter log, Action<string>? onLine, string prefix)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length <= position) return;
            fs.Position = position;
            using var reader = new StreamReader(fs, Encoding.UTF8);
            while (!reader.EndOfStream)
            {
                string? line = reader.ReadLine();
                if (line is null) break;
                lock (sink) sink.AppendLine(line);
                string shown = prefix + line;
                log.WriteLine(shown);
                try { onLine?.Invoke(shown); } catch { }
            }
            position = fs.Position;
        }
        catch
        {
            // 文件正被提权进程写入，下一轮再读。
        }
    }

    private string NewLogPath(string phase)
    {
        Directory.CreateDirectory(_logDirectory);
        string safe = MakeSafe(phase);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(_logDirectory, $"{stamp}-{safe}.log");
    }

    private static string MakeSafe(string value)
    {
        var sb = new StringBuilder();
        foreach (char c in value)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        return sb.ToString();
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }

    private static string EscapeSingle(string value) => value.Replace("'", "''");

    private static string QuoteArgument(string value)
    {
        if (value.Length == 0) return "\"\"";
        bool needQuote = value.Any(c => char.IsWhiteSpace(c) || c == '"');
        if (!needQuote) return value;
        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                backslashes++;
            }
            else if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
                backslashes = 0;
            }
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }
}

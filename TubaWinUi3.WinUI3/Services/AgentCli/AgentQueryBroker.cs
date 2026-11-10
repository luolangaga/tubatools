using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.AgentCli;

/// <summary>Same-user, read-only elevation bridge. Never accepts paths, arguments or arbitrary executables.</summary>
internal static class AgentQueryBroker
{
    internal const string WorkerSwitch = "--agent-query-broker";
    [DllImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WaitNamedPipe(string name, uint timeout);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetSecurityDescriptorSacl(IntPtr descriptor, out bool present, out IntPtr sacl, out bool defaulted);
    [DllImport("advapi32.dll")]
    private static extern uint SetSecurityInfo(Microsoft.Win32.SafeHandles.SafePipeHandle handle, int objectType, uint information,
        IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    internal static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    internal static bool IsReadOnlyCommand(string command) => command == "hardware";
    private static string PipeName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var key = identity.User?.Value + "|" + AppContext.BaseDirectory.ToUpperInvariant();
            return "Tuba.AgentQuery." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
        }
    }
    private static Task? _server;
    internal static NamedPipeServerStream CreateServer(string pipeName)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new UnauthorizedAccessException("无法识别当前 Windows 用户。");
        // CurrentUserOnly uses token.Owner (Administrators for an elevated token), so it cannot
        // bridge split UAC tokens. Use the actual user SID and a medium integrity label instead.
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm($"O:{sid.Value}D:P(D;;GA;;;NU)(A;;GA;;;{sid.Value})");
        var pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security,
            additionalAccessRights: PipeAccessRights.TakeOwnership);
        IntPtr descriptor = IntPtr.Zero;
        try
        {
            // Apply only LABEL_SECURITY_INFORMATION. Supplying a SACL at Create requires
            // SeSecurityPrivilege, even for an elevated administrator, and would prevent startup.
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;;NW;;;ME)", 1, out descriptor, out _)
                || !GetSecurityDescriptorSacl(descriptor, out _, out var sacl, out _))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            uint error = SetSecurityInfo(pipe.SafePipeHandle, 6, 0x10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, sacl);
            if (error != 0) throw new System.ComponentModel.Win32Exception((int)error);
            return pipe;
        }
        catch { pipe.Dispose(); throw; }
        finally { if (descriptor != IntPtr.Zero) LocalFree(descriptor); }
    }
    internal static void StartForMainInstance()
    {
        if (IsAdmin()) _server ??= Task.Run(() => ServeAsync(standalone: false));
    }

    internal static async Task ServeAsync(bool standalone)
    {
        if (!IsAdmin()) return;
        AppSettings.Load();
        try
        {
            while (true)
            {
                using var pipe = CreateServer(PipeName);
                using var idle = new CancellationTokenSource(standalone ? TimeSpan.FromMinutes(5) : Timeout.InfiniteTimeSpan);
                try { await pipe.WaitForConnectionAsync(idle.Token); }
                catch (OperationCanceledException) { return; }
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                try
                {
                    // The single hardware query, read into a bounded buffer. No caller-supplied output file is opened as admin.
                    var chars = new char[16];
                    int length = 0;
                    while (length < chars.Length)
                    {
                        int count = await reader.ReadAsync(chars.AsMemory(length, 1), deadline.Token);
                        if (count == 0 || chars[length] == '\n') break;
                        length++;
                    }
                    var command = new string(chars, 0, length).TrimEnd('\r');
                    if (!IsReadOnlyCommand(command)) throw new ArgumentException("管理员查询通道只支持 hardware。");
                    var data = await AgentCliService.QueryLocalAsync(command, disposeSensors: false).WaitAsync(deadline.Token);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { ok = true, data }, AgentCliService.Json).AsMemory(), deadline.Token);
                }
                catch (Exception ex)
                {
                    try { await writer.WriteLineAsync(JsonSerializer.Serialize(new { ok = false, error = ex.Message }).AsMemory(), deadline.Token); }
                    catch (Exception) { /* Disconnected clients must not terminate the main app. */ }
                }
            }
        }
        catch (IOException) { /* Another elevated instance already owns this user's query endpoint. */ }
    }

    internal static async Task<JsonElement> QueryAsync(string command)
    {
        if (!IsReadOnlyCommand(command)) throw new ArgumentException("不支持的管理员查询。");
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        try { await pipe.ConnectAsync(250); }
        catch (TimeoutException)
        {
            // Existing endpoint busy with another report: queue instead of displaying another UAC prompt.
            bool available = WaitNamedPipe(@"\\.\pipe\" + PipeName, 1);
            int pipeError = Marshal.GetLastWin32Error();
            if (available || pipeError == 121)
            {
                await pipe.ConnectAsync(120_000);
            }
            else
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                start.ArgumentList.Add(WorkerSwitch);
                try { using var process = Process.Start(start); }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                { throw new UnauthorizedAccessException("已取消 Windows 管理员授权。无需重启 Agent；重新调用查询并在系统窗口允许，或先打开图吧工具箱。", ex); }
                await pipe.ConnectAsync(30_000);
            }
        }
        using (var identity = WindowsIdentity.GetCurrent())
            if (!Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)), identity.User))
                throw new UnauthorizedAccessException("管理员查询通道不属于当前用户，已拒绝连接。");
        return await ExchangeAsync(pipe, command);
    }

    internal static async Task<JsonElement> ExchangeAsync(NamedPipeClientStream pipe, string command)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        try
        {
        await writer.WriteLineAsync(command.AsMemory(), deadline.Token);
        // Serialized reports may include line breaks. The server closes the connection after each result.
        var json = await reader.ReadToEndAsync(deadline.Token);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.GetProperty("ok").GetBoolean()) throw new InvalidOperationException(root.GetProperty("error").GetString());
        return root.GetProperty("data").Clone();
        }
        finally
        {
            // The complete response ends at server EOF. StreamWriter.Dispose flushes even an
            // empty buffer, which can throw after that EOF and mask an otherwise valid result.
            try { writer.Dispose(); } catch (IOException) { }
        }
    }
}

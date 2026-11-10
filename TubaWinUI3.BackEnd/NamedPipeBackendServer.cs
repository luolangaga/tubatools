using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace TubaWinUI3.BackEnd;

/// <summary>
/// 命名管道后端服务器（移植自 ContextMenuMgr 的 NamedPipeBackendServer，GPL-3.0）。
/// 机制完全一致：每连接一个 NamedPipeServerStream，逐行 JSON（UTF-8 无 BOM）信封协议，
/// 请求-响应按 CorrelationId 关联，通知推送给已订阅连接，写锁保证同流写入不交错。
/// 业务分发由 <see cref="InterceptRequestHandler"/> 承担。
/// </summary>
public sealed class NamedPipeBackendServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly BackEndJsonContext JsonContext = new(JsonOptions);

    private readonly Func<InterceptPipeRequest, CancellationToken, Task<InterceptPipeResponse>> _dispatch;
    private readonly ConcurrentDictionary<Guid, PipeClientConnection> _clients = new();
    private CancellationTokenSource? _acceptLoopCts;
    private Task? _acceptLoopTask;

    public NamedPipeBackendServer(Func<InterceptPipeRequest, CancellationToken, Task<InterceptPipeResponse>> dispatch)
    {
        _dispatch = dispatch;
    }

    public int ConnectedClientCount => _clients.Count;

    public void Start(CancellationToken cancellationToken)
    {
        // Fail closed if an unprivileged process has already claimed the well-known name.
        var first = CreateServerStream(PipeOptions.FirstPipeInstance);
        _acceptLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(first, _acceptLoopCts.Token));
    }

    public void Stop()
    {
        try
        {
            _acceptLoopCts?.Cancel();
            foreach (var client in _clients.Values)
            {
                try { client.Dispose(); } catch { }
            }
            _clients.Clear();
        }
        catch { }
    }

    /// <summary>向所有已订阅连接广播通知（单个连接失败只移除该连接）。</summary>
    public void BroadcastNotification(InterceptBackendNotification notification)
    {
        var envelope = new InterceptPipeEnvelope
        {
            MessageType = InterceptPipeMessageType.Notification,
            Notification = notification,
        };

        var subscribers = _clients.Values.Where(c => c.IsNotificationSubscriber).ToList();
        foreach (var connection in subscribers)
        {
            try
            {
                _ = connection.SendAsync(envelope, CancellationToken.None);
            }
            catch
            {
                _clients.TryRemove(connection.Id, out _);
                try { connection.Dispose(); } catch { }
            }
        }
    }

    public void BroadcastServiceStopping()
    {
        BroadcastNotification(new InterceptBackendNotification
        {
            Kind = InterceptPipeNotificationKind.ServiceStopping,
            Message = "后端服务正在退出。",
            Timestamp = DateTimeOffset.UtcNow,
        });
    }

    // ---------- 连接生命周期 ----------

    private async Task AcceptLoopAsync(NamedPipeServerStream first, CancellationToken cancellationToken)
    {
        NamedPipeServerStream? pending = first;
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = pending ?? CreateServerStream();
                pending = null;
                await server.WaitForConnectionAsync(cancellationToken);
                server.ReadMode = PipeTransmissionMode.Byte;
                _ = ObserveClientTaskAsync(server, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                BackEndLog.Error($"管道接受循环异常：{ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
                catch { break; }
            }
        }
        pending?.Dispose();
    }

    private async Task ObserveClientTaskAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        try
        {
            await HandleClientAsync(stream, cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch { }
    }

    internal static PipeSecurity CreatePipeSecurity()
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        pipeSecurity.SetAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        // Management commands are restricted to elevated administrators and SYSTEM.
        // The account SID alone is insufficient: its medium-integrity processes must not inherit control.
        pipeSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        return pipeSecurity;
    }

    private static NamedPipeServerStream CreateServerStream(PipeOptions options = PipeOptions.None)
    {

        return NamedPipeServerStreamAcl.Create(
            InterceptPipeConstants.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | options,
            0,
            0,
            CreatePipeSecurity());
    }

    internal static bool IsAuthorizedIdentity(WindowsIdentity identity) =>
        identity.IsSystem || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

    private static bool IsAuthorizedClient(NamedPipeServerStream stream)
    {
        bool authorized = false;
        try { stream.RunAsClient(() => { using var identity = WindowsIdentity.GetCurrent(); authorized = IsAuthorizedIdentity(identity); }); }
        catch { return false; }
        return authorized;
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        var connection = new PipeClientConnection(stream);
        _clients[connection.Id] = connection;
        BackEndLog.Info($"管道客户端接入：{connection.Id}");

        try
        {
            while (stream.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var line = await connection.Reader.ReadLineAsync().WaitAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                InterceptPipeEnvelope? envelope;
                try
                {
                    envelope = JsonSerializer.Deserialize(line, JsonContext.InterceptPipeEnvelope);
                }
                catch (JsonException)
                {
                    BackEndLog.Warn($"管道载荷不是合法 JSON，忽略：{Truncate(line, 200)}");
                    continue;
                }

                if (envelope?.MessageType != InterceptPipeMessageType.Request || envelope.Request is null)
                {
                    BackEndLog.Warn($"管道载荷不是请求，忽略：{connection.Id}");
                    continue;
                }

                if (envelope.Request.Command == InterceptPipeCommand.SubscribeNotifications)
                {
                    connection.IsNotificationSubscriber = true;
                }

                if (!IsAuthorizedClient(stream))
                    break;

                InterceptPipeResponse response;
                try
                {
                    // 处理器允许独立失败：管道保持存活，调用方收到结构化错误响应
                    response = await _dispatch(envelope.Request, cancellationToken);
                }
                catch (Exception ex)
                {
                    BackEndLog.Error($"管道请求异常：{ex.Message}");
                    response = new InterceptPipeResponse
                    {
                        Success = false,
                        Message = ex.Message,
                    };
                }

                await connection.SendAsync(
                    new InterceptPipeEnvelope
                    {
                        MessageType = InterceptPipeMessageType.Response,
                        CorrelationId = envelope.CorrelationId,
                        Response = response,
                    },
                    cancellationToken);

                // 成功的状态变更请求向所有订阅者广播（包含发起者，发起者按 ClientOperationId 去重）
                if (response.Success && response.Item is not null)
                {
                    BroadcastNotification(new InterceptBackendNotification
                    {
                        Kind = InterceptPipeNotificationKind.ItemStateChanged,
                        Item = response.Item,
                        Message = response.Message,
                        ClientOperationId = response.ClientOperationId,
                        Timestamp = DateTimeOffset.UtcNow,
                    });
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            BackEndLog.Warn($"管道客户端异常：{connection.Id} {ex.Message}");
        }
        finally
        {
            _clients.TryRemove(connection.Id, out _);
            connection.Dispose();
            BackEndLog.Info($"管道客户端断开：{connection.Id}");
        }
    }

    private static string Truncate(string text, int length)
        => text.Length <= length ? text : text[..length] + "…";

    private sealed class PipeClientConnection : IDisposable
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly NamedPipeServerStream _stream;

        public PipeClientConnection(NamedPipeServerStream stream)
        {
            Id = Guid.NewGuid();
            _stream = stream;
            Reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            Writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = true,
            };
        }

        public Guid Id { get; }

        public StreamReader Reader { get; }

        public StreamWriter Writer { get; }

        public bool IsNotificationSubscriber { get; set; }

        public async Task SendAsync(InterceptPipeEnvelope envelope, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(envelope, JsonContext.InterceptPipeEnvelope);
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await Writer.WriteLineAsync(payload).WaitAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            try { Reader.Dispose(); } catch { }
            try { Writer.Dispose(); } catch { }
            try { _stream.Dispose(); } catch { }
            _writeLock.Dispose();
        }
    }
}

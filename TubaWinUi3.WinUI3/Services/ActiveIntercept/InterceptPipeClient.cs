using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.ActiveIntercept;

/// <summary>
/// 命名管道后端客户端（移植自 ContextMenuMgr 的 NamedPipeBackendClient）。
/// 与后端线上协议一致：UTF-8 无 BOM、每行一条 JSON（camelCase）。
/// 请求采用"按次连接"模型（每次调用新建连接、2 秒超时、按 CorrelationId 配对响应），
/// 通知走独立常开订阅连接（断线 1 秒自动重连）。
/// </summary>
public sealed class InterceptPipeClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _notificationLoopCts;
    private Task? _notificationLoopTask;
    private volatile bool _disposed;
    private volatile bool _isConnected;

    /// <summary>后端管道是否已连通（通知订阅连接在线）。</summary>
    public bool IsConnected => _isConnected;

    /// <summary>收到后端推送通知（任意线程回调；UI 侧自行编组到 DispatcherQueue）。</summary>
    public event EventHandler<InterceptBackendNotification>? NotificationReceived;

    public async Task ConnectNotificationsAsync()
    {
        if (_disposed) return;

        lock (_sync)
        {
            if (_notificationLoopCts is not null) return;
            _notificationLoopCts = new CancellationTokenSource();
            _notificationLoopTask = Task.Run(() => NotificationLoopAsync(_notificationLoopCts.Token));
        }
    }

    public bool IsNotificationSubscribed
    {
        get
        {
            lock (_sync) return _notificationLoopCts is not null;
        }
    }

    // ================= 命令 =================

    public async Task PingAsync(CancellationToken cancellationToken = default)
        => await SendRequestCoreAsync(new InterceptPipeRequest { Command = InterceptPipeCommand.Ping }, cancellationToken);

    public async Task<InterceptSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestCoreAsync(
            new InterceptPipeRequest { Command = InterceptPipeCommand.GetSnapshot }, cancellationToken);
        return response.Snapshot;
    }

    /// <summary>审核决策。返回更新后的条目（失败时抛 BackendRequestException）。</summary>
    public async Task<InterceptItemDto?> ApplyDecisionAsync(
        string itemId, InterceptDecision decision, bool trust, Guid clientOperationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestCoreAsync(new InterceptPipeRequest
        {
            Command = InterceptPipeCommand.ApplyDecision,
            ItemId = itemId,
            Decision = decision,
            Trust = trust,
            ClientOperationId = clientOperationId,
        }, cancellationToken);
        return response.Item;
    }

    public async Task<InterceptItemDto?> DeleteItemAsync(string itemId, Guid clientOperationId, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestCoreAsync(new InterceptPipeRequest
        {
            Command = InterceptPipeCommand.DeleteItem,
            ItemId = itemId,
            ClientOperationId = clientOperationId,
        }, cancellationToken);
        return response.Item;
    }

    public async Task<InterceptItemDto?> UndoDeleteAsync(string itemId, Guid clientOperationId, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestCoreAsync(new InterceptPipeRequest
        {
            Command = InterceptPipeCommand.UndoDelete,
            ItemId = itemId,
            ClientOperationId = clientOperationId,
        }, cancellationToken);
        return response.Item;
    }

    public async Task PurgeDeletedItemAsync(string itemId, CancellationToken cancellationToken = default)
        => await SendRequestCoreAsync(new InterceptPipeRequest
        {
            Command = InterceptPipeCommand.PurgeDeletedItem,
            ItemId = itemId,
        }, cancellationToken);

    public async Task StopTrackingAsync(string itemId, CancellationToken cancellationToken = default)
        => await SendRequestCoreAsync(new InterceptPipeRequest
        {
            Command = InterceptPipeCommand.StopTracking,
            ItemId = itemId,
        }, cancellationToken);

    public async Task ResumeTrackingAsync(string itemId, CancellationToken cancellationToken = default)
        => await SendRequestCoreAsync(new InterceptPipeRequest
        {
            Command = InterceptPipeCommand.ResumeTracking,
            ItemId = itemId,
        }, cancellationToken);

    public async Task RemoveEventRowsAsync(IEnumerable<string> rowIds, CancellationToken cancellationToken = default)
        => await SendRequestCoreAsync(new InterceptPipeRequest
        {
            Command = InterceptPipeCommand.RemoveEventRows,
            RowIds = rowIds?.ToList() ?? [],
        }, cancellationToken);

    public async Task ClearEventsAsync(CancellationToken cancellationToken = default)
        => await SendRequestCoreAsync(new InterceptPipeRequest
        {
            Command = InterceptPipeCommand.ClearEvents,
        }, cancellationToken);

    public async Task SetTrustPolicyAsync(string exePath, string policy, CancellationToken cancellationToken = default)
        => await SendRequestCoreAsync(new InterceptPipeRequest
        {
            Command = InterceptPipeCommand.SetTrustPolicy,
            ExePath = exePath,
            Policy = policy,
        }, cancellationToken);

    public async Task RequestShutdownAsync(CancellationToken cancellationToken = default)
        => await SendRequestCoreAsync(new InterceptPipeRequest
        {
            Command = InterceptPipeCommand.RequestShutdown,
        }, cancellationToken);

    // ================= 请求/响应核心 =================

    /// <summary>按次连接：发送请求，读取直到匹配该 CorrelationId 的响应。</summary>
    private async Task<InterceptPipeResponse> SendRequestCoreAsync(InterceptPipeRequest request, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new InvalidOperationException("管道客户端已关闭。");
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            using var stream = new NamedPipeClientStream(".", InterceptPipeConstants.PipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
            await stream.ConnectAsync(2000, cancellationToken);
            stream.ReadMode = PipeTransmissionMode.Byte;

            using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = true,
            };

            var correlationId = Guid.NewGuid();
            var envelope = new InterceptPipeEnvelope
            {
                MessageType = InterceptPipeMessageType.Request,
                CorrelationId = correlationId,
                Request = request,
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions)).WaitAsync(cancellationToken);

            while (true)
            {
                var line = await reader.ReadLineAsync().WaitAsync(cancellationToken);
                if (line is null)
                {
                    throw new InvalidOperationException("后端管道在返回响应前关闭。");
                }

                InterceptPipeEnvelope? responseEnvelope;
                try
                {
                    responseEnvelope = JsonSerializer.Deserialize<InterceptPipeEnvelope>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (responseEnvelope is null) continue;

                // 请求通道上顺带到达的通知：非本操作触发的才对外广播（按 ClientOperationId 去重）
                if (responseEnvelope.MessageType == InterceptPipeMessageType.Notification
                    && responseEnvelope.Notification is not null)
                {
                    if (responseEnvelope.Notification.ClientOperationId != request.ClientOperationId)
                    {
                        NotificationReceived?.Invoke(this, responseEnvelope.Notification);
                    }
                    continue;
                }

                if (responseEnvelope.MessageType != InterceptPipeMessageType.Response || responseEnvelope.Response is null)
                {
                    continue;
                }

                if (!responseEnvelope.Response.Success)
                {
                    throw new BackendRequestException(
                        responseEnvelope.Response.Message,
                        responseEnvelope.Response.ErrorCode);
                }

                return responseEnvelope.Response;
            }
        }
        catch (TimeoutException)
        {
            throw new BackendRequestException("连接后端超时，请确认后端服务是否在运行。", "PIPE_TIMEOUT");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ================= 通知订阅（常开 + 自动重连） =================

    private async Task NotificationLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var stream = new NamedPipeClientStream(".", InterceptPipeConstants.PipeName,
                    PipeDirection.InOut, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
                await stream.ConnectAsync(InterceptPipeConstants.ConnectTimeoutMs, cancellationToken);
                stream.ReadMode = PipeTransmissionMode.Byte;

                using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
                {
                    AutoFlush = true,
                };

                var subscriptionEnvelope = new InterceptPipeEnvelope
                {
                    MessageType = InterceptPipeMessageType.Request,
                    CorrelationId = Guid.NewGuid(),
                    Request = new InterceptPipeRequest
                    {
                        Command = InterceptPipeCommand.SubscribeNotifications,
                    },
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(subscriptionEnvelope, JsonOptions)).WaitAsync(cancellationToken);

                var ackLine = await reader.ReadLineAsync().WaitAsync(cancellationToken);
                if (ackLine is null)
                {
                    throw new InvalidOperationException("后端管道在确认订阅前关闭。");
                }

                var ackEnvelope = JsonSerializer.Deserialize<InterceptPipeEnvelope>(ackLine, JsonOptions);
                if (ackEnvelope?.Response is not { Success: true })
                {
                    throw new InvalidOperationException(ackEnvelope?.Response?.Message ?? "后端拒绝了通知订阅。");
                }

                _isConnected = true;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().WaitAsync(cancellationToken);
                    if (line is null)
                    {
                        break;
                    }

                    var envelope = JsonSerializer.Deserialize<InterceptPipeEnvelope>(line, JsonOptions);
                    if (envelope?.MessageType == InterceptPipeMessageType.Notification && envelope.Notification is not null)
                    {
                        NotificationReceived?.Invoke(this, envelope.Notification);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 后端未启动/重启：稍后重连
            }
            finally
            {
                _isConnected = false;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(InterceptPipeConstants.NotificationReconnectDelaySeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // ================= 生命周期 =================

    public void Dispose()
    {
        _disposed = true;
        CancellationTokenSource? cts;
        Task? task;
        lock (_sync)
        {
            cts = _notificationLoopCts;
            task = _notificationLoopTask;
            _notificationLoopCts = null;
            _notificationLoopTask = null;
        }
        try { cts?.Cancel(); } catch { }
        try { task?.Wait(2000); } catch { }
        _sendLock.Dispose();
    }
}

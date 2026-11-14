using TubaWinUi3.PluginSdk;

namespace Tubatools.CapybaraLulu;

/// <summary>插件生命周期与「应用/还原」的唯一入口。所有界面改动在 UI 线程执行。</summary>
internal static class CapybaraRuntime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IPluginHostAccess? _hostAccess;
    private static CapybaraSkin? _skin;
    private static bool _loaded;

    public static CapybaraSettings Settings { get; private set; } = new();
    public static bool IsApplied => _skin is not null;
    public static IPluginHostAccess? HostAccess => _hostAccess;
    public static string? LastError { get; private set; }

    public static async ValueTask StartAsync(IPluginStartupContext context, CancellationToken ct)
    {
        LastError = null;
        _hostAccess = context.HostAccess;
        if (_hostAccess is null)
        {
            LastError = "插件没有声明 fullTrust，无法修改主界面。";
            return;
        }
        try
        {
            if (!_loaded)
            {
                Settings = await CapybaraSettings.LoadAsync(key => context.GetSettingAsync(key, ct), ct);
                _loaded = true;
            }
            if (Settings.Enabled) await RebuildAsync(_hostAccess, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        // 美化包只占设置页一个入口，不注册成内置工具；即使设置加载失败也要把入口挂上，
        // 让用户能打开面板排查。
        CapybaraSettingsEntry.Attach(_hostAccess);
    }

    public static async ValueTask StopAsync(CancellationToken ct)
    {
        CapybaraSettingsEntry.Detach();
        var host = _hostAccess;
        if (host is null) return;
        try { await RestoreAsync(host, ct); }
        catch (Exception ex) { LastError = ex.Message; }
    }

    /// <summary>设置入口在启动钩子没加载成功时（例如插件被重新启用）补一次加载。</summary>
    public static async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_loaded || _hostAccess is null) return;
        Settings = await CapybaraSettings.LoadAsync(key => _hostAccess.GetSettingAsync(key, ct), ct);
        _loaded = true;
    }

    public static async Task SetEnabledAsync(bool enabled, CancellationToken ct)
    {
        Settings.Enabled = enabled;
        await SaveAsync(ct);
        var host = RequireHost();
        if (enabled) await RebuildAsync(host, ct);
        else await RestoreAsync(host, ct);
    }

    /// <summary>修改一项或多项设置：先落盘，再按需重建美化层。</summary>
    public static async Task UpdateAsync(Action<CapybaraSettings> mutate, CancellationToken ct)
    {
        mutate(Settings);
        await SaveAsync(ct);
        if (Settings.Enabled) await RebuildAsync(RequireHost(), ct);
    }

    public static async Task SaveAsync(CancellationToken ct)
    {
        var host = RequireHost();
        await Settings.SaveAsync((key, value) => host.SetSettingAsync(key, value, ct), ct);
    }

    /// <summary>拖动浓度滑块时只改透明度，避免反复重建图层造成闪烁。</summary>
    public static void PreviewOpacity(double opacity)
    {
        Settings.BackgroundOpacity = Math.Clamp(opacity, 0.08, 0.65);
        _skin?.UpdateBackgroundOpacity(Settings.BackgroundOpacity);
    }

    /// <summary>拖大小滑块时只改宽度，松开后再落盘。</summary>
    public static void PreviewMascotSize(double size)
    {
        Settings.MascotSize = Math.Clamp(size, 92, 190);
        _skin?.UpdateMascotSize(Settings.MascotSize);
    }

    public static void PreviewBubble(bool visible)
    {
        Settings.Bubble = visible;
        _skin?.UpdateBubbleVisible(visible);
    }

    public static async Task SpeakAsync(CancellationToken ct)
    {
        var host = RequireHost();
        if (!Settings.Enabled || !Settings.Corner) return;
        await host.RunOnUiThreadAsync(() => { _skin?.SpeakRandomForCurrentPage(); return Task.CompletedTask; }, ct);
    }

    public static async Task RebuildAsync(IPluginHostAccess host, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await host.RunOnUiThreadAsync(() =>
            {
                if (_skin is not null)
                {
                    try { _skin.Restore(); } catch { }
                    _skin = null;
                }
                if (Settings.Enabled)
                {
                    var skin = new CapybaraSkin(host, Settings);
                    try
                    {
                        skin.Apply();
                        _skin = skin;
                    }
                    catch
                    {
                        try { skin.Restore(); } catch { }
                        throw;
                    }
                }
                return Task.CompletedTask;
            }, ct);
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            throw;
        }
        finally { Gate.Release(); }
    }

    public static async Task RestoreAsync(IPluginHostAccess host, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await host.RunOnUiThreadAsync(() =>
            {
                if (_skin is null) return Task.CompletedTask;
                _skin.Restore();
                _skin = null;
                return Task.CompletedTask;
            }, ct);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            throw;
        }
        finally { Gate.Release(); }
    }

    private static IPluginHostAccess RequireHost()
        => _hostAccess ?? throw new InvalidOperationException("缺少 fullTrust 宿主访问权限，无法修改主界面。");
}

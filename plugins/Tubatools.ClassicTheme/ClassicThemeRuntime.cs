using TubaWinUi3.PluginSdk;

namespace Tubatools.ClassicTheme;

internal static class ClassicThemeRuntime
{
    public const string EnabledKey = "ClassicTheme.Enabled";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IPluginHostAccess? _hostAccess;
    private static ClassicThemeApplier? _applier;

    public static string? LastError { get; private set; }
    public static bool IsApplied => _applier is not null;

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
            var enabled = await context.GetSettingAsync(EnabledKey, ct);
            if (!string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase))
                await ApplyAsync(_hostAccess, ct);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    public static async ValueTask StopAsync(CancellationToken ct)
    {
        var host = _hostAccess;
        if (host is null) return;
        try { await RestoreAsync(host, ct); }
        catch (Exception ex) { LastError = ex.Message; }
    }

    public static async Task SetEnabledAsync(bool enabled, IPluginServices services, CancellationToken ct)
    {
        await services.SetSettingAsync(EnabledKey, enabled ? "true" : "false", ct);
        var host = _hostAccess ?? services.GetService<IPluginHostAccess>();
        if (host is null) throw new InvalidOperationException("经典样式需要 fullTrust 宿主访问权限。");
        _hostAccess = host;
        if (enabled) await ApplyAsync(host, ct);
        else await RestoreAsync(host, ct);
    }

    public static async Task ApplyAsync(IPluginHostAccess host, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (_applier is not null) return;
            var applier = new ClassicThemeApplier(host);
            try
            {
                await host.RunOnUiThreadAsync(() => { applier.Apply(); return Task.CompletedTask; }, ct);
            }
            catch
            {
                try { await host.RunOnUiThreadAsync(() => { applier.Restore(); return Task.CompletedTask; }, CancellationToken.None); }
                catch { }
                throw;
            }
            _applier = applier;
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
            var applier = _applier;
            if (applier is null) return;
            await host.RunOnUiThreadAsync(() => { applier.Restore(); return Task.CompletedTask; }, ct);
            _applier = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            throw;
        }
        finally { Gate.Release(); }
    }
}

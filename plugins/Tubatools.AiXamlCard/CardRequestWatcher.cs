using Microsoft.UI.Dispatching;

namespace Tubatools.AiXamlCard;

/// <summary>
/// 监听助手交来的卡片请求：<c>&lt;pluginData&gt;/requests/*.json</c>。
///
/// 这是「直接展示」的入口——助手指写一个 JSON 文件，插件立刻开窗渲染，
/// 不依赖后台扫描、不依赖宿主内部控件名，也不需要用户去别处翻页。
/// 每份请求处理完即删除，避免重启后重复弹出。
/// </summary>
internal sealed class CardRequestWatcher : IDisposable
{
    private readonly string _directory;
    private readonly DispatcherQueue _dispatcher;
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherQueueTimer? _debounce;
    private bool _disposed;

    public CardRequestWatcher(string pluginData, DispatcherQueue dispatcher)
    {
        _directory = CardRequestFile.DirectoryFor(pluginData);
        _dispatcher = dispatcher;
        try { Directory.CreateDirectory(_directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        _debounce = dispatcher.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(120);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => Drain();

        try
        {
            _watcher = new FileSystemWatcher(_directory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnTouched;
            _watcher.Changed += OnTouched;
            _watcher.Renamed += OnTouched;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _watcher = null;
        }
    }

    /// <summary>启动时先消费积压的请求，覆盖「写入早于监听就绪」的竞态。</summary>
    public void Start() => Drain();

    private void OnTouched(object sender, FileSystemEventArgs args) => Schedule();

    private void Schedule()
    {
        if (_disposed) return;
        if (_dispatcher.HasThreadAccess) _debounce.Start();
        else _dispatcher.TryEnqueue(() => { if (!_disposed) _debounce.Start(); });
    }

    private void Drain()
    {
        if (_disposed || !Directory.Exists(_directory)) return;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(_directory, "*.json").Order(StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        foreach (var file in files)
        {
            var request = CardRequestFile.Read(file);
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (request is null) continue;
            try { CardWindow.Show(request); }
            catch (Exception) { /* 一张卡片失败不应影响后续请求。 */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnTouched;
            _watcher.Changed -= OnTouched;
            _watcher.Renamed -= OnTouched;
            _watcher.Dispose();
        }
        _debounce?.Stop();
    }
}

using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using TubaWinUi3.PluginSdk;

namespace TubaWinUi3.Services.Plugins;

/// <summary>插件与主程序同权限运行。停用/卸载会立即隐藏工具；已加载的程序集要等重启后才完全释放。</summary>
public static class PluginManager
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, PluginLoadContext> Loaded = new(StringComparer.OrdinalIgnoreCase);
    // 已打开的旧工具可继续使用原上下文；新注册指向新版本，不强制卸载正在执行的代码。
    private static readonly List<PluginLoadContext> Retired = [];
    private static readonly Dictionary<string, StartupRegistration> Startups = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Started = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Errors = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object VisibilityGate = new();
    private static readonly HashSet<string> HiddenPackages = new(StringComparer.OrdinalIgnoreCase);
    public static string Root => Path.Combine(ConfigManager.GetDataDir(), "Plugins");
    private static string StatePath => Path.Combine(Root, "installed.json");
    public static string? GetError(string id) => Errors.GetValueOrDefault(id);
    public static bool IsLoaded(string id) => Loaded.ContainsKey(id);

    /// <summary>停用/卸载后立即从工具列表、搜索和导航中隐藏，不必等重启。</summary>
    internal static bool IsPackageVisible(string packageId)
    {
        lock (VisibilityGate) return !HiddenPackages.Contains(packageId);
    }

    private static void SetPackageVisible(string packageId, bool visible)
    {
        lock (VisibilityGate)
        {
            if (visible) HiddenPackages.Remove(packageId);
            else HiddenPackages.Add(packageId);
        }
    }

    private static void RefreshToolLists()
    {
        ToolCatalog.OnToolsChanged();
        if (App.MainWindow is { } window) window.DispatcherQueue.TryEnqueue(() => window.RefreshToolCategories());
    }
    public static PluginManifest GetMetadata(InstalledPlugin plugin) => PluginPackageRules.ReadManifest(PackageRoot(plugin));
    public static string? GetIconPath(InstalledPlugin plugin)
    {
        var manifest = GetMetadata(plugin);
        return manifest.Icon.Length > 0 ? PluginPackageRules.ResolveIcon(PackageRoot(plugin), manifest.Icon) : null;
    }

    public static IReadOnlyList<InstalledPlugin> GetInstalled()
    {
        if (!File.Exists(StatePath)) return [];
        // 损坏时保留文件并报告，不自动覆盖安装记录。
        return JsonSerializer.Deserialize<List<InstalledPlugin>>(File.ReadAllText(StatePath))
            ?? throw new InvalidDataException("插件安装记录无效。");
    }

    private static void Save(IEnumerable<InstalledPlugin> plugins)
    {
        Directory.CreateDirectory(Root);
        var temporary = StatePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(plugins));
        File.Move(temporary, StatePath, true);
    }

    private static string PackageRoot(InstalledPlugin p) => Path.Combine(Root,
        PluginPackageRules.Segment(p.Id), PluginPackageRules.Segment(p.Version));

    public static void LoadInstalled()
    {
        try
        {
            var installed = GetInstalled();
            try { CleanupUnused(installed); }
            catch (Exception ex) { Errors["storage"] = ex.Message; }
            foreach (var plugin in installed.Where(p => p.Enabled))
                try { Load(plugin); }
                catch (Exception ex) { Errors[plugin.Id] = ex.Message; }
        }
        catch (Exception ex) { Errors["storage"] = ex.Message; }
    }

    private static readonly object LoadGate = new();
    private static Task? _loadTask;

    /// <summary>
    /// 后台加载已安装插件（磁盘 IO + Assembly.Load），主窗口不必等它完成才出首帧。
    /// 重复调用复用同一个任务；完成后调用方自行刷新导航（Load 内部会把启动钩子编组回 UI）。
    /// </summary>
    public static Task LoadInstalledAsync()
    {
        lock (LoadGate)
            return _loadTask ??= Task.Run(LoadInstalled);
    }

    private static void Load(InstalledPlugin plugin, bool replace = false)
    {
        if (!replace && Loaded.ContainsKey(plugin.Id)) return;
        if (replace) StopPlugin(plugin.Id);
        var root = PackageRoot(plugin);
        var manifest = PluginPackageRules.ReadManifest(root);
        var context = new PluginLoadContext(PluginPackageRules.ResolveFile(root, manifest.Assembly));
        try
        {
            var assembly = context.LoadFromAssemblyPath(PluginPackageRules.ResolveFile(root, manifest.Assembly));
            var type = assembly.GetType(manifest.EntryType, throwOnError: true)!;
            if (Activator.CreateInstance(type) is not ITubaPlugin entry)
                throw new InvalidDataException("入口必须实现 ITubaPlugin。");
            var host = new RegistrationHost(plugin.Id, root, manifest);
            entry.Register(host);
            host.Commit();
            BuiltinIconService.ResetCache();
            if (Loaded.TryGetValue(plugin.Id, out var previous)) Retired.Add(previous);
            Loaded[plugin.Id] = context;
            if (entry is IPluginStartup startup) Startups[plugin.Id] = new(startup, manifest, root);
            else Startups.Remove(plugin.Id);
            Errors.Remove(plugin.Id);
            // 插件可能由后台启动加载：启动钩子必须等主窗口内容完成首次加载，
            // 否则主题类插件操作 NavigationView 会因没有视觉父级而抛异常。
            if (App.MainWindow is { Content: Microsoft.UI.Xaml.FrameworkElement windowRoot } && windowRoot.IsLoaded)
                StartPlugin(plugin.Id);
        }
        catch { context.Unload(); throw; }
    }

    /// <summary>主窗口创建后调用：启动所有已加载插件的可选启动钩子。</summary>
    public static void StartPlugins()
    {
        if (App.MainWindow is not { Content: Microsoft.UI.Xaml.FrameworkElement windowRoot } || !windowRoot.IsLoaded) return;
        foreach (var id in Startups.Keys.ToList()) StartPlugin(id);
    }

    private static void StartPlugin(string id)
    {
        if (!Startups.TryGetValue(id, out var registration) || Started.Contains(id)) return;
        if (App.MainWindow is not { } window) return;
        void Run()
        {
            if (!Started.Add(id)) return;
            try
            {
                var services = new PluginHostServices(id, "", registration.PackageRoot, window.DispatcherQueue, () => null, _ => { }, registration.Manifest.FullTrust);
                var hostAccess = registration.Manifest.FullTrust ? services.GetService<IPluginHostAccess>() : null;
                var task = registration.Entry.StartupAsync(new StartupContext(services, hostAccess), CancellationToken.None);
                if (!task.IsCompletedSuccessfully) _ = ObserveAsync(task.AsTask(), id);
            }
            catch (Exception ex)
            {
                Errors[id] = ex.Message;
                Started.Remove(id);
            }
        }
        if (window.DispatcherQueue.HasThreadAccess) Run();
        else window.DispatcherQueue.TryEnqueue(Run);
    }

    private static void StopPlugin(string id)
    {
        if (!Startups.TryGetValue(id, out var registration) || !Started.Remove(id)) return;
        void Run()
        {
            try
            {
                var task = registration.Entry.ShutdownAsync(CancellationToken.None);
                if (!task.IsCompletedSuccessfully) _ = ObserveAsync(task.AsTask(), id);
            }
            catch (Exception ex) { Errors[id] = ex.Message; }
        }
        if (App.MainWindow is { } window && !window.DispatcherQueue.HasThreadAccess)
            window.DispatcherQueue.TryEnqueue(Run);
        else Run();
    }

    private static async Task ObserveAsync(Task task, string id)
    {
        try { await task; }
        catch (Exception ex) { Errors[id] = ex.Message; }
    }

    private sealed record StartupRegistration(IPluginStartup Entry, PluginManifest Manifest, string PackageRoot);

    private sealed class StartupContext(PluginHostServices services, IPluginHostAccess? hostAccess) : IPluginStartupContext
    {
        public string PluginId => services.PluginId;
        public string DataDirectory => services.DataDirectory;
        public IPluginHostAccess? HostAccess => hostAccess;
        public Task<string?> GetSettingAsync(string key, CancellationToken ct = default) => services.GetSettingAsync(key, ct);
        public Task SetSettingAsync(string key, string? value, CancellationToken ct = default) => services.SetSettingAsync(key, value, ct);
    }

    /// <summary>加载用户从 AI 开发工作区明确选择的本地 NuGet 插件包。</summary>
    public static async Task InstallLocalAsync(string archive, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        var staging = Path.Combine(Root, ".staging-" + Guid.NewGuid().ToString("N"));
        string? moved = null;
        try
        {
            var installed = GetInstalled().ToList();
            var package = ReadPackageIdentity(archive);
            var record = new InstalledPlugin(package.Id, package.Version);
            var destination = PackageRoot(record);
            if (Directory.Exists(destination))
                throw new IOException("此版本已存在，请增加插件版本号再构建，以免覆盖正在使用的 DLL。");
            await Task.Run(() => ExtractPackage(archive, staging, package), ct);
            PluginPackageRules.ReadManifest(staging);
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            moved = destination;
            var updated = installed.Where(p => !p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase)).Append(record).ToList();
            Save(updated);
            SetPackageVisible(record.Id, true);
            try { Load(record, replace: true); }
            catch
            {
                Save(installed);
                throw;
            }
            ToolCatalog.OnToolsChanged();
            App.MainWindow?.RefreshToolCategories();
            moved = null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging)) DeleteWithoutLinks(staging);
                if (moved is not null && Directory.Exists(moved))
                    try { DeleteWithoutLinks(moved); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            finally { Gate.Release(); }
        }
    }

    internal static PluginPackage ReadPackageIdentity(string archive)
    {
        if (new FileInfo(archive).Length > 100L * 1024 * 1024) throw new InvalidDataException("插件包超过 100 MB。");
        using var zip = ZipFile.OpenRead(archive);
        var nuspec = zip.Entries.Single(e => !e.FullName.Contains('/') && !e.FullName.Contains('\\') && e.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspec.Length > 1024 * 1024) throw new InvalidDataException("插件元数据过大。");
        using var stream = nuspec.Open();
        using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        var metadata = System.Xml.Linq.XDocument.Load(reader).Descendants().Single(e => e.Name.LocalName == "metadata");
        string Field(string name) => metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? "";
        var package = new PluginPackage(Field("id"), Field("version"), Field("description"), Field("authors"));
        PluginPackageRules.Segment(package.Id);
        PluginPackageRules.Segment(package.Version);
        return package;
    }

    public static async Task InstallAsync(PluginPackage package, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        var staging = Path.Combine(Root, ".staging-" + Guid.NewGuid().ToString("N"));
        var archive = staging + ".nupkg";
        try
        {
            var installed = GetInstalled().ToList();
            var record = new InstalledPlugin(package.Id, package.Version);
            var destination = PackageRoot(record);
            if (installed.Any(p => p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase) && p.Version == package.Version))
                throw new InvalidOperationException("此版本已安装；加载失败时请查看错误后重启，或卸载后重装。");

            if (Directory.Exists(destination))
            {
                // 卸载只移除安装记录，文件要等重启后才能安全清理（旧程序集可能仍被已打开的页面使用）。
                // 重装同一版本时优先复用完好的旧目录；损坏或无法复用时再清掉重新解包。
                ct.ThrowIfCancellationRequested();
                if (IsPackageIntact(destination))
                {
                    CommitInstalled(installed, record);
                    return;
                }
                try { DeleteWithoutLinks(destination); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new IOException("旧版插件文件仍被占用，请重启应用后再安装。", ex);
                }
            }

            Directory.CreateDirectory(Root);
            await NuGetPluginSource.DownloadAsync(package, archive, ct);
            await Task.Run(() => ExtractPackage(archive, staging, package), ct);
            PluginPackageRules.ReadManifest(staging);
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            CommitInstalled(installed, record);
        }
        finally
        {
            try
            {
                if (File.Exists(archive)) File.Delete(archive);
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
            finally { Gate.Release(); }
        }
    }

    private static bool IsPackageIntact(string root)
    {
        try { PluginPackageRules.ReadManifest(root); return true; }
        catch { return false; }
    }

    private static void CommitInstalled(List<InstalledPlugin> installed, InstalledPlugin record)
    {
        installed.RemoveAll(p => p.Id.Equals(record.Id, StringComparison.OrdinalIgnoreCase));
        installed.Add(record);
        Save(installed);
        SetPackageVisible(record.Id, true);
        try { Load(record); }
        catch (Exception ex) { Errors[record.Id] = ex.Message; }
        RefreshToolLists();
    }

    internal static void ExtractPackage(string archive, string destination, PluginPackage expected)
    {
        using var zip = ZipFile.OpenRead(archive);
        long size = 0;
        foreach (var entry in zip.Entries)
        {
            size += entry.Length;
            if (size > 300 * 1024 * 1024 || zip.Entries.Count > 5000)
                throw new InvalidDataException("插件解压大小或文件数超过限制。");
            // 同时拒绝 ZIP 中的 Unix 符号链接。
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("插件包不能包含符号链接。");
            var path = PluginPackageRules.ResolveFile(destination, entry.FullName);
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path);
        }
        var nuspec = Directory.GetFiles(destination, "*.nuspec").Single();
        var metadata = System.Xml.Linq.XDocument.Load(nuspec).Descendants().First(e => e.Name.LocalName == "metadata");
        string Field(string name) => metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? "";
        if (!string.Equals(Field("id"), expected.Id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Field("version"), expected.Version, StringComparison.OrdinalIgnoreCase) ||
            !Field("tags").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries).Contains("Tubatools", StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("NuGet 包身份或 Tubatools 标签与列表不符。");
    }

    public static async Task SetEnabledAsync(string id, bool? enabled)
    {
        await Gate.WaitAsync();
        try
        {
            var records = GetInstalled().ToList();
            var old = records.First(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            records.Remove(old);
            var record = enabled.HasValue ? old with { Enabled = enabled.Value } : null;
            if (record is not null) records.Add(record);
            Save(records);
            if (record is { Enabled: true })
            {
                SetPackageVisible(record.Id, true);
                if (!IsLoaded(record.Id))
                {
                    try { Load(record); }
                    catch (Exception ex) { Errors[record.Id] = ex.Message; }
                }
                StartPlugin(record.Id);
            }
            else
            {
                StopPlugin(id);
                SetPackageVisible(id, false);
            }
            RefreshToolLists();
            // 已加载的程序集不强制卸载；重启后才能完全停止插件的后台代码。
        }
        finally { Gate.Release(); }
    }

    internal static void CleanupUnused(IReadOnlyList<InstalledPlugin> installed)
    {
        if (!Directory.Exists(Root)) return;
        var keep = installed.Select(PackageRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var package in Directory.GetDirectories(Root))
        {
            if ((File.GetAttributes(package) & FileAttributes.ReparsePoint) != 0 || Path.GetFileName(package).StartsWith('.')) continue;
            foreach (var version in Directory.GetDirectories(package))
            {
                if (keep.Contains(version)) continue;
                try { DeleteWithoutLinks(version); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void DeleteWithoutLinks(string directory)
    {
        var full = Path.GetFullPath(directory);
        if (!full.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("清理路径不在插件目录内。");
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("跳过链接。");
        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("跳过链接。");
            if ((attributes & FileAttributes.Directory) != 0) DeleteWithoutLinks(entry);
            else File.Delete(entry);
        }
        Directory.Delete(full);
    }

    internal sealed class RegistrationHost(string packageId, string packageRoot, PluginManifest manifest) : IPluginHost
    {
        private readonly List<IBuiltinTool> _pending = [];
        private bool _committed;
        public int ApiVersion => 2;
        public string Language => LocalizationService.CurrentLanguage;
        public void RegisterTool(PluginToolDefinition tool)
        {
            if (_committed) throw new InvalidOperationException("只能在 Register 期间注册工具。");
            ArgumentNullException.ThrowIfNull(tool);
            string? xaml = null;
            if (tool.XamlPath is { } xamlPath)
            {
                var path = PluginPackageRules.ResolveFile(packageRoot, xamlPath);
                if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("XAML 文件超过 2 MB。");
                xaml = File.ReadAllText(path);
            }
            PluginToolRules.Validate(tool, xaml);
            var icon = tool.IconPath.Length > 0 ? tool.IconPath : manifest.Icon;
            _pending.Add(new PluginXamlTool(packageId, tool, icon.Length > 0 ? PluginPackageRules.ResolveIcon(packageRoot, icon) : null, xaml, packageRoot, manifest.FullTrust));
        }
        public void Commit()
        {
            if (_committed) throw new InvalidOperationException("注册已提交。");
            BuiltinToolRegistry.ReplacePluginTools(packageId, _pending);
            _committed = true;
        }
    }

    private sealed class PluginLoadContext(string entryPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(entryPath);
        protected override Assembly? Load(AssemblyName name)
        {
            // 契约与 WinUI 类型必须共享宿主身份。
            if (name.Name == typeof(ITubaPlugin).Assembly.GetName().Name || name.Name == "TubaWinUi3" ||
                name.Name?.StartsWith("Microsoft.UI.") == true || name.Name is "WinRT.Runtime" or "Microsoft.WinUI" or "Microsoft.Windows.SDK.NET")
                return Default.LoadFromAssemblyName(name);
            var path = _resolver.ResolveAssemblyToPath(name);
            // 插件包里的第三方依赖通常与入口 DLL 平铺在一起；deps.json 可能把 RID 资产
            // 映射到 runtimes/<rid>/lib/<tfm>/... 这类包内布局，文件不在时退回同目录按名解析。
            if (path is not null && !File.Exists(path)) path = null;
            if (path is null && name.Name is { } simple)
            {
                var sibling = Path.Combine(Path.GetDirectoryName(entryPath)!, simple + ".dll");
                if (File.Exists(sibling)) path = sibling;
            }
            return path is null ? null : LoadFromAssemblyPath(path);
        }
        protected override IntPtr LoadUnmanagedDll(string name)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(name);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}


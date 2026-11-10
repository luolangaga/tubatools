namespace TubaWinUi3.PluginSdk;

public sealed record PluginBackground(string? ImagePath, double Opacity);
public interface IPluginBackgroundService
{
    Task<PluginBackground> GetAsync(CancellationToken ct = default);
    /// <summary>Package-relative or absolute local image. Null clears the toolbox background; copied images survive plugin upgrades.</summary>
    Task SetAsync(string? imagePath, double opacity = 0.15, CancellationToken ct = default);
    Task SetOpacityAsync(double opacity, CancellationToken ct = default);
}

public sealed record PluginNavigationItem(string Id, string Title, string Glyph, bool IsVisible, int Order, bool IsFooter = false, string IconPath = "");
public sealed record PluginNavigationChange(string Id, string? Title = null, string? Glyph = null, bool? IsVisible = null, int? Order = null, string? IconPath = null);
public interface IPluginNavigationService
{
    Task<IReadOnlyList<PluginNavigationItem>> GetItemsAsync(CancellationToken ct = default);
    /// <summary>Null properties leave prior overrides intact. Empty Title/Glyph/IconPath restores that property's default.</summary>
    Task UpdateAsync(PluginNavigationChange change, CancellationToken ct = default);
    /// <summary>Moves listed menu IDs to the front in order; unlisted items retain their relative order. Settings remains in the footer.</summary>
    Task SetOrderAsync(IReadOnlyList<string> ids, CancellationToken ct = default);
    /// <summary>Null resets all navigation overrides; a specific ID resets only that item.</summary>
    Task ResetAsync(string? id = null, CancellationToken ct = default);
}

public sealed record PluginHostInfo(string Version, int ApiVersion, string Language, string Architecture, bool IsPackaged);
public interface IPluginHostInfoService { PluginHostInfo GetInfo(); }
public enum PluginDiagnosticKind { Hardware, Sensors, Disks, CpuZ }
public interface IPluginDiagnosticsService
{
    /// <summary>Read-only host diagnostics serialized as JSON. Sensors share the host monitor without disposing it.</summary>
    Task<string> ReadJsonAsync(PluginDiagnosticKind kind, CancellationToken ct = default);
}

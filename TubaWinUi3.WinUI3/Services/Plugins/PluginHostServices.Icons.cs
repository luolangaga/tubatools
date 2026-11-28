using TubaWinUi3.PluginSdk;

namespace TubaWinUi3.Services.Plugins;

internal sealed partial class PluginHostServices
{
    /// <summary>图标工坊的 SDK 门面：所有读取/写入都回到 UI 线程，图片解码放到线程池。</summary>
    private sealed class IconFacade(PluginHostServices host) : IPluginIconService
    {
        public async Task<IReadOnlyList<PluginIconSlot>> GetSlotsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<PluginIconSlot> result = [];
            await host.OnUi(() => { result = IconThemeService.GetSlots(); return Task.CompletedTask; }, ct);
            return result;
        }

        public async Task<PluginIconState> GetStateAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            PluginIconState? result = null;
            await host.OnUi(() => { result = IconThemeService.GetState(); return Task.CompletedTask; }, ct);
            return result!;
        }

        public async Task SetImageAsync(PluginIconGroup group, string id, string sourcePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("图标位置 id 为空。", nameof(id));
            if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("图片路径为空。", nameof(sourcePath));
            ct.ThrowIfCancellationRequested();

            // 先在 UI 线程确认这个位置真实存在，避免把任意文件写进设置。
            string canonicalId = "";
            await host.OnUi(() =>
            {
                var slot = IconThemeService.GetSlots().FirstOrDefault(s => s.Group == group && s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new KeyNotFoundException($"找不到可定制的图标位置：{group}:{id}");
                canonicalId = slot.Id;
                return Task.CompletedTask;
            }, ct);

            ct.ThrowIfCancellationRequested();
            var imported = await Task.Run(() => IconStudioImageImporter.Import(sourcePath), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            await host.OnUi(() => { IconThemeService.SetImportedImage(group, canonicalId, imported); return Task.CompletedTask; }, ct);
        }

        public Task ResetAsync(PluginIconGroup group, string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("图标位置 id 为空。", nameof(id));
            ct.ThrowIfCancellationRequested();
            return host.OnUi(() => { IconThemeService.Reset(group, id); return Task.CompletedTask; }, ct);
        }

        public Task ResetAllAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return host.OnUi(() => { IconThemeService.ResetAll(); return Task.CompletedTask; }, ct);
        }

        public Task ApplyPresetAsync(PluginIconPreset preset, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return host.OnUi(() => { IconThemeService.ApplyPreset(preset); return Task.CompletedTask; }, ct);
        }
    }
}

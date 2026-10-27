namespace TubaWinUi3.Models;

public sealed class HardwareInfoItem
{
    public required string Label { get; init; }

    public required string Value { get; set; }

    public string? BrandKey { get; set; }

    public bool IsVerified { get; set; }

    /// <summary>替换数据源时仍需保留的补充信息（如 WMI 报告的处理器缓存）。</summary>
    public string? SupplementalValue { get; set; }

    /// <summary>独立物理设备的完整描述；经典版仍使用聚合的 Value，全景按设备逐张展示。</summary>
    public IReadOnlyList<string> DeviceValues { get; set; } = [];

    public string? NicknameValue { get; set; }

    /// <summary>显示用标签：按当前界面语言翻译（Label 本身是数据键，服务内部比较仍用中文）。</summary>
    public string DisplayLabel => Services.LocalizationService.TranslateHardwareLabel(Label);

    /// <summary>模板在 UI 线程按需取图，不在后台硬件采集时创建 SvgImageSource。</summary>
    public Microsoft.UI.Xaml.Media.ImageSource? IconSource => Services.HardwareIconService.Get(Services.HardwareIconService.KeyForLabel(Label));

    /// <summary>详情行提示（模板内 ToolTip 走绑定——附加属性不能用 Uid）。</summary>
    public string CopyHint => Services.LocalizationService.L("Hw_CopyHint", "点击复制");
}

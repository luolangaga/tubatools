using System.Text.Json;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// YOLO/DETR 检测的可调参数（按模型持久化于 AppSettings）。默认值随模型家族而异
/// （yolo 0.25 / detr 0.5，与 <see cref="DetectionRunner.DefaultThreshold"/> 一致）。
/// </summary>
public sealed class DetectionOptions
{
    /// <summary>置信度阈值 0..1。</summary>
    public float Confidence { get; set; } = 0.25f;

    /// <summary>NMS 的 IoU 阈值 0..1（越高保留越多重叠框）。</summary>
    public float Iou { get; set; } = 0.45f;

    /// <summary>最多保留的检测框数（取代此前硬编码的 100）。</summary>
    public int MaxDetections { get; set; } = 300;

    /// <summary>类别内 NMS：不同类别互不抑制（避免猫和狗重叠时只剩一个）。</summary>
    public bool ClassAwareNms { get; set; } = true;

    /// <summary>被禁用的类别（按模型原始英文标签，如 "person"）。命中的检测项会被过滤。</summary>
    public HashSet<string> DisabledClasses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOpts = new();

    internal static string KeyFor(string modelId) => $"AiPlayground_Detect_{modelId}";

    /// <summary>按模型读取参数；无存档或损坏时返回按家族默认的实例。</summary>
    public static DetectionOptions Load(string modelId, bool isYolo)
    {
        var fallback = CreateDefault(isYolo);
        try
        {
            var raw = AppSettings.Get(KeyFor(modelId));
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var loaded = JsonSerializer.Deserialize<DetectionOptions>(raw, JsonOpts);
            if (loaded is null) return fallback;
            loaded.DisabledClasses ??= new(StringComparer.OrdinalIgnoreCase);
            return loaded.Normalized();
        }
        catch
        {
            return fallback;
        }
    }

    public static DetectionOptions CreateDefault(bool isYolo) => new()
    {
        Confidence = isYolo ? 0.25f : 0.5f,
        Iou = 0.45f,
        MaxDetections = 300,
        ClassAwareNms = true,
    };

    public void Save(string modelId)
    {
        try
        {
            AppSettings.Set(KeyFor(modelId), JsonSerializer.Serialize(Normalized(), JsonOpts));
        }
        catch { }
    }

    /// <summary>取值收敛到合法区间（防手改存档越界）。</summary>
    public DetectionOptions Normalized()
    {
        Confidence = Math.Clamp(Confidence, 0.01f, 0.99f);
        Iou = Math.Clamp(Iou, 0.05f, 0.95f);
        MaxDetections = Math.Clamp(MaxDetections, 1, 1000);
        DisabledClasses ??= new(StringComparer.OrdinalIgnoreCase);
        return this;
    }

    public DetectionOptions Clone() => new()
    {
        Confidence = Confidence,
        Iou = Iou,
        MaxDetections = MaxDetections,
        ClassAwareNms = ClassAwareNms,
        DisabledClasses = new HashSet<string>(DisabledClasses, StringComparer.OrdinalIgnoreCase),
    };
}

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>一个检测框（坐标为原图像素）。</summary>
public sealed class DetectionItem
{
    /// <summary>展示用标签（已翻译为中文）。</summary>
    public required string Label { get; init; }
    /// <summary>模型原始标签（英文，如 "person"）；供类别过滤与绘制规则匹配。</summary>
    public string RawLabel { get; init; } = "";
    /// <summary>类别索引（模型输出）；-1 = 未知。</summary>
    public int ClassId { get; init; } = -1;
    public required float Score { get; init; }
    public required float X1 { get; init; }
    public required float Y1 { get; init; }
    public required float X2 { get; init; }
    public required float Y2 { get; init; }

    public float Width => X2 - X1;
    public float Height => Y2 - Y1;
}

/// <summary>
/// 目标检测：支持 DETR（sigmoid + cxcywh 反归一化）与 YOLOv8/v9/v10 型输出（[1,84,N] + NMS）。
/// YOLO 使用内置 COCO-80 标签；DETR 使用模型 config.json 的 id2label。
/// </summary>
public sealed class DetectionRunner : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _family;
    private readonly string _inputName;
    private readonly int _yoloInputSize;
    private readonly string[] _configLabels;
    private readonly string[] _cocoLabels;
    private bool _disposed;

    public string RuntimeNote { get; }
    public bool IsYolo => _family == "yolo";
    public float DefaultThreshold => IsYolo ? 0.25f : 0.5f;

    public DetectionRunner(AiSessionResult sessionResult, string modelDir, string family)
    {
        _session = sessionResult.Session;
        RuntimeNote = sessionResult.RuntimeNote;
        _family = family;
        _inputName = _session.InputMetadata.Keys.First();
        var dims = _session.InputMetadata.First().Value.Dimensions;
        _yoloInputSize = dims.Length == 4 && dims[2] > 0 ? dims[2] : 640;
        _configLabels = AiPlaygroundLabels.LoadId2Label(modelDir);
        _cocoLabels = AiPlaygroundLabels.LoadCocoLabels();
    }

    /// <summary>旧签名（阈值 + IoU 直接给），保留以兼容既有调用/测试。</summary>
    public List<DetectionItem> Run(string imagePath, float threshold, float iouThreshold) =>
        Run(imagePath, new DetectionOptions
        {
            Confidence = threshold,
            Iou = iouThreshold,
            ClassAwareNms = false,       // 旧行为 = 类别无关 NMS
            MaxDetections = 100,         // 旧行为上限
        }.Normalized());

    /// <summary>按参数检测（可调 NMS/类别过滤/最大框数）。</summary>
    public List<DetectionItem> Run(string imagePath, DetectionOptions options)
    {
        var result = IsYolo
            ? RunYolo(imagePath, options)
            : RunDetr(imagePath, options);
        return FilterDisabled(result, options);
    }

    /// <summary>对一帧 BGRA 图像检测（摄像头/屏幕/窗口实时流用）。</summary>
    public List<DetectionItem> Run(FrameBuffer frame, DetectionOptions options)
    {
        var result = IsYolo
            ? RunYoloFrame(frame, options)
            : RunDetrFrame(frame, options);
        return FilterDisabled(result, options);
    }

    private List<DetectionItem> RunYoloFrame(FrameBuffer frame, DetectionOptions options)
    {
        var prepared = ImagePreprocess.PrepareYolo(frame, _yoloInputSize);
        using var outputs = _session.Run(
            new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, ToTensor(prepared)) });
        var value = outputs.First().AsTensor<float>();
        return DetectionDecode.Yolo(value.ToArray(), value.Dimensions.ToArray(), _cocoLabels,
            frame.Width, frame.Height, _yoloInputSize, options);
    }

    private List<DetectionItem> RunDetrFrame(FrameBuffer frame, DetectionOptions options)
    {
        var prepared = ImagePreprocess.PrepareDetr(frame, 800, 1333);
        using var outputs = _session.Run(
            new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, ToTensor(prepared)) });

        float[]? logits = null;
        int[]? logitsDims = null;
        float[]? boxes = null;
        foreach (var output in outputs)
        {
            var value = output.AsTensor<float>();
            var dims = value.Dimensions.ToArray();
            var data = value.ToArray();
            if (boxes is null && dims.Length >= 2 && dims[^1] == 4) boxes = data;
            else if (logits is null) { logits = data; logitsDims = dims; }
        }
        if (logits is null || boxes is null || logitsDims is null || logitsDims.Length < 3)
            throw new InvalidOperationException("无法识别 DETR 模型输出（需要 logits 与 pred_boxes）。");

        return DetectionDecode.Detr(logits, logitsDims, boxes, _configLabels, _cocoLabels,
            frame.Width, frame.Height, options);
    }

    private static DenseTensor<float> ToTensor(PreparedImage prepared) =>
        new(prepared.Data, new[] { 1, 3, prepared.Height, prepared.Width }, false);

    private static List<DetectionItem> FilterDisabled(List<DetectionItem> items, DetectionOptions options) =>
        options.DisabledClasses.Count == 0
            ? items
            : items.Where(i => !options.DisabledClasses.Contains(i.RawLabel)).ToList();

    private List<DetectionItem> RunDetr(string imagePath, DetectionOptions options)
    {
        var (imageWidth, imageHeight) = ImagePreprocess.GetImageSize(imagePath);
        var prepared = ImagePreprocess.PrepareDetr(imagePath, 800, 1333);
        var tensor = new DenseTensor<float>(
            prepared.Data, new[] { 1, 3, prepared.Height, prepared.Width }, false);
        using var outputs = _session.Run(
            new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });

        float[]? logits = null;
        int[]? logitsDims = null;
        float[]? boxes = null;
        foreach (var output in outputs)
        {
            var value = output.AsTensor<float>();
            var dims = value.Dimensions.ToArray();
            var data = value.ToArray();
            if (boxes is null && dims.Length >= 2 && dims[^1] == 4)
            {
                boxes = data;
            }
            else if (logits is null)
            {
                logits = data;
                logitsDims = dims;
            }
        }
        if (logits is null || boxes is null || logitsDims is null || logitsDims.Length < 3)
            throw new InvalidOperationException("无法识别 DETR 模型输出（需要 logits 与 pred_boxes）。");

        return DetectionDecode.Detr(
            logits, logitsDims, boxes, _configLabels, _cocoLabels,
            imageWidth, imageHeight, options);
    }

    private List<DetectionItem> RunYolo(string imagePath, DetectionOptions options)
    {
        var (imageWidth, imageHeight) = ImagePreprocess.GetImageSize(imagePath);
        var prepared = ImagePreprocess.PrepareYolo(imagePath, _yoloInputSize);
        var tensor = new DenseTensor<float>(
            prepared.Data, new[] { 1, 3, prepared.Height, prepared.Width }, false);
        using var outputs = _session.Run(
            new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });

        var value = outputs.First().AsTensor<float>();
        var dims = value.Dimensions.ToArray();
        return DetectionDecode.Yolo(
            value.ToArray(), dims, _cocoLabels,
            imageWidth, imageHeight, _yoloInputSize, options);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }
}

/// <summary>检测解码纯逻辑（可单测）：张量 → 检测框。</summary>
internal static class DetectionDecode
{
    /// <summary>DETR：logits [1, N, C]（sigmoid 分数）+ boxes [1, N, 4]（归一化 cxcywh）。</summary>
    internal static List<DetectionItem> Detr(
        float[] logits,
        int[] logitsDims,
        float[] boxes,
        string[] configLabels,
        string[] cocoLabels,
        int imageWidth,
        int imageHeight,
        DetectionOptions options)
    {
        int queries = logitsDims[1];
        int classCount = logitsDims[2];
        var items = new List<DetectionItem>();
        for (int query = 0; query < queries; query++)
        {
            int baseIndex = query * classCount;
            int bestClass = -1;
            float bestLogit = float.MinValue;
            for (int c = 0; c < classCount; c++)
            {
                var name = LabelFor(c, configLabels, cocoLabels);
                if (IsNoObject(name)) continue;
                var logit = logits[baseIndex + c];
                if (logit > bestLogit)
                {
                    bestLogit = logit;
                    bestClass = c;
                }
            }
            if (bestClass < 0) continue;

            var score = AiMath.Sigmoid(bestLogit);
            if (score < options.Confidence) continue;

            float cx = boxes[query * 4], cy = boxes[query * 4 + 1];
            float w = boxes[query * 4 + 2], h = boxes[query * 4 + 3];
            var raw = LabelFor(bestClass, configLabels, cocoLabels);
            items.Add(new DetectionItem
            {
                Label = AiPlaygroundLabels.Translate(raw),
                RawLabel = raw,
                ClassId = bestClass,
                Score = score,
                X1 = Clamp((cx - w / 2f) * imageWidth, imageWidth),
                Y1 = Clamp((cy - h / 2f) * imageHeight, imageHeight),
                X2 = Clamp((cx + w / 2f) * imageWidth, imageWidth),
                Y2 = Clamp((cy + h / 2f) * imageHeight, imageHeight),
            });
        }
        return NmsAndSort(items, options);
    }

    /// <summary>
    /// YOLO：支持两种导出布局。
    /// - <c>[1, attrs, anchors]</c> 原始头（attrs = 4 + 类别数，可能未含 sigmoid）
    /// - <c>[N, 6]</c> **NMS 已内置**的导出（x1,y1,x2,y2, score, class_id；坐标在 inputSize 空间）
    /// 实测 Xenova/yolov9-c 是后者。
    /// </summary>
    internal static List<DetectionItem> Yolo(
        float[] output,
        int[] dims,
        string[] cocoLabels,
        int imageWidth,
        int imageHeight,
        int inputSize,
        DetectionOptions options)
    {
        if (dims.Length == 2 && dims[1] == 6)
            return YoloNmsOutput(output, dims[0], cocoLabels, imageWidth, imageHeight, inputSize, options);

        if (dims.Length != 3)
            throw new InvalidOperationException($"无法识别 YOLO 输出形状：[{string.Join(",", dims)}]");

        bool anchorMajor = dims[1] > dims[2];
        int attrs = anchorMajor ? dims[2] : dims[1];
        int anchors = anchorMajor ? dims[1] : dims[2];
        int classCount = attrs - 4;
        if (classCount <= 0 || anchors <= 0)
            throw new InvalidOperationException($"YOLO 输出维度异常：[{string.Join(",", dims)}]");

        float Get(int attr, int anchor) =>
            anchorMajor ? output[anchor * attrs + attr] : output[attr * anchors + anchor];

        // 若导出的图未包含 sigmoid（出现负值），迭代时补算
        bool needsSigmoid = false;
        int step = Math.Max(1, anchors / 32);
        for (int c = 4; c < attrs && !needsSigmoid; c++)
        {
            for (int anchor = 0; anchor < anchors; anchor += step)
            {
                if (Get(c, anchor) < 0)
                {
                    needsSigmoid = true;
                    break;
                }
            }
        }

        float scaleX = imageWidth / (float)inputSize;
        float scaleY = imageHeight / (float)inputSize;
        var items = new List<DetectionItem>();
        for (int anchor = 0; anchor < anchors; anchor++)
        {
            int bestClass = -1;
            float bestScore = 0;
            for (int c = 0; c < classCount; c++)
            {
                float score = Get(4 + c, anchor);
                if (needsSigmoid) score = AiMath.Sigmoid(score);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestClass = c;
                }
            }
            if (bestClass < 0 || bestScore < options.Confidence) continue;

            float cx = Get(0, anchor), cy = Get(1, anchor);
            float w = Get(2, anchor), h = Get(3, anchor);
            var raw = bestClass < cocoLabels.Length ? cocoLabels[bestClass] : $"class {bestClass}";
            items.Add(new DetectionItem
            {
                Label = AiPlaygroundLabels.Translate(raw),
                RawLabel = raw,
                ClassId = bestClass,
                Score = bestScore,
                X1 = Clamp((cx - w / 2f) * scaleX, imageWidth),
                Y1 = Clamp((cy - h / 2f) * scaleY, imageHeight),
                X2 = Clamp((cx + w / 2f) * scaleX, imageWidth),
                Y2 = Clamp((cy + h / 2f) * scaleY, imageHeight),
            });
        }
        return NmsAndSort(items, options);
    }

    /// <summary>
    /// [N, 6] 布局（NMS 已在图内完成）：x1,y1,x2,y2, score, class_id。
    /// 坐标位于 inputSize 空间，需按原图尺寸等比缩放回像素。
    /// </summary>
    internal static List<DetectionItem> YoloNmsOutput(
        float[] output,
        int count,
        string[] cocoLabels,
        int imageWidth,
        int imageHeight,
        int inputSize,
        DetectionOptions options)
    {
        float scaleX = imageWidth / (float)Math.Max(1, inputSize);
        float scaleY = imageHeight / (float)Math.Max(1, inputSize);
        var items = new List<DetectionItem>();

        for (var i = 0; i < count; i++)
        {
            var b = i * 6;
            if (b + 5 >= output.Length) break;
            var score = output[b + 4];
            if (score < options.Confidence) continue;

            var classId = (int)Math.Round(output[b + 5]);
            var raw = classId >= 0 && classId < cocoLabels.Length ? cocoLabels[classId] : $"class {classId}";

            items.Add(new DetectionItem
            {
                Label = AiPlaygroundLabels.Translate(raw),
                RawLabel = raw,
                ClassId = classId,
                Score = score,
                X1 = Clamp(output[b] * scaleX, imageWidth),
                Y1 = Clamp(output[b + 1] * scaleY, imageHeight),
                X2 = Clamp(output[b + 2] * scaleX, imageWidth),
                Y2 = Clamp(output[b + 3] * scaleY, imageHeight),
            });
        }
        return NmsAndSort(items, options);
    }

    internal static string LabelFor(int index, string[] configLabels, string[] cocoLabels)
    {
        if (index < configLabels.Length && !string.IsNullOrEmpty(configLabels[index]))
            return configLabels[index];
        if (index < cocoLabels.Length)
            return cocoLabels[index];
        return $"class {index}";
    }

    internal static bool IsNoObject(string label) =>
        label.Length == 0
        || label.Equals("N/A", StringComparison.OrdinalIgnoreCase)
        || label.Equals("no-object", StringComparison.OrdinalIgnoreCase)
        || label.Equals("background", StringComparison.OrdinalIgnoreCase);

    private static float Clamp(float value, float max) => Math.Clamp(value, 0, max);

    /// <summary>
    /// NMS + 排序：<paramref name="options"/>.ClassAwareNms 为真时按类别分组分别抑制
    /// （不同类互不影响），否则类别无关；上限 = <paramref name="options"/>.MaxDetections。
    /// </summary>
    internal static List<DetectionItem> NmsAndSort(List<DetectionItem> items, DetectionOptions options)
    {
        if (items.Count <= 1) return items;

        if (options.ClassAwareNms)
        {
            var kept = new List<DetectionItem>();
            foreach (var group in items.GroupBy(i => i.ClassId))
            {
                var groupItems = group.ToList();
                var boxes = groupItems.Select(i => new[] { i.X1, i.Y1, i.X2, i.Y2 }).ToList();
                var scores = groupItems.Select(i => i.Score).ToList();
                var idx = AiMath.Nms(boxes, scores, options.Iou, maxOutput: options.MaxDetections);
                kept.AddRange(idx.Select(i => groupItems[i]));
            }
            return kept.OrderByDescending(i => i.Score).Take(options.MaxDetections).ToList();
        }

        var allBoxes = items.Select(i => new[] { i.X1, i.Y1, i.X2, i.Y2 }).ToList();
        var allScores = items.Select(i => i.Score).ToList();
        var keep = AiMath.Nms(allBoxes, allScores, options.Iou, maxOutput: options.MaxDetections);
        return keep.Select(i => items[i]).OrderByDescending(i => i.Score).ToList();
    }

    /// <summary>旧签名（仅 IoU），保留以兼容既有调用/测试；等价于默认选项下的类别无关 NMS。</summary>
    internal static List<DetectionItem> NmsAndSort(List<DetectionItem> items, float iouThreshold) =>
        NmsAndSort(items, new DetectionOptions
        {
            Iou = iouThreshold,
            ClassAwareNms = false,
            MaxDetections = 100,
        }.Normalized());
}

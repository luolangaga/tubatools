using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TubaWinUi3.Services.AiPlayground;

public sealed class ClassificationResult
{
    /// <summary>展示用标签（已翻译）。</summary>
    public required string Label { get; init; }
    /// <summary>模型原始标签（英文，如 "tabby, tabby cat"）；供规则匹配与文件归类。</summary>
    public string RawLabel { get; init; } = "";
    public required float Score { get; init; }
}

/// <summary>图像分类：预处理 → ONNX 会话 → softmax → Top-K（标签来自模型 config.json id2label）。</summary>
public sealed class ClassificationRunner : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string[] _labels;
    private readonly int _size;
    private readonly bool _timmCrop;
    private bool _disposed;

    public string RuntimeNote { get; }

    public ClassificationRunner(AiSessionResult sessionResult, string modelDir, bool timmCrop)
    {
        _session = sessionResult.Session;
        RuntimeNote = sessionResult.RuntimeNote;
        _timmCrop = timmCrop;
        _inputName = _session.InputMetadata.Keys.First();
        var dims = _session.InputMetadata.First().Value.Dimensions;
        int height = dims.Length == 4 && dims[2] > 0 ? dims[2] : 224;
        int width = dims.Length == 4 && dims[3] > 0 ? dims[3] : 224;
        _size = Math.Max(32, Math.Min(height, width));
        _labels = AiPlaygroundLabels.LoadId2Label(modelDir);
    }

    public List<ClassificationResult> Run(string imagePath, int topK = 5)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ClassificationRunner));
        var prepared = ImagePreprocess.PrepareClassification(imagePath, _size, _timmCrop);
        var tensor = new DenseTensor<float>(
            prepared.Data, new[] { 1, 3, prepared.Height, prepared.Width }, false);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
        using var outputs = _session.Run(inputs);

        var logits = outputs.First().AsEnumerable<float>().ToArray();
        var probabilities = AiMath.Softmax(logits);
        var top = AiMath.TopK(probabilities, topK);

        var results = new List<ClassificationResult>(top.Length);
        foreach (var (index, score) in top)
        {
            var raw = index < _labels.Length ? _labels[index] : $"class {index}";
            var display = AiPlaygroundLabels.Translate(AiPlaygroundLabels.Shorten(raw));
            results.Add(new ClassificationResult
            {
                Label = display,
                RawLabel = AiPlaygroundLabels.Shorten(raw),
                Score = score,
            });
        }
        return results;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }
}

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>图像特征向量（CLIP 视觉编码器 → L2 归一化），用于相似度对比。</summary>
public sealed class EmbeddingRunner : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _size;
    private bool _disposed;

    public string RuntimeNote { get; }
    public int Dimension { get; private set; }

    public EmbeddingRunner(AiSessionResult sessionResult)
    {
        _session = sessionResult.Session;
        RuntimeNote = sessionResult.RuntimeNote;
        _inputName = _session.InputMetadata.Keys.First();
        var dims = _session.InputMetadata.First().Value.Dimensions;
        int height = dims.Length == 4 && dims[2] > 0 ? dims[2] : 224;
        int width = dims.Length == 4 && dims[3] > 0 ? dims[3] : 224;
        _size = Math.Max(32, Math.Min(height, width));
    }

    public float[] Embed(string imagePath)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EmbeddingRunner));
        var prepared = ImagePreprocess.PrepareClipVision(imagePath, _size);
        var tensor = new DenseTensor<float>(
            prepared.Data, new[] { 1, 3, prepared.Height, prepared.Width }, false);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
        using var outputs = _session.Run(inputs);

        // 输出选择：优先 image_embeds → pooler_output → 第一个输出（[1,T,D] 时按 token 平均池化）
        DisposableNamedOnnxValue? chosen = null;
        foreach (var output in outputs)
        {
            if (output.Name.Contains("embeds", StringComparison.OrdinalIgnoreCase))
            {
                chosen = output;
                break;
            }
            if (output.Name.Equals("pooler_output", StringComparison.OrdinalIgnoreCase))
                chosen = output;
            else if (chosen is null)
                chosen = output;
        }
        if (chosen is null) throw new InvalidOperationException("模型没有可用的输出。");

        var tensorOut = chosen.AsTensor<float>();
        var dims = tensorOut.Dimensions.ToArray();
        var data = tensorOut.ToArray();

        float[] vector;
        if (dims.Length == 2)
        {
            vector = data;
        }
        else if (dims.Length == 3)
        {
            int tokens = dims[1];
            int dim = dims[2];
            vector = new float[dim];
            for (int t = 0; t < tokens; t++)
                for (int d = 0; d < dim; d++)
                    vector[d] += data[t * dim + d];
            for (int d = 0; d < dim; d++)
                vector[d] /= Math.Max(1, tokens);
        }
        else
        {
            vector = data;
        }

        var normalized = AiMath.L2Normalize(vector);
        Dimension = normalized.Length;
        return normalized;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }
}

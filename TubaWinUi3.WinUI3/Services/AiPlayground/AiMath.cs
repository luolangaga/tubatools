namespace TubaWinUi3.Services.AiPlayground;

/// <summary>推理数学工具（纯函数，可单测）：softmax / Top-K / NMS / 余弦相似度。</summary>
public static class AiMath
{
    public static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    public static float[] Softmax(float[] logits)
    {
        var result = new float[logits.Length];
        if (logits.Length == 0) return result;
        float max = logits[0];
        for (int i = 1; i < logits.Length; i++)
            if (logits[i] > max) max = logits[i];
        double sum = 0;
        for (int i = 0; i < logits.Length; i++)
        {
            double e = Math.Exp(logits[i] - max);
            result[i] = (float)e;
            sum += e;
        }
        if (sum <= 0) return result;
        for (int i = 0; i < result.Length; i++)
            result[i] = (float)(result[i] / sum);
        return result;
    }

    /// <summary>取分数最高的 K 项（降序）。</summary>
    public static (int Index, float Score)[] TopK(float[] scores, int k)
    {
        return scores
            .Select((score, index) => (Index: index, Score: score))
            .OrderByDescending(pair => pair.Score)
            .Take(Math.Max(0, k))
            .ToArray();
    }

    public static float[] L2Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var v in vector) sum += (double)v * v;
        var norm = Math.Sqrt(sum);
        if (norm <= 1e-12) return vector;
        var result = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++)
            result[i] = (float)(vector[i] / norm);
        return result;
    }

    /// <summary>余弦相似度（自动按未归一化向量处理）。</summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        int length = Math.Min(a.Length, b.Length);
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < length; i++)
        {
            dot += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }
        if (normA <= 1e-12 || normB <= 1e-12) return 0;
        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }

    /// <summary>IoU（xyxy 坐标）。</summary>
    public static float Iou(float[] a, float[] b)
    {
        float x1 = MathF.Max(a[0], b[0]);
        float y1 = MathF.Max(a[1], b[1]);
        float x2 = MathF.Min(a[2], b[2]);
        float y2 = MathF.Min(a[3], b[3]);
        float inter = MathF.Max(0, x2 - x1) * MathF.Max(0, y2 - y1);
        if (inter <= 0) return 0;
        float areaA = MathF.Max(0, a[2] - a[0]) * MathF.Max(0, a[3] - a[1]);
        float areaB = MathF.Max(0, b[2] - b[0]) * MathF.Max(0, b[3] - b[1]);
        var union = areaA + areaB - inter;
        return union <= 0 ? 0 : inter / union;
    }

    /// <summary>非极大值抑制，返回保留的索引（按分数降序）。</summary>
    public static List<int> Nms(
        IReadOnlyList<float[]> boxes,
        IReadOnlyList<float> scores,
        float iouThreshold,
        int maxOutput = 100)
    {
        var order = Enumerable.Range(0, scores.Count)
            .OrderByDescending(i => scores[i])
            .ToList();
        var kept = new List<int>();
        var suppressed = new bool[scores.Count];

        foreach (var index in order)
        {
            if (suppressed[index]) continue;
            kept.Add(index);
            if (kept.Count >= maxOutput) break;
            for (int j = 0; j < order.Count; j++)
            {
                var other = order[j];
                if (other == index || suppressed[other]) continue;
                if (Iou(boxes[index], boxes[other]) > iouThreshold)
                    suppressed[other] = true;
            }
        }
        return kept;
    }
}

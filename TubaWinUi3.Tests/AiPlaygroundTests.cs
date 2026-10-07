using System.Text;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiPlayground;
using Xunit;

namespace TubaWinUi3.Tests;

public class AiPlaygroundCatalogTests
{
    [Fact]
    public void Presets_AreCompleteAndUnique()
    {
        var presets = AiPlaygroundCatalog.All;
        Assert.NotEmpty(presets);
        Assert.Equal(presets.Count, presets.Select(p => p.Id).Distinct().Count());

        foreach (var preset in presets)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Id));
            Assert.False(string.IsNullOrWhiteSpace(preset.DisplayName));
            Assert.Contains('/', preset.Repo);
            Assert.False(string.IsNullOrWhiteSpace(preset.License));
            Assert.False(string.IsNullOrWhiteSpace(preset.ApproxSizeText));
            Assert.NotEmpty(preset.Files);
            Assert.False(string.IsNullOrWhiteSpace(preset.Family));

            var names = preset.Files.Select(f => f.TargetName).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(preset.Files, f => Assert.False(string.IsNullOrWhiteSpace(f.Path)));
        }
    }

    [Fact]
    public void ChatPresets_ContainGenAiConfigAndOnnx()
    {
        foreach (var preset in AiPlaygroundCatalog.All.Where(p => p.Task == AiTaskKind.Chat))
        {
            Assert.Contains(preset.Files, f => f.TargetName == "genai_config.json");
            Assert.Contains(preset.Files, f => f.TargetName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(preset.Files, f => f.TargetName == "tokenizer.json");
        }
    }

    [Fact]
    public void VisionPresets_HaveOnnxConfigAndPreprocessor()
    {
        foreach (var preset in AiPlaygroundCatalog.All.Where(p => p.Task != AiTaskKind.Chat))
        {
            Assert.Contains(preset.Files, f => f.TargetName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(preset.Files, f => f.TargetName == "config.json");
            Assert.Contains(preset.Files, f => f.TargetName == "preprocessor_config.json");
        }
    }

    [Fact]
    public void GetRepoPath_JoinsBasePath()
    {
        var preset = new AiModelPreset
        {
            Id = "t",
            Task = AiTaskKind.Chat,
            Family = "phi",
            Repo = "microsoft/Phi-3.5-mini-instruct-onnx",
            BasePath = "cpu_and_mobile/cpu-int4-awq-block-128-acc-level-4",
            DisplayName = "t",
            License = "MIT",
            ApproxSizeText = "约 1 MB",
            Files = [new AiModelFile("genai_config.json")],
        };
        Assert.Equal(
            "cpu_and_mobile/cpu-int4-awq-block-128-acc-level-4/genai_config.json",
            preset.GetRepoPath(preset.Files[0]));
    }

    [Fact]
    public void GetById_WorksAndReturnsNullForUnknown()
    {
        Assert.NotNull(AiPlaygroundCatalog.GetById("resnet50"));
        Assert.Null(AiPlaygroundCatalog.GetById("不存在"));
    }
}

public class HfModelDownloaderTests
{
    [Fact]
    public void HostOrder_MirrorFirstForAutoAndMirror()
    {
        Assert.Equal(HfModelDownloader.MirrorHost, HfModelDownloader.HostOrderFor("auto")[0]);
        Assert.Equal(HfModelDownloader.MirrorHost, HfModelDownloader.HostOrderFor("mirror")[0]);
        Assert.Equal(HfModelDownloader.OfficialHost, HfModelDownloader.HostOrderFor("official")[0]);
    }

    [Fact]
    public void BuildFileUrl_UsesBasePathAndEscapesSegments()
    {
        var preset = new AiModelPreset
        {
            Id = "t",
            Task = AiTaskKind.ImageClassification,
            Family = "resnet",
            Repo = "Xenova/resnet-50",
            BasePath = "onnx",
            DisplayName = "t",
            License = "Apache-2.0",
            ApproxSizeText = "约 1 MB",
            Files = [new AiModelFile("a b/model.onnx")],
        };
        var url = HfModelDownloader.BuildFileUrl(HfModelDownloader.MirrorHost, preset, preset.Files[0]);
        Assert.Equal("https://hf-mirror.com/Xenova/resnet-50/resolve/main/onnx/a%20b/model.onnx", url);
    }

    [Fact]
    public async Task ResolveFirstAvailable_TakesFirstSuccessWithoutWaitingForSlowProbe()
    {
        // 官方源被墙的场景：镜像秒回成功，另一个探测永远不完成——
        // 解析必须在拿到第一个成功结果后立即返回，而不是等挂掉的探测超时。
        var neverCompletes = new TaskCompletionSource<HfModelDownloader.ProbeResult>();
        var task = HfModelDownloader.ResolveFirstAvailableAsync(
            host => host == "fast.example"
                ? Task.FromResult(new HfModelDownloader.ProbeResult(
                    new ResolvedDownloadUrl("https://fast.example/f", "f", 3), false))
                : neverCompletes.Task,
            ["slow.example", "fast.example"],
            new HashSet<string>(),
            CancellationToken.None);

        var winner = await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("https://fast.example/f", winner!.Url);
    }

    [Fact]
    public async Task ResolveFirstAvailable_MarksNetworkFailedHostButNotFileLevelFailure()
    {
        var unreachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var winner = await HfModelDownloader.ResolveFirstAvailableAsync(
            host => Task.FromResult(host == "dead.example"
                ? new HfModelDownloader.ProbeResult(null, true)   // 超时/连不上
                : new HfModelDownloader.ProbeResult(null, false)), // 可达但没有此文件
            ["dead.example", "alive.example"],
            unreachable,
            CancellationToken.None);

        Assert.Null(winner);
        Assert.Contains("dead.example", unreachable);
        Assert.DoesNotContain("alive.example", unreachable);
    }

    [Fact]
    public async Task ResolveFirstAvailable_FallsBackWhenPrimaryLacksFile()
    {
        var unreachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var winner = await HfModelDownloader.ResolveFirstAvailableAsync(
            host => Task.FromResult(host == "hf-mirror.com"
                ? new HfModelDownloader.ProbeResult(null, false)
                : new HfModelDownloader.ProbeResult(
                    new ResolvedDownloadUrl("https://huggingface.co/f", "f", 1), false)),
            [HfModelDownloader.MirrorHost, HfModelDownloader.OfficialHost],
            unreachable,
            CancellationToken.None);

        Assert.Equal("https://huggingface.co/f", winner!.Url);
        Assert.Empty(unreachable);
    }

    [Fact]
    public async Task ResolveFirstAvailable_ThrowsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HfModelDownloader.ResolveFirstAvailableAsync(
                _ => new TaskCompletionSource<HfModelDownloader.ProbeResult>().Task,
                ["a.example"],
                new HashSet<string>(),
                cts.Token));
    }
}

public class AiModelLibraryHelperTests
{
    [Theory]
    [InlineData("约 2.4 GB", 2576980377L)]
    [InlineData("约 98 MB", 102760448L)]
    [InlineData("约 25 MB", 26214400L)]
    public void ParseApproxBytes_ParsesSizes(string text, long expected)
    {
        Assert.Equal(expected, AiModelLibrary.ParseApproxBytes(text));
    }

    [Fact]
    public void ParseApproxBytes_ReturnsZeroForUnknown()
    {
        Assert.Equal(0, AiModelLibrary.ParseApproxBytes("未知"));
    }
}

public class DetectionDecodeTests
{
    private static DetectionOptions Opt(float threshold, float iou) =>
        new DetectionOptions { Confidence = threshold, Iou = iou, ClassAwareNms = false, MaxDetections = 100 }.Normalized();

    [Fact]
    public void Detr_DecodesSkipsNoObjectAndScalesCoordinates()
    {
        // 2 个查询 × 3 类（0 = N/A）；query0 最佳有效类为 cat(-1)、query1 为 dog(2)
        var logits = new float[] { 10f, -1f, -2f, -5f, -3f, 2f };
        var logitsDims = new[] { 1, 2, 3 };
        var boxes = new float[] { 0.5f, 0.5f, 0.2f, 0.2f, 0.25f, 0.25f, 0.5f, 0.5f };
        var labels = new[] { "N/A", "cat", "dog" };

        var items = DetectionDecode.Detr(
            logits, logitsDims, boxes, labels, Array.Empty<string>(),
            imageWidth: 100, imageHeight: 200, options: Opt(0.2f, 0.45f));

        Assert.Equal(2, items.Count);
        Assert.Equal("狗", items[0].Label);
        Assert.Equal("猫", items[1].Label);
        Assert.Equal(1.0 / (1 + Math.Exp(-2)), items[0].Score, 3);
        Assert.Equal(1.0 / (1 + Math.Exp(1)), items[1].Score, 3);
        Assert.Equal(40, items[1].X1, 3);
        Assert.Equal(80, items[1].Y1, 3);
        Assert.Equal(60, items[1].X2, 3);
        Assert.Equal(120, items[1].Y2, 3);
    }

    [Fact]
    public void Detr_NmsSuppressesDuplicates()
    {
        var logits = new float[] { 0f, -5f, -0.1f, -5f };
        var logitsDims = new[] { 1, 2, 2 };
        var boxes = new float[] { 0.5f, 0.5f, 0.2f, 0.2f, 0.5f, 0.5f, 0.2f, 0.2f };
        var labels = new[] { "cat", "dog" };

        var items = DetectionDecode.Detr(
            logits, logitsDims, boxes, labels, Array.Empty<string>(),
            imageWidth: 100, imageHeight: 100, options: Opt(0.3f, 0.45f));

        Assert.Single(items);
        Assert.Equal(0.5f, items[0].Score, 3);
    }

    [Fact]
    public void Yolo_DecodesAttrMajorLayout()
    {
        var (data, dims) = BuildYoloTensor(transposed: false, classScore: 0.9f);
        var items = DetectionDecode.Yolo(data, dims, new[] { "cat", "dog" },
            imageWidth: 1280, imageHeight: 720, inputSize: 640, options: Opt(0.3f, 0.45f));

        Assert.Single(items);
        Assert.Equal("狗", items[0].Label);
        Assert.Equal(1, items[0].ClassId);
        Assert.Equal(576, items[0].X1, 3);
        Assert.Equal(324, items[0].Y1, 3);
        Assert.Equal(704, items[0].X2, 3);
        Assert.Equal(396, items[0].Y2, 3);
    }

    [Fact]
    public void Yolo_DecodesAnchorMajorLayout()
    {
        var (data, dims) = BuildYoloTensor(transposed: true, classScore: 0.9f);
        var items = DetectionDecode.Yolo(data, dims, new[] { "cat", "dog" },
            imageWidth: 1280, imageHeight: 720, inputSize: 640, options: Opt(0.3f, 0.45f));

        Assert.Single(items);
        Assert.Equal("狗", items[0].Label);
        Assert.Equal(576, items[0].X1, 3);
    }

    [Fact]
    public void Yolo_AppliesSigmoidWhenLogitsAreNegative()
    {
        var (data, dims) = BuildYoloTensor(transposed: false, classScore: -0.5f, backgroundScore: -10f);
        var items = DetectionDecode.Yolo(data, dims, new[] { "cat", "dog" },
            imageWidth: 640, imageHeight: 640, inputSize: 640, options: Opt(0.3f, 0.45f));

        // 存在负值 → 判定为 logits，全部类别分数补 sigmoid：
        // cat(0.1) → 0.525（最高）、dog(-0.5) → 0.378；不加 sigmoid 则两者都低于阈值。
        Assert.Single(items);
        Assert.Equal("猫", items[0].Label);
        Assert.Equal(AiMath.Sigmoid(0.1f), items[0].Score, 4);
    }

    [Fact]
    public void Yolo_FallsBackToClassIndexWithoutLabels()
    {
        var (data, dims) = BuildYoloTensor(transposed: false, classScore: 0.9f);
        var items = DetectionDecode.Yolo(data, dims, Array.Empty<string>(),
            imageWidth: 640, imageHeight: 640, inputSize: 640, options: Opt(0.3f, 0.45f));

        Assert.Single(items);
        Assert.Equal("class 1", items[0].Label);
        Assert.Equal(1, items[0].ClassId);
    }

    [Fact]
    public void Yolo_DecodesNmsBuiltInOutput()
    {
        // Xenova/yolov9-c 实测输出：NMS 已在图内完成，形状 [N,6]
        // 每行 = x1,y1,x2,y2,score,class_id；坐标在 inputSize(640) 空间
        var output = new float[]
        {
            320f, 320f, 384f, 384f, 0.95f, 16f,   // dog (class 16)
            10f,  10f,  50f,  50f, 0.10f, 0f,     // 低于阈值，应被丢弃
        };
        var dims = new[] { 2, 6 };

        var items = DetectionDecode.Yolo(output, dims, AiPlaygroundLabels.LoadCocoLabels(),
            imageWidth: 1280, imageHeight: 720, inputSize: 640, options: Opt(0.3f, 0.45f));

        Assert.Single(items);
        Assert.Equal("狗", items[0].Label);          // class 16 = dog
        Assert.Equal(16, items[0].ClassId);
        Assert.Equal("dog", items[0].RawLabel);
        Assert.Equal(0.95f, items[0].Score, 3);
        // 640 空间坐标等比放大到 1280x720
        Assert.Equal(640, items[0].X1, 3);
        Assert.Equal(360, items[0].Y1, 3);
        Assert.Equal(768, items[0].X2, 3);
        Assert.Equal(432, items[0].Y2, 3);
    }

    [Fact]
    public void Yolo_NmsOutputIsEmptyWhenNoDetection()
    {
        // 模型返回 0 行时不应抛异常（曾报「无法识别 YOLO 输出形状:[0,6]」）
        var items = DetectionDecode.Yolo(Array.Empty<float>(), new[] { 0, 6 }, AiPlaygroundLabels.LoadCocoLabels(),
            imageWidth: 1280, imageHeight: 720, inputSize: 640, options: Opt(0.3f, 0.45f));
        Assert.Empty(items);
    }

    [Fact]
    public void ClassAwareNms_DoesNotSuppressAcrossClasses()
    {
        // 两个完全重叠的框、不同类别：类别内 NMS 应都保留；类别无关 NMS 只留一个。
        var items = new List<DetectionItem>
        {
            new() { Label = "人", RawLabel = "person", ClassId = 0, Score = 0.9f, X1 = 0, Y1 = 0, X2 = 100, Y2 = 100 },
            new() { Label = "车", RawLabel = "car", ClassId = 2, Score = 0.8f, X1 = 0, Y1 = 0, X2 = 100, Y2 = 100 },
        };

        var classAware = DetectionDecode.NmsAndSort(items,
            new DetectionOptions { Iou = 0.45f, ClassAwareNms = true, MaxDetections = 100 }.Normalized());
        Assert.Equal(2, classAware.Count);

        var classAgnostic = DetectionDecode.NmsAndSort(items,
            new DetectionOptions { Iou = 0.45f, ClassAwareNms = false, MaxDetections = 100 }.Normalized());
        Assert.Single(classAgnostic);
    }

    [Fact]
    public void MaxDetections_IsHonored()
    {
        var items = Enumerable.Range(0, 20).Select(i => new DetectionItem
        {
            Label = "人", RawLabel = "person", ClassId = 0,
            Score = 0.5f + i * 0.01f,
            X1 = i * 50, Y1 = 0, X2 = i * 50 + 10, Y2 = 10,   // 互不重叠
        }).ToList();

        var kept = DetectionDecode.NmsAndSort(items,
            new DetectionOptions { Iou = 0.45f, ClassAwareNms = true, MaxDetections = 5 }.Normalized());
        Assert.Equal(5, kept.Count);
    }

    private const int Attrs = 6;
    private const int Anchors = 10;

    private static (float[] Data, int[] Dims) BuildYoloTensor(bool transposed, float classScore, float backgroundScore = 0f)
    {
        var dims = transposed ? new[] { 1, Anchors, Attrs } : new[] { 1, Attrs, Anchors };
        var data = new float[Attrs * Anchors];
        void Set(int attr, int anchor, float value)
        {
            if (transposed)
                data[anchor * Attrs + attr] = value;
            else
                data[attr * Anchors + anchor] = value;
        }
        Set(0, 0, 320f);
        Set(1, 0, 320f);
        Set(2, 0, 64f);
        Set(3, 0, 64f);
        Set(4, 0, 0.1f);       // cat
        Set(5, 0, classScore); // dog
        for (int anchor = 1; anchor < Anchors; anchor++)
        {
            Set(4, anchor, backgroundScore);
            Set(5, anchor, backgroundScore);
        }
        return (data, dims);
    }
}

public class AiMathTests
{
    [Fact]
    public void Softmax_NormalizesAndHandlesLargeValues()
    {
        var result = AiMath.Softmax(new[] { 1000f, 1000f, 1000f });
        Assert.Equal(1.0, result.Sum(), 6);
        Assert.Equal(1f / 3, result[0], 6);
    }

    [Fact]
    public void TopK_ReturnsDescendingTopItems()
    {
        var top = AiMath.TopK(new[] { 0.1f, 0.9f, 0.5f }, 2);
        Assert.Equal(2, top.Length);
        Assert.Equal(1, top[0].Index);
        Assert.Equal(2, top[1].Index);
    }

    [Fact]
    public void Nms_SuppressesOverlappingBoxes()
    {
        var boxes = new List<float[]>
        {
            new[] { 0f, 0f, 10f, 10f },
            new[] { 1f, 1f, 11f, 11f },
            new[] { 100f, 100f, 110f, 110f },
        };
        var kept = AiMath.Nms(boxes, new List<float> { 0.9f, 0.8f, 0.7f }, 0.45f);
        Assert.Equal(new[] { 0, 2 }, kept);
    }

    [Fact]
    public void CosineSimilarity_And_L2Normalize()
    {
        var a = AiMath.L2Normalize(new[] { 3f, 4f });
        Assert.Equal(0.6f, a[0], 5);
        Assert.Equal(0.8f, a[1], 5);
        Assert.Equal(1f, AiMath.CosineSimilarity(new[] { 1f, 0f }, new[] { 1f, 0f }), 5);
        Assert.Equal(0f, AiMath.CosineSimilarity(new[] { 1f, 0f }, new[] { 0f, 1f }), 5);
    }

    [Fact]
    public void Iou_ComputesOverlap()
    {
        // [0,0,2,2] 与 [1,1,3,3]：交集 1，并集 7
        Assert.Equal(1f / 7, AiMath.Iou(new[] { 0f, 0f, 2f, 2f }, new[] { 1f, 1f, 3f, 3f }), 5);
    }
}

public class ImagePreprocessTests
{
    [Fact]
    public void PrepareYolo_UsesZeroToOneRange()
    {
        // 实测（Xenova/yolov9-c + 官方 city-streets.jpg）：必须 0..1；喂 0..255 会得到数千个垃圾框
        var path = CreateTempImage(40, 20, System.Drawing.Color.White);
        try
        {
            var prepared = ImagePreprocess.PrepareYolo(path, 64);
            Assert.True(prepared.Data.All(v => v >= 0f && v <= 1f));
            Assert.Equal(1f, prepared.Data[0], 3);   // 纯白图 → R 通道 1.0
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PrepareYolo_ReturnsSquareChwTensor()
    {
        var path = CreateTempImage(40, 20);
        try
        {
            var prepared = ImagePreprocess.PrepareYolo(path, 64);
            Assert.Equal(64, prepared.Width);
            Assert.Equal(64, prepared.Height);
            Assert.Equal(3 * 64 * 64, prepared.Data.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PrepareClassification_UsesTimmCrop()
    {
        var path = CreateTempImage(40, 20);
        try
        {
            var prepared = ImagePreprocess.PrepareClassification(path, 224, timmCrop: true);
            Assert.Equal(224, prepared.Width);
            Assert.Equal(224, prepared.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PrepareDetr_CapsLongestEdge()
    {
        var path = CreateTempImage(400, 100);
        try
        {
            var prepared = ImagePreprocess.PrepareDetr(path, 800, 1333);
            Assert.Equal(1333, prepared.Width);
            Assert.Equal(333, prepared.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempImage(
        int width, int height, System.Drawing.Color? color = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tuba_ai_test_{Guid.NewGuid():N}.png");
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(color ?? System.Drawing.Color.Orange);
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }
}

public class AiPlaygroundLabelTests
{
    [Fact]
    public void LoadCocoLabels_Has80StandardClasses()
    {
        var labels = AiPlaygroundLabels.LoadCocoLabels();
        Assert.Equal(80, labels.Length);
        Assert.Equal("person", labels[0]);
        Assert.Equal("toothbrush", labels[^1]);
    }

    [Fact]
    public void Translate_KnownEnglishToChinese_UnknownPassThrough()
    {
        Assert.Equal("人", AiPlaygroundLabels.Translate("person"));
        Assert.Equal("狗", AiPlaygroundLabels.Translate("dog"));
        Assert.Equal("unknown-thing", AiPlaygroundLabels.Translate("unknown-thing"));
    }

    [Fact]
    public void Shorten_TrimsScientificName()
    {
        Assert.Equal("tench", AiPlaygroundLabels.Shorten("tench, Tinca tinca"));
        Assert.Equal("cat", AiPlaygroundLabels.Shorten("cat"));
    }

    [Fact]
    public void LoadId2Label_ReadsConfigJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tuba_ai_cfg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "config.json"),
                """{ "id2label": { "0": "N/A", "1": "person", "2": "bicycle" } }""");
            var labels = AiPlaygroundLabels.LoadId2Label(dir);
            Assert.Equal(3, labels.Length);
            Assert.Equal("person", labels[1]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class LlmPromptTests
{
    [Fact]
    public void PhiPrompt_UsesOfficialMarkers()
    {
        var prompt = LlmPromptBuilder.BuildPrompt(
            AiPromptFormat.Phi,
            "sys",
            new List<(string, string)> { ("user", "hello"), ("assistant", "hi") });
        Assert.Contains("<|system|>\nsys<|end|>", prompt);
        Assert.Contains("<|user|>\nhello<|end|>", prompt);
        Assert.Contains("<|assistant|>\nhi<|end|>", prompt);
        Assert.EndsWith("<|assistant|>\n", prompt);
    }

    [Fact]
    public void ChatMlPrompt_UsesImStartMarkers()
    {
        var prompt = LlmPromptBuilder.BuildPrompt(
            AiPromptFormat.ChatML,
            "sys",
            new List<(string, string)> { ("user", "hello") });
        Assert.Contains("<|im_start|>system\nsys<|im_end|>", prompt);
        Assert.Contains("<|im_start|>user\nhello<|im_end|>", prompt);
        Assert.EndsWith("<|im_start|>assistant\n", prompt);
    }

    [Fact]
    public void FindStopMarker_FindsMarkerOrReturnsMinusOne()
    {
        Assert.Equal(3, LlmPromptBuilder.FindStopMarker(new StringBuilder("abc<|end|>def")));
        Assert.Equal(-1, LlmPromptBuilder.FindStopMarker(new StringBuilder("plain text")));
    }
}

/// <summary>注册测试：与 BuiltinToolRegistryTests 共享集合，避免反射清空注册表时并行冲突。</summary>
[Collection("BuiltinToolRegistry")]
public class AiPlaygroundRegistrationTests
{
    private static void ClearRegistry()
    {
        var list = (List<IBuiltinTool>)typeof(BuiltinToolRegistry)
            .GetField("_tools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        list.Clear();
    }

    [Fact]
    public void RegisterDefaults_ContainsAiPlayground()
    {
        ClearRegistry();
        BuiltinToolRegistry.RegisterDefaults();
        var tool = BuiltinToolRegistry.GetById("ai-playground");
        Assert.NotNull(tool);
        Assert.Equal("本地AI试炼场", tool.Name);
        Assert.Equal("实用工具", tool.Category);
        Assert.Equal(BuiltinToolKind.InstantAction, tool.Kind);
    }
}

public class AiChatSessionStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tuba_ai_chat_{Guid.NewGuid():N}");

    public AiChatSessionStoreTests() => AiChatSessionStore.RootOverride = _root;

    public void Dispose()
    {
        AiChatSessionStore.RootOverride = null;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static AiChatSession SessionWithUser(string text)
    {
        var session = AiChatSessionStore.CreateSession("m1");
        session.Messages.Add(new AiPlaygroundChatMessage { Role = "user", Text = text });
        return session;
    }

    [Fact]
    public void SaveThenList_RoundTripsAndSortsByRecent()
    {
        var first = SessionWithUser("第一条");
        first.Messages.Add(new AiPlaygroundChatMessage { Role = "assistant", Text = "回复" });
        AiChatSessionStore.SaveSession("m1", first);

        var second = SessionWithUser("第二条");
        AiChatSessionStore.SaveSession("m1", second);

        var list = AiChatSessionStore.ListSessions("m1");
        Assert.Equal(2, list.Count);
        Assert.Equal(second.Id, list[0].Id);              // 最近更新在前
        Assert.Equal(2, list[1].Messages.Count);
        Assert.Equal("user", list[1].Messages[0].Role);
        Assert.Equal("回复", list[1].Messages[1].Text);
    }

    [Fact]
    public void SaveSession_AutoGeneratesTitleFromFirstUserMessage()
    {
        var session = SessionWithUser("帮我写一个快速排序的 C# 实现并详细解释它的时间复杂度和空间复杂度");
        AiChatSessionStore.SaveSession("m1", session);
        var loaded = AiChatSessionStore.ListSessions("m1").Single();
        Assert.StartsWith("帮我写一个快速排序", loaded.Title);
        Assert.EndsWith("…", loaded.Title);
        Assert.Equal(21, loaded.Title.Length); // 20 字 + 省略号
    }

    [Fact]
    public void SaveSession_ShortTitleNotTruncated()
    {
        var session = SessionWithUser("你好");
        AiChatSessionStore.SaveSession("m1", session);
        Assert.Equal("你好", AiChatSessionStore.ListSessions("m1").Single().Title);
    }

    [Fact]
    public void SaveSession_KeepsCustomTitle()
    {
        var session = SessionWithUser("hello");
        session.Title = "我的标题";
        AiChatSessionStore.SaveSession("m1", session);
        Assert.Equal("我的标题", AiChatSessionStore.ListSessions("m1").Single().Title);
    }

    [Fact]
    public void EmptySession_IsNotPersisted()
    {
        var session = AiChatSessionStore.CreateSession("m1");
        AiChatSessionStore.SaveSession("m1", session);
        Assert.Empty(AiChatSessionStore.ListSessions("m1"));
    }

    [Fact]
    public void SessionsAreIsolatedPerModel()
    {
        AiChatSessionStore.SaveSession("m1", SessionWithUser("one"));
        AiChatSessionStore.SaveSession("m2", SessionWithUser("two"));
        Assert.Single(AiChatSessionStore.ListSessions("m1"));
        Assert.Single(AiChatSessionStore.ListSessions("m2"));
        Assert.Equal("one", AiChatSessionStore.ListSessions("m1").Single().Messages[0].Text);
    }

    [Fact]
    public void DeleteSession_RemovesOnlyTarget()
    {
        var keep = SessionWithUser("keep");
        var remove = SessionWithUser("remove");
        AiChatSessionStore.SaveSession("m1", keep);
        AiChatSessionStore.SaveSession("m1", remove);

        AiChatSessionStore.DeleteSession("m1", remove.Id);

        var list = AiChatSessionStore.ListSessions("m1");
        Assert.Single(list);
        Assert.Equal(keep.Id, list[0].Id);
    }

    [Fact]
    public void ListSessions_ToleratesCorruptFile()
    {
        AiChatSessionStore.SaveSession("m1", SessionWithUser("ok"));
        File.WriteAllText(Path.Combine(_root, "m1", "broken.json"), "{ not json");
        Assert.Single(AiChatSessionStore.ListSessions("m1"));
    }
}

public class AiLlmTargetTests
{
    [Fact]
    public void ResolveLlmTargetCore_AutoPrefersQnnNpu()
    {
        var (provider, deviceType, _) = AiRuntimeService.ResolveLlmTargetCore(
            AiEngineMode.Auto, [("QNNExecutionProvider", "NPU"), ("DmlExecutionProvider", "GPU")]);
        Assert.Equal("QNNExecutionProvider", provider);
        Assert.Equal("NPU", deviceType);
    }

    [Fact]
    public void ResolveLlmTargetCore_IntelMachineFallsBackToCpu()
    {
        // Intel 场景实测（2026-10）：DML 控制流报错、OpenVINO NPU 编译不过动态序列、
        // OpenVINO GPU 生成首 token 即崩（allocator）→ Auto 一律回退 CPU。
        var (provider, deviceType, note) = AiRuntimeService.ResolveLlmTargetCore(
            AiEngineMode.Auto,
            [("OpenVINOExecutionProvider", "NPU"), ("OpenVINOExecutionProvider", "GPU"), ("DmlExecutionProvider", "GPU"), ("CPUExecutionProvider", "CPU")]);
        Assert.Null(provider);
        Assert.Null(deviceType);
        Assert.Contains("CPU", note);
    }

    [Fact]
    public void ResolveLlmTargetCore_AutoFallsBackToCpuWhenNoUsableAccelerator()
    {
        // 只有 DirectML（LLM 不可用）与 CPU → 返回 null（GenAI 默认 CPU），并如实标注
        var (provider, deviceType, note) = AiRuntimeService.ResolveLlmTargetCore(
            AiEngineMode.Auto, [("CPUExecutionProvider", "CPU"), ("DmlExecutionProvider", "GPU")]);
        Assert.Null(provider);
        Assert.Null(deviceType);
        Assert.Contains("CPU", note);
    }

    [Fact]
    public void ResolveLlmTargetCore_GpuModeRejectsDirectMl()
    {
        var (provider, _, note) = AiRuntimeService.ResolveLlmTargetCore(
            AiEngineMode.Gpu, [("DmlExecutionProvider", "GPU")]);
        Assert.Null(provider);
        Assert.Contains("CPU", note);
    }

    [Fact]
    public void ResolveLlmTargetCore_CpuModeUsesGenAiDefault()
    {
        var (provider, _, note) = AiRuntimeService.ResolveLlmTargetCore(
            AiEngineMode.Cpu, [("QNNExecutionProvider", "NPU")]);
        Assert.Null(provider);
        Assert.Contains("CPU", note);
    }

    [Fact]
    public void ResolveLlmTargetCore_NpuModeRejectsNonQnnNpu()
    {
        var (provider, _, note) = AiRuntimeService.ResolveLlmTargetCore(
            AiEngineMode.Npu, [("OpenVINOExecutionProvider", "NPU")]);
        Assert.Null(provider);
        Assert.Contains("CPU", note);
    }

    [Fact]
    public void RequiresProbe_OnlyForNonQnnNpu()
    {
        // 非 QNN 的 NPU 必须先经子进程探测
        Assert.True(AiRuntimeService.RequiresProbe("OpenVINOExecutionProvider", "NPU"));
        // QNN 走自身 EP 上下文编译，无需探测
        Assert.False(AiRuntimeService.RequiresProbe("QNNExecutionProvider", "NPU"));
        // 非 NPU 设备无需探测
        Assert.False(AiRuntimeService.RequiresProbe("DmlExecutionProvider", "GPU"));
        Assert.False(AiRuntimeService.RequiresProbe("CPUExecutionProvider", "CPU"));
    }

    [Fact]
    public void GetPolicyFor_NeverSelectsNpu()
    {
        // 策略路径永远不选 NPU（NPU 只能走显式设备 + 子进程探测这一条门）：
        // YOLO 闪退事故（0xC0000005 in openvino_intel_npu_compiler.dll）的根因就是
        // 探测失败回 null 后，策略 PREFER_NPU 又把 NPU 挂了回来。
        Assert.Equal(
            Microsoft.ML.OnnxRuntime.ExecutionProviderDevicePolicy.PREFER_GPU,
            AiRuntimeService.GetPolicyFor(AiEngineMode.Npu, ["NPU", "GPU"]));
        Assert.Equal(
            Microsoft.ML.OnnxRuntime.ExecutionProviderDevicePolicy.PREFER_CPU,
            AiRuntimeService.GetPolicyFor(AiEngineMode.Npu, ["NPU"]));
        Assert.Equal(
            Microsoft.ML.OnnxRuntime.ExecutionProviderDevicePolicy.PREFER_GPU,
            AiRuntimeService.GetPolicyFor(AiEngineMode.Auto, ["NPU", "GPU"]));
        Assert.Equal(
            Microsoft.ML.OnnxRuntime.ExecutionProviderDevicePolicy.PREFER_CPU,
            AiRuntimeService.GetPolicyFor(AiEngineMode.Auto, ["NPU"]));
        Assert.Equal(
            Microsoft.ML.OnnxRuntime.ExecutionProviderDevicePolicy.PREFER_CPU,
            AiRuntimeService.GetPolicyFor(AiEngineMode.Cpu, ["NPU", "GPU"]));
    }

    [Fact]
    public void ResolveVisionTargetCore_NpuModePicksExplicitNpuNeedingProbe()
    {
        // Intel 场景：OpenVINO NPU（非 AUTO）→ 显式选择且需探测
        var (ep, type, probe) = AiRuntimeService.ResolveVisionTargetCore(
            AiEngineMode.Npu, [("OpenVINOExecutionProvider", "NPU"), ("DmlExecutionProvider", "GPU")]);
        Assert.Equal("OpenVINOExecutionProvider", ep);
        Assert.Equal("NPU", type);
        Assert.True(probe);
    }

    [Fact]
    public void ResolveVisionTargetCore_AutoPrefersDmlGpuAndExcludesAuto()
    {
        // Auto：GPU 优先 DirectML；OpenVINO AUTO 设备一律排除（内部会路由 NPU）
        var (ep, type, probe) = AiRuntimeService.ResolveVisionTargetCore(
            AiEngineMode.Auto,
            [("OpenVINOExecutionProvider", "NPU"), ("OpenVINOExecutionProvider.AUTO", "NPU"),
             ("DmlExecutionProvider", "GPU"), ("CPUExecutionProvider", "CPU")]);
        Assert.Equal("DmlExecutionProvider", ep);
        Assert.Equal("GPU", type);
        Assert.False(probe);
    }

    [Fact]
    public void ResolveVisionTargetCore_QnnNpuNeedsNoProbe()
    {
        var (ep, type, probe) = AiRuntimeService.ResolveVisionTargetCore(
            AiEngineMode.Npu, [("QNNExecutionProvider", "NPU"), ("CPUExecutionProvider", "CPU")]);
        Assert.Equal("QNNExecutionProvider", ep);
        Assert.Equal("NPU", type);
        Assert.False(probe);
    }

    [Fact]
    public void RequiresProbe_MarksNonQnnNpuForProbing()
    {
        // YOLO 事故：探测假阳性（只建会话 / 在 CPU 上验证）会让主进程挂 NPU 时 0xC0000005。
        // 非 QNN 的 NPU 必须先经「子进程编译 + 真实推理 + 目标设备已注册」三重校验。
        Assert.True(AiRuntimeService.RequiresProbe("OpenVINOExecutionProvider", "NPU"));
        Assert.False(AiRuntimeService.RequiresProbe("QNNExecutionProvider", "NPU"));
        Assert.False(AiRuntimeService.RequiresProbe("DmlExecutionProvider", "GPU"));
    }

    [Fact]
    public void ResolveVisionTargetCore_CpuModePicksCpu()
    {
        var (ep, type, probe) = AiRuntimeService.ResolveVisionTargetCore(
            AiEngineMode.Cpu, [("DmlExecutionProvider", "GPU"), ("CPUExecutionProvider", "CPU")]);
        Assert.Equal("CPUExecutionProvider", ep);
        Assert.Equal("CPU", type);
        Assert.False(probe);
    }
}

public class AiEpProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tuba_ep_probe_{Guid.NewGuid():N}");

    public AiEpProbeTests() => AiEpProbe.RootOverride = _root;

    public void Dispose()
    {
        AiEpProbe.RootOverride = null;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void StoreThenGetCached_RoundTrips()
    {
        Assert.Null(AiEpProbe.GetCached("m.onnx", "OpenVINOExecutionProvider", "NPU", 224, 224));

        AiEpProbe.Store("m.onnx", "OpenVINOExecutionProvider", "NPU", 224, 224,
            new AiEpProbeResult { Ok = true, CheckedAt = DateTime.Now });

        var cached = AiEpProbe.GetCached("m.onnx", "OpenVINOExecutionProvider", "NPU", 224, 224);
        Assert.NotNull(cached);
        Assert.True(cached!.Ok);
    }

    [Fact]
    public void CacheKeyDistinguishesShapeAndDevice()
    {
        AiEpProbe.Store("m.onnx", "EP", "NPU", 224, 224, new AiEpProbeResult { Ok = true });
        Assert.Null(AiEpProbe.GetCached("m.onnx", "EP", "NPU", 640, 640));
        Assert.Null(AiEpProbe.GetCached("m.onnx", "EP", "GPU", 224, 224));
        Assert.Null(AiEpProbe.GetCached("other.onnx", "EP", "NPU", 224, 224));
    }

    [Fact]
    public void Clear_RemovesCachedEntries()
    {
        AiEpProbe.Store("m.onnx", "EP", "NPU", 224, 224, new AiEpProbeResult { Ok = true });
        AiEpProbe.Clear();
        Assert.Null(AiEpProbe.GetCached("m.onnx", "EP", "NPU", 224, 224));
    }

    [Fact]
    public void FailureResult_PersistsErrorText()
    {
        AiEpProbe.Store("m.onnx", "OpenVINOExecutionProvider", "NPU", 800, 1333,
            new AiEpProbeResult { Ok = false, Error = "探测子进程异常退出（0xC0000409）" });
        var cached = AiEpProbe.GetCached("m.onnx", "OpenVINOExecutionProvider", "NPU", 800, 1333);
        Assert.NotNull(cached);
        Assert.False(cached!.Ok);
        Assert.Contains("0xC0000409", cached.Error);
    }
}

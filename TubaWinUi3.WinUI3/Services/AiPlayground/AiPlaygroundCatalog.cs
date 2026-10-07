namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// 内置精选模型目录。所有仓库均已在 Hugging Face 验证存在；
/// 文件清单与预处理参数（归一化/尺寸）按仓库内的 preprocessor_config.json 核对过。
/// 新增模型：加一条 preset 即可（Runner 按 Task + Family 自动选择管线）。
/// </summary>
public static class AiPlaygroundCatalog
{
    private static IReadOnlyList<AiModelPreset>? _all;

    public static IReadOnlyList<AiModelPreset> All => _all ??= Build();

    public static AiModelPreset? GetById(string id) => All.FirstOrDefault(p => p.Id == id);

    private static IReadOnlyList<AiModelPreset> Build() =>
    [
        // ── 对话（LLM，ORT GenAI / genai_config.json 驱动）──────────────
        new AiModelPreset
        {
            Id = "phi35-mini-int4-cpu",
            Task = AiTaskKind.Chat,
            Family = "phi",
            Repo = "microsoft/Phi-3.5-mini-instruct-onnx",
            BasePath = "cpu_and_mobile/cpu-int4-awq-block-128-acc-level-4",
            DisplayName = "Phi-3.5-mini-instruct · INT4 · CPU/移动版",
            TagKey = "AiPlayground_Tag_NpuCpu",
            License = "MIT",
            ApproxSizeText = "约 2.4 GB",
            PromptFormat = AiPromptFormat.Phi,
            Files =
            [
                new("genai_config.json"),
                new("config.json"),
                new("special_tokens_map.json"),
                new("tokenizer.json"),
                new("tokenizer_config.json"),
                new("phi-3.5-mini-instruct-cpu-int4-awq-block-128-acc-level-4.onnx"),
                new("phi-3.5-mini-instruct-cpu-int4-awq-block-128-acc-level-4.onnx.data"),
            ],
        },
        new AiModelPreset
        {
            Id = "phi35-mini-int4-gpu",
            Task = AiTaskKind.Chat,
            Family = "phi",
            Repo = "microsoft/Phi-3.5-mini-instruct-onnx",
            BasePath = "gpu/gpu-int4-awq-block-128",
            DisplayName = "Phi-3.5-mini-instruct · INT4 · GPU 版",
            TagKey = "AiPlayground_Tag_Gpu",
            License = "MIT",
            ApproxSizeText = "约 2.5 GB",
            PromptFormat = AiPromptFormat.Phi,
            Files =
            [
                new("genai_config.json"),
                new("config.json"),
                new("chat_template.jinja"),
                new("special_tokens_map.json"),
                new("tokenizer.json"),
                new("tokenizer_config.json"),
                new("model.onnx"),
                new("model.onnx.data"),
            ],
        },
        new AiModelPreset
        {
            Id = "phi4-mini-int4-cpu",
            Task = AiTaskKind.Chat,
            Family = "phi",
            Repo = "microsoft/Phi-4-mini-instruct-onnx",
            BasePath = "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4",
            DisplayName = "Phi-4-mini-instruct · INT4 · CPU/移动版",
            TagKey = "AiPlayground_Tag_NpuCpu",
            License = "MIT",
            ApproxSizeText = "约 2.4 GB",
            PromptFormat = AiPromptFormat.Phi,
            Files =
            [
                new("genai_config.json"),
                new("config.json"),
                new("added_tokens.json"),
                new("merges.txt"),
                new("vocab.json"),
                new("special_tokens_map.json"),
                new("tokenizer.json"),
                new("tokenizer_config.json"),
                new("model.onnx"),
                new("model.onnx.data"),
            ],
        },
        new AiModelPreset
        {
            Id = "phi4-mini-int4-gpu",
            Task = AiTaskKind.Chat,
            Family = "phi",
            Repo = "microsoft/Phi-4-mini-instruct-onnx",
            BasePath = "gpu/gpu-int4-rtn-block-32",
            DisplayName = "Phi-4-mini-instruct · INT4 · GPU 版",
            TagKey = "AiPlayground_Tag_Gpu",
            License = "MIT",
            ApproxSizeText = "约 2.4 GB",
            PromptFormat = AiPromptFormat.Phi,
            Files =
            [
                new("genai_config.json"),
                new("config.json"),
                new("added_tokens.json"),
                new("merges.txt"),
                new("vocab.json"),
                new("special_tokens_map.json"),
                new("tokenizer.json"),
                new("tokenizer_config.json"),
                new("model.onnx"),
                new("model.onnx.data"),
            ],
        },

        // ── 图像分类 ───────────────────────────────────────────────
        new AiModelPreset
        {
            Id = "resnet50",
            Task = AiTaskKind.ImageClassification,
            Family = "resnet",
            Repo = "Xenova/resnet-50",
            DisplayName = "ResNet-50",
            License = "Apache-2.0",
            ApproxSizeText = "约 98 MB",
            Files =
            [
                new("onnx/model.onnx", "model.onnx"),
                new("preprocessor_config.json"),
                new("config.json"),
            ],
        },
        new AiModelPreset
        {
            Id = "resnet50-q",
            Task = AiTaskKind.ImageClassification,
            Family = "resnet",
            Repo = "Xenova/resnet-50",
            DisplayName = "ResNet-50 · 量化",
            TagKey = "AiPlayground_Tag_Quantized",
            License = "Apache-2.0",
            ApproxSizeText = "约 25 MB",
            Files =
            [
                new("onnx/model_quantized.onnx", "model_quantized.onnx"),
                new("preprocessor_config.json"),
                new("config.json"),
            ],
        },

        // ── 目标检测 ───────────────────────────────────────────────
        new AiModelPreset
        {
            Id = "detr-resnet50",
            Task = AiTaskKind.ObjectDetection,
            Family = "detr",
            Repo = "Xenova/detr-resnet-50",
            DisplayName = "DETR ResNet-50",
            License = "Apache-2.0",
            ApproxSizeText = "约 160 MB",
            Files =
            [
                new("onnx/model.onnx", "model.onnx"),
                new("preprocessor_config.json"),
                new("config.json"),
            ],
        },
        new AiModelPreset
        {
            Id = "detr-resnet50-q",
            Task = AiTaskKind.ObjectDetection,
            Family = "detr",
            Repo = "Xenova/detr-resnet-50",
            DisplayName = "DETR ResNet-50 · 量化",
            TagKey = "AiPlayground_Tag_Quantized",
            License = "Apache-2.0",
            ApproxSizeText = "约 42 MB",
            Files =
            [
                new("onnx/model_quantized.onnx", "model_quantized.onnx"),
                new("preprocessor_config.json"),
                new("config.json"),
            ],
        },
        new AiModelPreset
        {
            Id = "yolov9-c",
            Task = AiTaskKind.ObjectDetection,
            Family = "yolo",
            Repo = "Xenova/yolov9-c",
            DisplayName = "YOLOv9-C",
            TagKey = "AiPlayground_Tag_Gpl",
            License = "GPL-3.0",
            ApproxSizeText = "约 170 MB",
            IsAdvanced = true,
            Files =
            [
                new("onnx/model.onnx", "model.onnx"),
                new("preprocessor_config.json"),
                new("config.json"),
            ],
        },
        new AiModelPreset
        {
            Id = "yolov9-c-q",
            Task = AiTaskKind.ObjectDetection,
            Family = "yolo",
            Repo = "Xenova/yolov9-c",
            DisplayName = "YOLOv9-C · 量化",
            TagKey = "AiPlayground_Tag_QuantizedGpl",
            License = "GPL-3.0",
            ApproxSizeText = "约 45 MB",
            IsAdvanced = true,
            Files =
            [
                new("onnx/model_quantized.onnx", "model_quantized.onnx"),
                new("preprocessor_config.json"),
                new("config.json"),
            ],
        },

        // ── 图像特征（CLIP 视觉编码器，相似度对比）────────────────────
        new AiModelPreset
        {
            Id = "clip-vit-b32-vision",
            Task = AiTaskKind.ImageEmbedding,
            Family = "clip",
            Repo = "Xenova/clip-vit-base-patch32",
            DisplayName = "CLIP ViT-B/32 · 视觉编码器",
            License = "MIT",
            ApproxSizeText = "约 87 MB",
            Files =
            [
                new("onnx/vision_model.onnx", "vision_model.onnx"),
                new("preprocessor_config.json"),
                new("config.json"),
            ],
        },
        new AiModelPreset
        {
            Id = "clip-vit-b32-vision-q",
            Task = AiTaskKind.ImageEmbedding,
            Family = "clip",
            Repo = "Xenova/clip-vit-base-patch32",
            DisplayName = "CLIP ViT-B/32 · 视觉编码器 · 量化",
            TagKey = "AiPlayground_Tag_Quantized",
            License = "MIT",
            ApproxSizeText = "约 22 MB",
            Files =
            [
                new("onnx/vision_model_quantized.onnx", "vision_model_quantized.onnx"),
                new("preprocessor_config.json"),
                new("config.json"),
            ],
        },
    ];
}

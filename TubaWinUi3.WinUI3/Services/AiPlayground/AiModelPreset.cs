namespace TubaWinUi3.Services.AiPlayground;

/// <summary>本地 AI 试炼场支持的任务类型。</summary>
public enum AiTaskKind
{
    /// <summary>文本生成 / 对话（ORT GenAI）。</summary>
    Chat,
    /// <summary>图像分类（ImageNet 等，Top-K 结果）。</summary>
    ImageClassification,
    /// <summary>目标检测（DETR / YOLO）。</summary>
    ObjectDetection,
    /// <summary>图像特征向量（CLIP 视觉编码器，用于相似度对比）。</summary>
    ImageEmbedding,
}

/// <summary>对话模型的提示词模板风格。</summary>
public enum AiPromptFormat { Phi, ChatML, Llama3, Plain }

/// <summary>推理引擎模式（映射为 Windows ML 的 EP 选择策略）。</summary>
public enum AiEngineMode { Auto, Npu, Gpu, Cpu }

/// <summary>模型文件下载描述：Path 为仓库内相对路径，TargetName 为落盘文件名。</summary>
public sealed record AiModelFile(string Path, string? SaveAs = null)
{
    public string TargetName => SaveAs ?? Path.Split('/')[^1];
}

/// <summary>内置精选模型的静态定义（来源：Hugging Face 公开仓库）。</summary>
public sealed class AiModelPreset
{
    public required string Id { get; init; }
    public required AiTaskKind Task { get; init; }
    /// <summary>模型家族：phi / resnet / detr / yolo / clip，决定预处理与后处理管线。</summary>
    public required string Family { get; init; }
    public required string Repo { get; init; }
    public string Revision { get; init; } = "main";
    /// <summary>仓库内的目录前缀（空 = 仓库根）。</summary>
    public string BasePath { get; init; } = "";
    public required IReadOnlyList<AiModelFile> Files { get; init; }
    /// <summary>技术名（语言无关，如 "Phi-3.5-mini-instruct · INT4"）。</summary>
    public required string DisplayName { get; init; }
    /// <summary>本地化徽标键（AiPlayground_Tag_*），空 = 无徽标。</summary>
    public string TagKey { get; init; } = "";
    public required string License { get; init; }
    /// <summary>展示用大小（实际以下载时探测为准）。</summary>
    public required string ApproxSizeText { get; init; }
    public AiPromptFormat PromptFormat { get; init; } = AiPromptFormat.Phi;
    public bool IsAdvanced { get; init; }

    public string GetRepoPath(AiModelFile file) =>
        string.IsNullOrEmpty(BasePath) ? file.Path : $"{BasePath}/{file.Path}";
}

/// <summary>用户导入的本地模型（持久化于 models.json）。</summary>
public sealed class CustomAiModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public AiTaskKind Task { get; set; }
    /// <summary>"single-file"（复制进模型库）或 "genai-folder"（引用原目录，不复制）。</summary>
    public string Kind { get; set; } = "single-file";
    public string Path { get; set; } = "";
    public string? LabelsPath { get; set; }
    public AiPromptFormat PromptFormat { get; set; } = AiPromptFormat.Phi;
    public string ImportedAt { get; set; } = "";
}

/// <summary>页面使用的统一模型条目（内置预设或自定义导入）。</summary>
public sealed class AiModelEntry
{
    public required string Id { get; init; }
    public required AiTaskKind Task { get; init; }
    public required string DisplayName { get; init; }
    public string TagKey { get; init; } = "";
    public string SubLine { get; init; } = "";
    public AiModelPreset? Preset { get; init; }
    public CustomAiModel? Custom { get; init; }
    public bool IsCustom => Custom is not null;
}

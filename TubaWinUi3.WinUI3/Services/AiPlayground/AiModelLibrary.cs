using System.Text.Json;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>模型在磁盘上的状态。</summary>
public sealed class AiModelStatus
{
    public bool AllReady { get; init; }
    /// <summary>已存在部分文件（可用于"继续下载"提示）。</summary>
    public bool AnyPresent { get; init; }
    public int PresentFiles { get; init; }
    public int TotalFiles { get; init; }
    public long Bytes { get; init; }
    public string Describe()
    {
        if (AllReady) return "已就绪";
        if (AnyPresent) return $"不完整（{PresentFiles}/{TotalFiles}）";
        return "未下载";
    }
}

/// <summary>
/// 本地 AI 试炼场的模型库：文件落盘位置（&lt;DataDir&gt;/ai-models/&lt;id&gt;/）、
/// 完整性判定、删除与占用统计、本地模型导入（models.json 持久化）。
/// </summary>
public static class AiModelLibrary
{
    private static readonly object JsonGate = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string ModelsRoot => Path.Combine(ConfigManager.GetDataDir(), "ai-models");

    public static string CustomIndexPath => Path.Combine(ModelsRoot, "models.json");

    /// <summary>内置预设的落盘目录（自定义单文件模型也复制到这里）。</summary>
    public static string GetStoredModelDir(string id) => Path.Combine(ModelsRoot, id);

    /// <summary>条目的真实数据位置：预设/单文件 = 模型库内；引用型 genai 目录 = 用户原目录。</summary>
    public static string GetEntryDataDir(AiModelEntry entry)
    {
        if (entry.Custom is { Kind: "genai-folder" })
            return entry.Custom.Path;
        return GetStoredModelDir(entry.Id);
    }

    // ── 状态 ────────────────────────────────────────────────────────

    public static AiModelStatus GetStatus(AiModelEntry entry)
    {
        if (entry.Preset is not null)
        {
            var dir = GetStoredModelDir(entry.Preset.Id);
            int present = 0;
            long bytes = 0;
            foreach (var file in entry.Preset.Files)
            {
                var path = Path.Combine(dir, file.TargetName);
                if (FileExistsNonEmpty(path, out var size))
                {
                    present++;
                    bytes += size;
                }
            }
            return new AiModelStatus
            {
                AllReady = present == entry.Preset.Files.Count,
                AnyPresent = present > 0,
                PresentFiles = present,
                TotalFiles = entry.Preset.Files.Count,
                Bytes = bytes,
            };
        }

        var custom = entry.Custom!;
        if (custom.Kind == "genai-folder")
        {
            var dir = custom.Path;
            var ok = Directory.Exists(dir) && File.Exists(Path.Combine(dir, "genai_config.json"))
                     && Directory.EnumerateFiles(dir, "*.onnx").Any();
            return new AiModelStatus
            {
                AllReady = ok,
                AnyPresent = Directory.Exists(dir),
                PresentFiles = ok ? 1 : 0,
                TotalFiles = 1,
                Bytes = MeasureDirectory(dir),
            };
        }

        var filePath = custom.Path;
        var ready = FileExistsNonEmpty(filePath, out var fileSize);
        return new AiModelStatus
        {
            AllReady = ready,
            AnyPresent = ready,
            PresentFiles = ready ? 1 : 0,
            TotalFiles = 1,
            Bytes = fileSize,
        };
    }

    /// <summary>视觉模型的主 .onnx 文件（目录内优先 model.onnx / model_quantized.onnx）。</summary>
    public static string? FindVisionModelFile(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            foreach (var name in new[] { "model.onnx", "model_quantized.onnx", "vision_model.onnx", "vision_model_quantized.onnx" })
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }
            return Directory.EnumerateFiles(dir, "*.onnx")
                .FirstOrDefault(f => !Path.GetFileName(f).Contains("compiled", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static bool FileExistsNonEmpty(string path, out long size)
    {
        size = 0;
        try
        {
            if (!File.Exists(path)) return false;
            size = new FileInfo(path).Length;
            return size > 0;
        }
        catch
        {
            return false;
        }
    }

    // ── 删除 / 统计 ─────────────────────────────────────────────────

    /// <summary>删除模型数据。预设/复制型自定义模型删除模型库内目录；引用型只移除登记。</summary>
    public static void DeleteModel(AiModelEntry entry)
    {
        if (entry.Custom is { Kind: "genai-folder" })
        {
            RemoveCustomModel(entry.Custom.Id);
            return;
        }
        TryDeleteDirectory(GetStoredModelDir(entry.Id));
        if (entry.IsCustom)
        {
            RemoveCustomModel(entry.Custom!.Id);
        }
        // 删除模型后其 NPU 编译缓存也一并清掉（按前缀匹配）
        try
        {
            if (Directory.Exists(AiRuntimeService.CompiledCacheDir))
            {
                foreach (var file in Directory.GetFiles(AiRuntimeService.CompiledCacheDir, $"{entry.Id}__*.onnx"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    public static long MeasureDirectory(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return 0;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>模型库总占用（含编译缓存）。</summary>
    public static long GetTotalSize() => MeasureDirectory(ModelsRoot);

    public static long GetFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return 0;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    // ── 自定义模型（models.json）────────────────────────────────────

    public static List<CustomAiModel> GetCustomModels()
    {
        lock (JsonGate)
        {
            try
            {
                if (!File.Exists(CustomIndexPath)) return [];
                var json = File.ReadAllText(CustomIndexPath);
                return JsonSerializer.Deserialize<List<CustomAiModel>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }
    }

    private static void SaveCustomModels(List<CustomAiModel> models)
    {
        lock (JsonGate)
        {
            try
            {
                Directory.CreateDirectory(ModelsRoot);
                File.WriteAllText(CustomIndexPath, JsonSerializer.Serialize(models, JsonOpts));
            }
            catch { }
        }
    }

    public static void RemoveCustomModel(string id)
    {
        var models = GetCustomModels();
        var target = models.FirstOrDefault(m => m.Id == id);
        if (target is null) return;
        models.Remove(target);
        SaveCustomModels(models);
    }

    /// <summary>
    /// 导入单个 .onnx 文件（复制进模型库）。标签文件可选（分类/检测模型的自定义标签）。
    /// 支持的任务：图像分类 / 目标检测 / 图像特征。
    /// </summary>
    public static CustomAiModel ImportSingleFile(string filePath, AiTaskKind task, string? labelsPath, string? displayName)
    {
        var name = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileNameWithoutExtension(filePath)
            : displayName.Trim();
        var id = MakeCustomId(name);
        var dir = GetStoredModelDir(id);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, Path.GetFileName(filePath));
        File.Copy(filePath, target, overwrite: true);

        var model = new CustomAiModel
        {
            Id = id,
            Name = name,
            Task = task,
            Kind = "single-file",
            Path = target,
            LabelsPath = labelsPath,
            ImportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };
        var models = GetCustomModels();
        models.Add(model);
        SaveCustomModels(models);
        return model;
    }

    /// <summary>
    /// 导入 ORT GenAI 模型目录（引用原位置，不复制——目录通常 2 GB+）。
    /// 必须包含 genai_config.json 与至少一个 .onnx 文件。
    /// </summary>
    public static CustomAiModel ImportGenAiFolder(string folder, AiPromptFormat promptFormat, string? displayName)
    {
        if (!File.Exists(Path.Combine(folder, "genai_config.json")))
            throw new InvalidOperationException("所选目录中没有 genai_config.json，不是 ORT GenAI 格式的模型目录。");
        if (!Directory.EnumerateFiles(folder, "*.onnx").Any())
            throw new InvalidOperationException("所选目录中没有 .onnx 模型文件。");

        var name = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : displayName.Trim();
        var id = MakeCustomId(name);
        var model = new CustomAiModel
        {
            Id = id,
            Name = name,
            Task = AiTaskKind.Chat,
            Kind = "genai-folder",
            Path = folder,
            PromptFormat = promptFormat,
            ImportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };
        var models = GetCustomModels();
        models.Add(model);
        SaveCustomModels(models);
        return model;
    }

    private static string MakeCustomId(string name)
    {
        var slug = new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (slug.Length > 24) slug = slug[..24];
        if (string.IsNullOrEmpty(slug)) slug = "model";
        return $"custom-{slug}-{Guid.NewGuid().ToString("N")[..4]}";
    }

    /// <summary>解析 "约 2.4 GB" 这类大小描述为字节数（用于磁盘空间预估）。失败返回 0。</summary>
    internal static long ParseApproxBytes(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"([\d.]+)\s*(GB|MB)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return 0;
        if (!double.TryParse(match.Groups[1].Value, out var value)) return 0;
        var unit = match.Groups[2].Value.ToUpperInvariant();
        return (long)(unit == "GB" ? value * (1L << 30) : value * (1L << 20));
    }

    // ── 统一条目 ────────────────────────────────────────────────────

    public static List<AiModelEntry> GetAllEntries()
    {
        var list = new List<AiModelEntry>();
        foreach (var preset in AiPlaygroundCatalog.All)
        {
            list.Add(new AiModelEntry
            {
                Id = preset.Id,
                Task = preset.Task,
                DisplayName = preset.DisplayName,
                TagKey = preset.TagKey,
                SubLine = $"{preset.Repo} · {preset.ApproxSizeText} · {preset.License}",
                Preset = preset,
            });
        }
        foreach (var custom in GetCustomModels())
        {
            list.Add(new AiModelEntry
            {
                Id = custom.Id,
                Task = custom.Task,
                DisplayName = custom.Name,
                TagKey = "AiPlayground_Tag_Custom",
                SubLine = custom.Kind == "genai-folder"
                    ? $"本地导入 · 引用目录：{custom.Path}"
                    : $"本地导入 · {custom.Path}",
                Custom = custom,
            });
        }
        return list;
    }
}

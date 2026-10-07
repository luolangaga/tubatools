using System.Text.Json;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// 标签加载与中文化：分类/检测模型的 id2label（模型目录 config.json）、
/// 内置 COCO-80 标签（Assets/AiPlayground/coco-labels.txt）与英文→中文对照。
/// </summary>
public static class AiPlaygroundLabels
{
    private static readonly Dictionary<string, string> ZhMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // COCO-80（YOLO 输出顺序）
        ["person"] = "人",
        ["bicycle"] = "自行车",
        ["car"] = "汽车",
        ["motorcycle"] = "摩托车",
        ["airplane"] = "飞机",
        ["bus"] = "公交车",
        ["train"] = "火车",
        ["truck"] = "卡车",
        ["boat"] = "船",
        ["traffic light"] = "交通灯",
        ["fire hydrant"] = "消防栓",
        ["stop sign"] = "停车标志",
        ["parking meter"] = "停车计时器",
        ["bench"] = "长椅",
        ["bird"] = "鸟",
        ["cat"] = "猫",
        ["dog"] = "狗",
        ["horse"] = "马",
        ["sheep"] = "绵羊",
        ["cow"] = "牛",
        ["elephant"] = "大象",
        ["bear"] = "熊",
        ["zebra"] = "斑马",
        ["giraffe"] = "长颈鹿",
        ["backpack"] = "背包",
        ["umbrella"] = "雨伞",
        ["handbag"] = "手提包",
        ["tie"] = "领带",
        ["suitcase"] = "行李箱",
        ["frisbee"] = "飞盘",
        ["skis"] = "滑雪板",
        ["snowboard"] = "单板滑雪",
        ["sports ball"] = "球",
        ["kite"] = "风筝",
        ["baseball bat"] = "棒球棒",
        ["baseball glove"] = "棒球手套",
        ["skateboard"] = "滑板",
        ["surfboard"] = "冲浪板",
        ["tennis racket"] = "网球拍",
        ["bottle"] = "瓶子",
        ["wine glass"] = "酒杯",
        ["cup"] = "杯子",
        ["fork"] = "叉子",
        ["knife"] = "刀",
        ["spoon"] = "勺子",
        ["bowl"] = "碗",
        ["banana"] = "香蕉",
        ["apple"] = "苹果",
        ["sandwich"] = "三明治",
        ["orange"] = "橙子",
        ["broccoli"] = "西兰花",
        ["carrot"] = "胡萝卜",
        ["hot dog"] = "热狗",
        ["pizza"] = "披萨",
        ["donut"] = "甜甜圈",
        ["cake"] = "蛋糕",
        ["chair"] = "椅子",
        ["couch"] = "沙发",
        ["potted plant"] = "盆栽",
        ["bed"] = "床",
        ["dining table"] = "餐桌",
        ["toilet"] = "马桶",
        ["tv"] = "电视",
        ["laptop"] = "笔记本电脑",
        ["mouse"] = "鼠标",
        ["remote"] = "遥控器",
        ["keyboard"] = "键盘",
        ["cell phone"] = "手机",
        ["microwave"] = "微波炉",
        ["oven"] = "烤箱",
        ["toaster"] = "烤面包机",
        ["sink"] = "水槽",
        ["refrigerator"] = "冰箱",
        ["book"] = "书",
        ["clock"] = "时钟",
        ["vase"] = "花瓶",
        ["scissors"] = "剪刀",
        ["teddy bear"] = "泰迪熊",
        ["hair drier"] = "吹风机",
        ["toothbrush"] = "牙刷",
        // DETR 91 类表中的额外类别
        ["street sign"] = "路牌",
        ["hat"] = "帽子",
        ["shoe"] = "鞋",
        ["eye glasses"] = "眼镜",
        ["plate"] = "盘子",
        ["mirror"] = "镜子",
        ["window"] = "窗户",
        ["desk"] = "桌子",
        ["door"] = "门",
        ["blender"] = "搅拌机",
    };

    /// <summary>从模型目录的 config.json 读取 id2label（按序号排列）。读取失败返回空数组。</summary>
    public static string[] LoadId2Label(string modelDir)
    {
        try
        {
            var path = Path.Combine(modelDir, "config.json");
            if (!File.Exists(path)) return [];
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("id2label", out var id2label) ||
                id2label.ValueKind != JsonValueKind.Object)
                return [];

            var pairs = new List<(int Index, string Label)>();
            int maxIndex = -1;
            foreach (var prop in id2label.EnumerateObject())
            {
                if (int.TryParse(prop.Name, out var index) && prop.Value.ValueKind == JsonValueKind.String)
                {
                    pairs.Add((index, prop.Value.GetString() ?? ""));
                    if (index > maxIndex) maxIndex = index;
                }
            }
            if (pairs.Count == 0) return [];

            var labels = new string[maxIndex + 1];
            foreach (var (index, label) in pairs)
                labels[index] = label;
            for (int i = 0; i < labels.Length; i++)
                labels[i] ??= $"class {i}";
            return labels;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>内置 COCO-80 标签（Assets/AiPlayground/coco-labels.txt）。</summary>
    public static string[] LoadCocoLabels()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AiPlayground", "coco-labels.txt");
            if (!File.Exists(path)) return [];
            return File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>英文标签 → 中文（未知原样返回）。</summary>
    public static string Translate(string label)
    {
        var trimmed = label.Trim();
        if (trimmed.Length == 0 || trimmed == "N/A") return trimmed;
        return ZhMap.TryGetValue(trimmed, out var zh) ? zh : trimmed;
    }

    /// <summary>ImageNet 标签形如 "tench, Tinca tinca"，显示取逗号前的常用名。</summary>
    public static string Shorten(string label)
    {
        var comma = label.IndexOf(',');
        return comma > 0 ? label[..comma].Trim() : label.Trim();
    }
}

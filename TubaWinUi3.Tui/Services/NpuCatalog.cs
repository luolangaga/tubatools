using System;

namespace TubaWinUi3.Tui.Services
{
    /// <summary>
    /// NPU 算力（TOPS）查询表。
    /// Windows 没有公开 NPU 算力的 API/WMI 字段，只能按设备名 + CPU 型号匹配厂商公开规格。
    /// NPU 的设备名往往很笼统（如 "Intel(R) AI Boost"、"AMD IPU Device"），所以同时接收 CPU 型号用于判断代数。
    /// 未收录的型号返回 null，调用方显示"未知"。
    /// </summary>
    public static class NpuCatalog
    {
        /// <summary>根据 NPU 设备名和 CPU 型号返回算力描述，如 "48 TOPS"；无法推断返回 null。</summary>
        public static string LookupTops(string npuName, string cpuName)
        {
            string tops;
            if (TryMatchQualcomm(npuName, cpuName, out tops)) return tops;
            if (TryMatchIntel(cpuName, out tops)) return tops;
            if (TryMatchAmd(cpuName, out tops)) return tops;
            return null;
        }

        /// <summary>高通：Snapdragon X 系列（X Elite / X Plus / X1E / X1P）均为 Hexagon NPU，45 TOPS（INT8）。</summary>
        private static bool TryMatchQualcomm(string npuName, string cpuName, out string tops)
        {
            tops = null;
            var text = (npuName + " " + cpuName).ToUpperInvariant();
            if (!text.Contains("SNAPDRAGON")) return false;
            // X2 及以上还未收录，宁可显示"未知"也不误导
            if (text.Contains("X2")) return true;
            if (text.Contains("X ELITE") || text.Contains("X PLUS") || text.Contains("X1E") || text.Contains("X1P") || text.Contains("X1-"))
            {
                tops = "45 TOPS";
                return true;
            }
            return false;
        }

        /// <summary>Intel：Core Ultra 系列，按型号代次区分（NPU 设备名统一为 "Intel(R) AI Boost"，必须看 CPU 型号）。</summary>
        private static bool TryMatchIntel(string cpuName, out string tops)
        {
            tops = null;
            var tokens = Tokenize(cpuName != null ? cpuName.ToUpperInvariant() : null);
            var coreIdx = IndexOfToken(tokens, token => token.Contains("CORE"));
            var ultraIdx = IndexOfToken(tokens, token => token == "ULTRA");
            if (coreIdx < 0 || ultraIdx < 0) return false;

            // 型号代号位于 "ULTRA <档次> <型号>"，如 "Ultra 7 155H" -> "155H"；部分 OEM 命名会插入 "PROCESSOR" 等词
            var model = FindModelToken(tokens, ultraIdx + 1);
            if (model == null) return false;

            if (model.EndsWith("V", StringComparison.Ordinal))
            {
                // Lunar Lake（Ultra 2xxV，NPU 4）：48 TOPS（INT8）
                tops = "48 TOPS";
                return true;
            }
            if (model.StartsWith("1", StringComparison.Ordinal))
            {
                // Meteor Lake（Ultra 1xx / Ultra 3 1xx，NPU 3）：11 TOPS
                tops = "11 TOPS";
                return true;
            }
            if (model.StartsWith("2", StringComparison.Ordinal))
            {
                // Arrow Lake（Ultra 2xxH/U/K 等，NPU 4）：13 TOPS
                tops = "13 TOPS";
                return true;
            }
            return false;
        }

        /// <summary>AMD：XDNA。Ryzen AI（Strix Point / Krackan / Strix Halo）50 TOPS；8xxx 系列（Hawk Point 移动版、Phoenix 桌面版）16 TOPS；7xxx 系列（Phoenix 移动版）10 TOPS。</summary>
        private static bool TryMatchAmd(string cpuName, out string tops)
        {
            tops = null;
            var upper = cpuName != null ? cpuName.ToUpperInvariant() : "";
            var tokens = Tokenize(upper);
            var ryzenIdx = IndexOfToken(tokens, token => token.Contains("RYZEN"));
            if (ryzenIdx < 0) return false;

            // 下一枚 token 是 "AI"（兼容 "AMD Ryzen AI ..." 与 "AMD Ryzen(TM) AI ..."）
            if (ryzenIdx + 1 < tokens.Length && tokens[ryzenIdx + 1] == "AI")
            {
                tops = "50 TOPS";
                return true;
            }

            var model = FindModelToken(tokens, ryzenIdx + 1);
            if (model == null) return false;

            if (model.StartsWith("8", StringComparison.Ordinal))
            {
                // 移动版 8040/8640/8840/8945 与桌面版 8500G/8600G/8700G
                tops = "16 TOPS";
                return true;
            }
            if (model.StartsWith("7", StringComparison.Ordinal))
            {
                // 移动版 7040/7540/7640/7740/7840/7940
                tops = "10 TOPS";
                return true;
            }
            return false;
        }

        /// <summary>从起始位置往后找第一个"以数字开头、长度 ≥2"的代号（跳过层级数字，如 "Ultra 7 155H" 中的 "7"）。</summary>
        private static string FindModelToken(string[] tokens, int startIdx)
        {
            for (var i = startIdx; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (token.Length < 2) continue;
                if (char.IsDigit(token[0])) return token;
            }
            return null;
        }

        private static string[] Tokenize(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            return text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static int IndexOfToken(string[] tokens, Func<string, bool> predicate)
        {
            for (var i = 0; i < tokens.Length; i++)
            {
                if (predicate(tokens[i])) return i;
            }
            return -1;
        }
    }
}
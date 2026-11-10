using System;
using System.Collections.Generic;
using System.Linq;
using TubaWinUi3.Tui.Services;

namespace TubaWinUi3.Tui.Models
{
    public sealed class ToolItem
    {
        public int? SortOrder { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Path { get; set; }
        public string RelativePath { get; set; }

        public string Description { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public IReadOnlyList<string> Tags { get; set; } = new List<string>();
        public string TagsText { get { return Tags != null && Tags.Count > 0 ? string.Join("  ", Tags) : ""; } }

        public string PrimaryArch { get; set; }
        public IReadOnlyList<ArchVariant> AlternateVersions { get; set; } = [];

        /// <summary>tools.json 跨分类副本：真实目录所在分类（主分类）。</summary>
        public string PrimaryCategory { get; set; }

        /// <summary>该工具出现的所有分类（含 tools.json categories 声明的副本分类），「全部工具」视图去重后合并。</summary>
        public IReadOnlyList<string> Categories { get; set; } = new List<string>();


        public string CategoriesText { get { return Categories != null && Categories.Count > 0 ? string.Join(" · ", Categories) : Category; } }

        public void SetCategories(IReadOnlyList<string> categories)
        {
            Categories = categories;
        }

        public string EffectiveWorkingDir => System.IO.Path.GetDirectoryName(Path) ?? ToolCatalog.ToolsRoot;

        public ToolItem()
        {
            Name = "";
            Category = "";
            Path = "";
            RelativePath = "";
        }
    }

    public sealed class ArchVariant
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Arch { get; set; }

        public ArchVariant() { Name = ""; Path = ""; Arch = ""; }
    }
}

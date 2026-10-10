using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TubaWinUi3.Compatible.Services;

namespace TubaWinUi3.Compatible.Models
{
    public sealed class ToolItem : INotifyPropertyChanged
    {
        public string LibraryId { get; set; }
        public int? SortOrder { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Path { get; set; }
        public string RelativePath { get; set; }
        public string Extension { get; set; }

        private string _iconPath;
        public string IconPath
        {
            get => _iconPath;
            set { if (SetField(ref _iconPath, value)) { } }
        }

        private string _iconGlyph;
        public string IconGlyph
        {
            get => _iconGlyph;
            set { if (SetField(ref _iconGlyph, value)) { } }
        }

        public string Description { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public string DatabaseSource { get; set; }
        public string DownloadUrl { get; set; }
        public string DownloadFilter { get; set; }
        public string WingetId { get; set; }
        public IReadOnlyList<string> Tags { get; set; } = new List<string>();
        public string TagsText { get { return Tags != null && Tags.Count > 0 ? string.Join("  ", Tags) : ""; } }

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetField(ref _isFavorite, value);
        }

        public string Folder { get { return System.IO.Path.GetDirectoryName(RelativePath) ?? Category; } }

        public bool NeedsDownload
        {
            get { return !string.IsNullOrWhiteSpace(DownloadUrl) || !string.IsNullOrWhiteSpace(WingetId); }
        }

        public bool NeedsWingetInstall { get { return !string.IsNullOrWhiteSpace(WingetId); } }

        public bool CanLaunch { get { return true; } }

        public string PrimaryArch { get; set; }
        public IReadOnlyList<ArchVariant> AlternateVersions { get; set; } = new List<ArchVariant>();
        public bool HasAlternateVersions { get { return AlternateVersions != null && AlternateVersions.Count > 0; } }

        /// <summary>tools.json 跨分类副本：真实目录所在分类（主分类）。</summary>
        public string PrimaryCategory { get; set; }

        /// <summary>该工具出现的所有分类（含 tools.json categories 声明的副本分类），「全部工具」视图去重后合并。</summary>
        public IReadOnlyList<string> Categories { get; set; } = new List<string>();

        /// <summary>是否由 tools.json 跨分类副本（category+categories）产生的副本。</summary>
        public bool IsLinked { get; set; }

        public string CategoriesText { get { return Categories != null && Categories.Count > 0 ? string.Join(" · ", Categories) : Category; } }

        public void SetCategories(IReadOnlyList<string> categories)
        {
            Categories = categories;
        }

        public List<ArchOption> ArchOptions { get; set; } = new List<ArchOption>();

        private ArchOption _selectedArch;
        public ArchOption SelectedArch
        {
            get => _selectedArch;
            set
            {
                if (SetField(ref _selectedArch, value))
                {
                    OnPropertyChanged("EffectivePath");
                    OnPropertyChanged("EffectiveWorkingDir");
                }
            }
        }

        public string EffectivePath { get { return SelectedArch != null ? SelectedArch.Path : Path; } }

        public string EffectiveWorkingDir
        {
            get { return System.IO.Path.GetDirectoryName(EffectivePath) ?? ToolCatalog.ToolsRoot; }
        }

        public string LaunchButtonText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DownloadUrl)) return "下载";
                return "打开";
            }
        }

        public void InitArchOptions()
        {
            ArchOptions.Clear();
            var primary = new ArchOption { Name = Name, Path = Path, Arch = PrimaryArch ?? "" };
            ArchOptions.Add(primary);
            if (AlternateVersions != null)
            {
                foreach (var v in AlternateVersions)
                {
                    ArchOptions.Add(new ArchOption { Name = v.Name, Path = v.Path, Arch = v.Arch });
                }
            }

            // 与主应用一致：ARM64 系统优先 ARM64 > x64 > x86，x64 系统优先 x64 > x86。
            var preferred = PickByPriority(ArchOptions)
                ?? ArchOptions.FirstOrDefault(a => string.IsNullOrEmpty(a.Arch))
                ?? primary;
            SelectedArch = preferred;
        }

        private static ArchOption PickByPriority(IReadOnlyList<ArchOption> options)
        {
            foreach (var arch in PreferredArchPriority)
            {
                var match = options.FirstOrDefault(o => o.Arch.Equals(arch, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return null;
        }

        /// <summary>主机 OS 架构（非进程架构）优先序，与主应用 PreferredArchPriority 一致。</summary>
        private static readonly IReadOnlyList<string> PreferredArchPriority = BuildPreferredArchPriority();

        private static IReadOnlyList<string> BuildPreferredArchPriority()
        {
            try
            {
                switch (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)
                {
                    case System.Runtime.InteropServices.Architecture.Arm64: return new[] { "ARM64", "x64", "x86" };
                    case System.Runtime.InteropServices.Architecture.X64: return new[] { "x64", "x86" };
                    case System.Runtime.InteropServices.Architecture.X86: return new[] { "x86" };
                }
            }
            catch { }
            return new[] { "x64", "x86" };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ToolItem()
        {
            Name = "";
            Category = "";
            Path = "";
            RelativePath = "";
            Extension = "";
        }
    }

    public sealed class ArchVariant
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Arch { get; set; }

        public ArchVariant() { Name = ""; Path = ""; Arch = ""; }
    }

    public sealed class ArchOption
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Arch { get; set; }

        public string DisplayText { get { return string.IsNullOrEmpty(Arch) ? "默认" : Arch; } }

        public override string ToString() { return DisplayText; }

        public ArchOption() { Name = ""; Path = ""; Arch = ""; }
    }
}

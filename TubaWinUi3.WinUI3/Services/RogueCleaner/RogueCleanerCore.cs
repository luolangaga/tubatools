#nullable disable
// 移植自 RogueCleaner（https://github.com/aakk007/RogueCleaner），MIT License，Copyright (c) 2026 aakk007
// 原版为 .NET Framework 4.x WinForms；此处为 WinUI 3 移植，逻辑保持一致。

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services.RogueCleaner
{

    internal static class AppMeta
    {
        public const string ProductName = "流氓软件克星";
        public const string Version = "2.0.16";
        public const string AuthorName = "aakk007";
        public const string Author52PojieUrl = "https://www.52pojie.cn/home.php?mod=space&uid=286924";
        public const string AuthorGitHubUrl = "https://github.com/aakk007";
        public const string Repository = "https://github.com/aakk007/RogueCleaner";
        public const string ReleasesUrl = "https://github.com/aakk007/RogueCleaner/releases";
        public const string LatestApiUrl = "https://api.github.com/repos/aakk007/RogueCleaner/releases/latest";
        public const string DataDirName = "流氓软件克星数据";
        public const string DotNetDownloadUrl = "https://dotnet.microsoft.com/download/dotnet-framework";
    }

    internal sealed class DataStore
    {
        public string Root { get; private set; }
        public string Backups { get; private set; }
        public string Reports { get; private set; }
        public string Logs { get; private set; }
        public string Updates { get; private set; }
        public string Quarantine { get; private set; }
        public string State { get; private set; }
        public string Feedbacks { get; private set; }

        // 默认数据目录：跟随本应用（TubaWinUi3）的 ConfigManager 数据目录，避免写入安装目录。
        public static DataStore CreateDefault()
        {
            string root;
            try
            {
                string baseDir = ConfigManager.GetDataDir();
                root = string.IsNullOrWhiteSpace(baseDir) ? CreateForExecutableFallback() : Path.Combine(baseDir, "RogueCleaner");
            }
            catch
            {
                root = CreateForExecutableFallback();
            }
            return new DataStore
            {
                Root = root,
                Backups = Path.Combine(root, "backups"),
                Reports = Path.Combine(root, "reports"),
                Logs = Path.Combine(root, "logs"),
                Updates = Path.Combine(root, "updates"),
                Quarantine = Path.Combine(root, "quarantine"),
                State = Path.Combine(root, "state"),
                Feedbacks = Path.Combine(root, "feedback")
            };
        }

        private static string CreateForExecutableFallback()
        {
            string exePath = Environment.ProcessPath ?? AppMeta.ProductName;
            string exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            return Path.Combine(exeDir, AppMeta.DataDirName);
        }

        public static DataStore CreateForExecutable(string exePath)
        {
            string exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            string root = Path.Combine(exeDir, AppMeta.DataDirName);
            return new DataStore
            {
                Root = root,
                Backups = Path.Combine(root, "backups"),
                Reports = Path.Combine(root, "reports"),
                Logs = Path.Combine(root, "logs"),
                Updates = Path.Combine(root, "updates"),
                Quarantine = Path.Combine(root, "quarantine"),
                State = Path.Combine(root, "state"),
                Feedbacks = Path.Combine(root, "feedback")
            };
        }

        public void Ensure()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Reports);
            Directory.CreateDirectory(Logs);
            Directory.CreateDirectory(Updates);
            Directory.CreateDirectory(Quarantine);
            Directory.CreateDirectory(State);
            Directory.CreateDirectory(Feedbacks);
        }

        public string StateFile(string name)
        {
            return Path.Combine(State, name);
        }

        public string Timestamp()
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }
    }

    internal static class Logger
    {
        private static DataStore store;

        public static void Initialize(DataStore dataStore)
        {
            store = dataStore;
        }

        public static void Error(string message, Exception ex)
        {
            // 统一走日志服务（日志目录/rogue-cleaner.log），不再自己往 RogueCleaner/logs 里攒文件
            if (store == null) return;
            LogService.Error(LogService.RogueCleaner, message, ex);
        }
    }

    internal sealed class Finding : INotifyPropertyChanged
    {
        private bool selected;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                OnPropertyChanged("Selected");
            }
        }

        public int Id { get; set; }
        public string Risk { get; set; }
        public int Score { get; set; }
        public string Vendor { get; set; }
        public string Category { get; set; }
        public string UserVisibleName { get; set; }
        public string UserImpact { get; set; }
        public string TechnicalLocation { get; set; }
        public string ActionKind { get; set; }
        public ActionTarget Target { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool CanRestore { get; set; }
        public string Evidence { get; set; }
        public string Status { get; set; }

        [JsonIgnore]
        public Image SoftwareIcon { get; set; }
        // WinUI 行模板直接绑定的图标（页面水合后填充）
        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.ImageSource IconDisplay { get; set; }
        [JsonIgnore]
        public string SoftwareName { get; set; }
        [JsonIgnore]
        public string IdentityConfidence { get; set; }
        [JsonIgnore]
        public string IconSource { get; set; }
        [JsonIgnore]
        public string IdentityExplanation { get; set; }

        public SoftwarePresentationEvidence PresentationEvidence()
        {
            ActionTarget target = Target ?? new ActionTarget();
            return new SoftwarePresentationEvidence
            {
                DeclaredName = UserVisibleName,
                DeclaredVendor = Vendor,
                IconValue = target.IconValue,
                FilePath = target.FilePath,
                Command = !string.IsNullOrEmpty(target.PresentationCommand) ? target.PresentationCommand : (!string.IsNullOrEmpty(target.UninstallCommand) ? target.UninstallCommand : Evidence),
                ServiceName = target.ServiceName,
                Clsid = target.Clsid,
                TechnicalLocation = TechnicalLocation
            };
        }

        public void ApplyPresentation(SoftwarePresentation presentation)
        {
            if (presentation == null) return;
            SoftwareIcon = presentation.Icon;
            SoftwareName = presentation.SoftwareName;
            IdentityConfidence = presentation.Confidence;
            IconSource = presentation.IconSource;
            IdentityExplanation = presentation.Explanation;
            if ((string.IsNullOrWhiteSpace(Vendor) || Vendor == "未知第三方" || Vendor == "未知") && presentation.Confidence != "Unknown") Vendor = presentation.Vendor;
        }

        public bool CanClean
        {
            get { return !string.Equals(ActionKind, "ReportOnly", StringComparison.OrdinalIgnoreCase); }
        }

        public string RiskDisplay
        {
            get { return CanClean ? Risk : "仅提示"; }
        }

        public bool BulkSelectable
        {
            get { return CanClean && !string.Equals(ActionKind, "InvokeUninstaller", StringComparison.OrdinalIgnoreCase); }
        }

        public string SelectionHint
        {
            get
            {
                if (CanClean && RequiresAdmin && !AdminUtil.IsAdministrator()) return "可勾选：处理时会请求 Windows 管理员权限；没有管理员凭据时仍可扫描和导出报告。";
                if (CanClean) return "可勾选：工具会先备份，再按“工具会怎么处理”执行。";
                return "不可勾选：" + ReportOnlyActionText();
            }
        }

        public string ActionText
        {
            get
            {
                if (string.Equals(ActionKind, "DeleteRegistryKey", StringComparison.OrdinalIgnoreCase)) return "备份后删除这条注册表项";
                if (string.Equals(ActionKind, "DeleteRegistryValue", StringComparison.OrdinalIgnoreCase)) return "备份后删除这条注册表值";
                if (string.Equals(ActionKind, "DisableShellExtension", StringComparison.OrdinalIgnoreCase)) return "备份状态后禁用右键扩展";
                if (string.Equals(ActionKind, "MoveFileToBackup", StringComparison.OrdinalIgnoreCase)) return "移动到恢复中心";
                if (string.Equals(ActionKind, "DisableService", StringComparison.OrdinalIgnoreCase)) return "备份状态后禁用服务";
                if (string.Equals(ActionKind, "DisableScheduledTask", StringComparison.OrdinalIgnoreCase)) return "备份状态后禁用计划任务";
                if (string.Equals(ActionKind, "InvokeUninstaller", StringComparison.OrdinalIgnoreCase)) return "只打开这个附带产品的卸载器，不卸载主程序";
                return ReportOnlyActionText();
            }
        }

        [JsonIgnore]
        public string CompactTitle
        {
            get
            {
                string title = UserVisibleName ?? string.Empty;
                if (title.IndexOf("：会出现", StringComparison.Ordinal) >= 0 || title.IndexOf("：疑似会出现", StringComparison.Ordinal) >= 0)
                {
                    int open = title.IndexOf('“');
                    int close = title.LastIndexOf('”');
                    if (open >= 0 && close > open) return title.Substring(open + 1, close - open - 1).Trim();
                }
                return title;
            }
        }

        [JsonIgnore]
        public string CompactLocation
        {
            get
            {
                string title = UserVisibleName ?? string.Empty;
                if (title.StartsWith("普通文件右键", StringComparison.Ordinal)) return "文件右键";
                if (title.StartsWith("文件夹右键", StringComparison.Ordinal)) return "文件夹右键";
                if (title.StartsWith("桌面/文件夹空白处右键", StringComparison.Ordinal)) return "空白处右键";
                if (title.StartsWith("磁盘盘符右键", StringComparison.Ordinal)) return "磁盘右键";
                if (title.StartsWith("快捷方式右键", StringComparison.Ordinal)) return "快捷方式";
                string category = Category ?? string.Empty;
                if (category.IndexOf("右键菜单", StringComparison.OrdinalIgnoreCase) >= 0) return "右键菜单";
                if (category.IndexOf("后台服务", StringComparison.OrdinalIgnoreCase) >= 0) return "后台服务";
                if (category.IndexOf("启动", StringComparison.OrdinalIgnoreCase) >= 0) return "开机启动";
                if (category.IndexOf("计划任务", StringComparison.OrdinalIgnoreCase) >= 0) return "计划任务";
                if (category.IndexOf("文件关联", StringComparison.OrdinalIgnoreCase) >= 0) return "文件关联";
                if (category.IndexOf("浏览器", StringComparison.OrdinalIgnoreCase) >= 0) return "浏览器";
                if (category.IndexOf("正在运行", StringComparison.OrdinalIgnoreCase) >= 0) return "正在运行";
                if (category.IndexOf("此电脑", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("资源管理器", StringComparison.OrdinalIgnoreCase) >= 0) return "资源管理器";
                if (category.IndexOf("卸载", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("弹窗", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("捆绑", StringComparison.OrdinalIgnoreCase) >= 0) return "组件诊断";
                if (category.IndexOf("附带产品", StringComparison.OrdinalIgnoreCase) >= 0) return "附带产品";
                return category;
            }
        }

        [JsonIgnore]
        public string CompactImpact
        {
            get
            {
                string category = Category ?? string.Empty;
                if (category.IndexOf("右键菜单", StringComparison.OrdinalIgnoreCase) >= 0) return "右键入口";
                if (category.IndexOf("后台服务", StringComparison.OrdinalIgnoreCase) >= 0) return "后台常驻";
                if (category.IndexOf("启动", StringComparison.OrdinalIgnoreCase) >= 0) return "开机启动";
                if (category.IndexOf("计划任务", StringComparison.OrdinalIgnoreCase) >= 0) return "定时运行";
                if (category.IndexOf("文件关联", StringComparison.OrdinalIgnoreCase) >= 0) return "打开方式";
                if (category.IndexOf("浏览器", StringComparison.OrdinalIgnoreCase) >= 0) return "浏览器组件";
                if (category.IndexOf("正在运行", StringComparison.OrdinalIgnoreCase) >= 0) return "正在运行";
                if (category.IndexOf("此电脑", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("资源管理器", StringComparison.OrdinalIgnoreCase) >= 0) return "资源管理器入口";
                if (category.IndexOf("卸载", StringComparison.OrdinalIgnoreCase) >= 0) return "原厂卸载";
                if (category.IndexOf("附带产品", StringComparison.OrdinalIgnoreCase) >= 0) return "独立安装";
                if (category.IndexOf("弹窗", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("捆绑", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("守护", StringComparison.OrdinalIgnoreCase) >= 0) return "异常组件";
                return ShortDisplayText(UserImpact, 12);
            }
        }

        [JsonIgnore]
        public string CompactAction
        {
            get
            {
                if (string.Equals(ActionKind, "DeleteRegistryKey", StringComparison.OrdinalIgnoreCase) || string.Equals(ActionKind, "DeleteRegistryValue", StringComparison.OrdinalIgnoreCase)) return "备份删除";
                if (string.Equals(ActionKind, "DisableShellExtension", StringComparison.OrdinalIgnoreCase)) return "备份禁用";
                if (string.Equals(ActionKind, "MoveFileToBackup", StringComparison.OrdinalIgnoreCase)) return "移入恢复";
                if (string.Equals(ActionKind, "DisableService", StringComparison.OrdinalIgnoreCase) || string.Equals(ActionKind, "DisableScheduledTask", StringComparison.OrdinalIgnoreCase)) return "备份禁用";
                if (string.Equals(ActionKind, "InvokeUninstaller", StringComparison.OrdinalIgnoreCase)) return "定向卸载";
                return "仅提示";
            }
        }

        private static string ShortDisplayText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "查看详情";
            string text = value.Trim();
            int sentence = text.IndexOfAny(new char[] { '。', '；', ';', '\r', '\n' });
            if (sentence > 0) text = text.Substring(0, sentence);
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "…";
        }

        private string ReportOnlyActionText()
        {
            string category = Category ?? string.Empty;
            if (category.IndexOf("默认打开程序", StringComparison.OrdinalIgnoreCase) >= 0) return "仅提示：这是双击默认打开方式，不替用户改默认应用";
            if (category.IndexOf("卸载入口", StringComparison.OrdinalIgnoreCase) >= 0) return "仅提示：没有可靠卸载命令，不硬删主程序";
            if (category.IndexOf("正在运行", StringComparison.OrdinalIgnoreCase) >= 0) return "仅提示：不强杀正在运行的进程";
            return "仅提示：为避免误伤，不参与一键清理";
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    internal sealed class UserWhitelistEntry
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string AddedAt { get; set; }
    }

    internal static class UserWhitelistStore
    {
        private const string FileName = "user-whitelist.json";

        public static string KeyFor(Finding finding)
        {
            if (finding == null) return string.Empty;
            ActionTarget target = finding.Target ?? new ActionTarget();
            return string.Join("|", new string[] { target.Kind, target.Hive, target.View, target.SubKey, target.ValueName, target.FilePath, target.ServiceName, target.TaskName, target.Clsid, finding.UserVisibleName })
                .ToLowerInvariant();
        }

        public static List<UserWhitelistEntry> Load(DataStore store)
        {
            try
            {
                string path = store.StateFile(FileName);
                if (!File.Exists(path)) return new List<UserWhitelistEntry>();
                List<UserWhitelistEntry> entries = JsonSerializer.Deserialize<List<UserWhitelistEntry>>(File.ReadAllText(path, Encoding.UTF8));
                return entries == null ? new List<UserWhitelistEntry>() : entries.Where(delegate(UserWhitelistEntry entry) { return entry != null && !string.IsNullOrWhiteSpace(entry.Key); }).ToList();
            }
            catch (Exception ex) { Logger.Error("读取用户白名单失败", ex); return new List<UserWhitelistEntry>(); }
        }

        public static void Save(DataStore store, List<UserWhitelistEntry> entries)
        {
            Directory.CreateDirectory(store.State);
            File.WriteAllText(store.StateFile(FileName), JsonSerializer.Serialize(entries ?? new List<UserWhitelistEntry>()), new UTF8Encoding(false));
        }

        public static bool Add(DataStore store, Finding finding)
        {
            string key = KeyFor(finding);
            if (string.IsNullOrWhiteSpace(key)) return false;
            List<UserWhitelistEntry> entries = Load(store);
            if (entries.Any(delegate(UserWhitelistEntry entry) { return string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase); })) return false;
            entries.Add(new UserWhitelistEntry { Key = key, Name = finding.UserVisibleName, AddedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
            Save(store, entries);
            return true;
        }

        public static bool Remove(DataStore store, Finding finding)
        {
            string key = KeyFor(finding);
            List<UserWhitelistEntry> entries = Load(store);
            int removed = entries.RemoveAll(delegate(UserWhitelistEntry entry) { return string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase); });
            if (removed > 0) Save(store, entries);
            return removed > 0;
        }

        public static void Apply(DataStore store, IEnumerable<Finding> findings)
        {
            HashSet<string> keys = new HashSet<string>(Load(store).Select(delegate(UserWhitelistEntry entry) { return entry.Key; }), StringComparer.OrdinalIgnoreCase);
            foreach (Finding finding in findings)
            {
                if (!keys.Contains(KeyFor(finding))) continue;
                finding.Selected = false;
                finding.Risk = "低";
                finding.Status = "已白名单";
                finding.UserImpact = "用户已主动加入本地白名单；本次仍保留证据展示，不建议处理。";
                finding.ActionKind = "ReportOnly";
            }
        }
    }
    internal static class VendorReviewWriter
    {
        public static string Write(DataStore store, string executablePath)
        {
            string hash;
            using (SHA256 sha = SHA256.Create()) using (FileStream stream = File.OpenRead(executablePath)) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(executablePath);
            string signer = "未检测到有效签名";
            // SYSLIB0057 无功能等价替代：X509CertificateLoader 只能解析 DER/PEM/PFX 证书文件，
            // 无法从可执行文件中提取 Authenticode 签名证书（实测对嵌入签名 exe 全部失败），故保留旧 API。
#pragma warning disable SYSLIB0057
            try { X509Certificate certificate = X509Certificate.CreateFromSignedFile(executablePath); if (certificate != null) signer = certificate.Subject; } catch { }
#pragma warning restore SYSLIB0057
            string path = Path.Combine(store.Reports, "vendor-review-" + store.Timestamp() + ".md");
            string body = "# 安全软件误报复核材料\n\n- 产品：" + AppMeta.ProductName + "\n- 版本：" + AppMeta.Version + "\n- 文件名：" + Path.GetFileName(executablePath) + "\n- SHA-256：`" + hash + "`\n- 签名：" + signer + "\n- 生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n该材料仅用于向安全软件厂商申请复核；程序不包含规避、绕过或对抗安全软件的功能。\n";
            File.WriteAllText(path, body, new UTF8Encoding(false));
            return path;
        }
    }
    internal sealed class ActionTarget
    {
        public string Kind { get; set; }
        public string Hive { get; set; }
        public string View { get; set; }
        public string SubKey { get; set; }
        public string ValueName { get; set; }
        public string FilePath { get; set; }
        public string ServiceName { get; set; }
        public string TaskName { get; set; }
        public string UninstallCommand { get; set; }
        public string IconValue { get; set; }
        public string PresentationCommand { get; set; }
        public string Clsid { get; set; }
        public string SourceSubKey { get; set; }
        public string ExpectedProductName { get; set; }
        public string ExpectedPublisher { get; set; }
        public string ExpectedUninstallCommand { get; set; }
    }

    internal enum ProductRemovalDisposition
    {
        Ignore,
        ReportComponentOnly,
        TargetIndependentProduct
    }

    internal static class ProductRemovalPolicy
    {
        private static readonly string[] StrongIndependentProductMarkers = new string[]
        {
            "360desktop", "desktoplite", "360桌面", "小鸟壁纸", "birdwallpaper", "wallpaper", "壁纸", "画报", "屏保",
            "桌面助手", "桌面整理", "hotnews", "热点资讯", "minipage", "迷你页", "popup", "adcomponent", "adservice",
            "gamecenter", "gamehall", "游戏中心", "游戏大厅", "推广组件", "广告组件"
        };

        private static readonly string[] WeakIndependentProductMarkers = new string[]
        {
            "softmgr", "软件管家", "browser", "浏览器", "tips", "资讯"
        };

        private static readonly string[] AbnormalPersistenceMarkers = new string[]
        {
            "watchdog", "guard", "keeper", "daemon", "popup", "adservice", "adpush", "hotnews", "newsfeed", "minipage",
            "守护", "自动恢复", "弹窗", "广告", "热点", "资讯", "推送"
        };

        public static ProductRemovalDisposition Classify(string displayName, string childName, string installLocation, string displayIcon, string uninstallCommand, bool hidden, bool adOrGuard, bool badComponent)
        {
            string text = string.Join(" ", new string[] { displayName, childName, installLocation, displayIcon }.Where(delegate(string value) { return !string.IsNullOrWhiteSpace(value); }).ToArray()).ToLowerInvariant();
            bool strongIndependentProduct = StrongIndependentProductMarkers.Any(delegate(string marker) { return text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0; });
            bool weakIndependentProduct = WeakIndependentProductMarkers.Any(delegate(string marker) { return text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0; });
            bool independentProduct = strongIndependentProduct || (weakIndependentProduct && (hidden || adOrGuard || badComponent));
            bool hasNamedUninstaller = !string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(uninstallCommand);
            if (independentProduct && hasNamedUninstaller) return ProductRemovalDisposition.TargetIndependentProduct;
            if (hidden && (adOrGuard || badComponent)) return ProductRemovalDisposition.ReportComponentOnly;
            return ProductRemovalDisposition.Ignore;
        }

        public static bool IsAbnormalPersistence(string name, string executablePath, bool badComponent)
        {
            if (badComponent) return true;
            string text = ((name ?? string.Empty) + " " + (executablePath ?? string.Empty)).ToLowerInvariant();
            return AbnormalPersistenceMarkers.Any(delegate(string marker) { return text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0; });
        }
    }

    internal enum ContextMenuDiagnosisDisposition
    {
        Ignore,
        Governed,
        ReportOnly,
        SystemProtected,
        ActionableExtension,
        ActionableCommand
    }

    internal static class ContextMenuDiagnosisPolicy
    {
        public static ContextMenuDiagnosisDisposition Classify(ContextMenuEntry entry, VendorIdentityResult identity)
        {
            if (entry == null || identity == null || !identity.Confirmed || identity.Conflicted) return ContextMenuDiagnosisDisposition.Ignore;
            if (string.Equals(entry.Scene, "命令仓库", StringComparison.OrdinalIgnoreCase)) return ContextMenuDiagnosisDisposition.Ignore;
            bool extension = string.Equals(entry.Type, "Shell 扩展", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
            if (!extension && IsCoreFileTypeVerb(entry.SubKey)) return ContextMenuDiagnosisDisposition.Ignore;
            if (!extension && IsSystemCommand(entry)) return ContextMenuDiagnosisDisposition.SystemProtected;
            if (!entry.Enabled) return ContextMenuDiagnosisDisposition.Governed;
            if (entry.ReadOnly || (extension && string.IsNullOrWhiteSpace(entry.Clsid))) return ContextMenuDiagnosisDisposition.ReportOnly;
            return extension ? ContextMenuDiagnosisDisposition.ActionableExtension : ContextMenuDiagnosisDisposition.ActionableCommand;
        }

        private static bool IsCoreFileTypeVerb(string subKey)
        {
            string value = (subKey ?? string.Empty).TrimEnd('\\');
            int slash = value.LastIndexOf('\\');
            string verb = (slash < 0 ? value : value.Substring(slash + 1)).Trim().ToLowerInvariant();
            return verb == "open" || verb == "edit" || verb == "print" || verb == "printto" || verb == "new" ||
                verb == "runas" || verb == "runasuser" || verb == "play" || verb == "preview";
        }

        private static bool IsSystemCommand(ContextMenuEntry entry)
        {
            string command = (entry == null ? string.Empty : entry.Command ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(command)) return false;
            string lowered = Environment.ExpandEnvironmentVariables(command).ToLowerInvariant();
            string[] systemTokens = new string[]
            {
                @"\windows\system32\",
                @"\windows\syswow64\",
                @"\windows\system\",
                @"\windows\explorer.exe"
            };
            foreach (string token in systemTokens)
            {
                if (lowered.IndexOf(token, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }
    }
    internal sealed class CleanupResult
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Vendor { get; set; }
        public string Category { get; set; }
        public string ActionKind { get; set; }
        public string TechnicalLocation { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public string Backup { get; set; }
        public ActionTarget Target { get; set; }

        [JsonIgnore]
        public Image SoftwareIcon { get; set; }
        // WinUI 行模板直接绑定的图标（页面水合后填充）
        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.ImageSource IconDisplay { get; set; }
        [JsonIgnore]
        public string SoftwareName { get; set; }
        [JsonIgnore]
        public string IdentityConfidence { get; set; }
        [JsonIgnore]
        public string IconSource { get; set; }
        [JsonIgnore]
        public string IdentityExplanation { get; set; }

        public SoftwarePresentationEvidence PresentationEvidence()
        {
            ActionTarget target = Target ?? new ActionTarget();
            return new SoftwarePresentationEvidence
            {
                DeclaredName = Title,
                DeclaredVendor = Vendor,
                IconValue = target.IconValue,
                FilePath = target.FilePath,
                Command = !string.IsNullOrEmpty(target.PresentationCommand) ? target.PresentationCommand : target.UninstallCommand,
                ServiceName = target.ServiceName,
                Clsid = target.Clsid,
                TechnicalLocation = TechnicalLocation
            };
        }

        public void ApplyPresentation(SoftwarePresentation presentation)
        {
            if (presentation == null) return;
            SoftwareIcon = presentation.Icon;
            SoftwareName = presentation.SoftwareName;
            IdentityConfidence = presentation.Confidence;
            IconSource = presentation.IconSource;
            IdentityExplanation = presentation.Explanation;
        }
    }

    internal sealed class CleanupBatch
    {
        public string Id { get; set; }
        public string CreatedAt { get; set; }
        public string Path { get; set; }
        public List<CleanupResult> Results { get; set; }
    }
    internal static class ChineseDisplayText
    {
        public static string CleanupStatus(string status)
        {
            if (string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase)) return "已处理";
            if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)) return "失败";
            if (string.Equals(status, "RestoreFailed", StringComparison.OrdinalIgnoreCase)) return "恢复失败";
            if (string.Equals(status, "Launched", StringComparison.OrdinalIgnoreCase)) return "已打开卸载窗口";
            if (string.Equals(status, "Skipped", StringComparison.OrdinalIgnoreCase)) return "已跳过";
            return string.IsNullOrWhiteSpace(status) ? "未知" : status;
        }

        public static string ContextMenuType(string type)
        {
            if (string.Equals(type, "Shell 命令", StringComparison.OrdinalIgnoreCase)) return "右键命令";
            if (string.Equals(type, "Shell 扩展", StringComparison.OrdinalIgnoreCase)) return "右键扩展";
            if (string.Equals(type, "现代右键扩展", StringComparison.OrdinalIgnoreCase)) return "现代右键扩展";
            if (string.Equals(type, "CommandStore", StringComparison.OrdinalIgnoreCase)) return "命令仓库";
            return string.IsNullOrWhiteSpace(type) ? "未知类型" : type;
        }

        public static string ContextMenuName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            string text = value.Trim();
            if (text.Equals("Open Folder as PyCharm Project", StringComparison.OrdinalIgnoreCase)) return "作为 PyCharm 项目打开文件夹";
            if (text.Equals("Open Folder as Project", StringComparison.OrdinalIgnoreCase)) return "作为项目打开文件夹";
            if (text.Equals("Open Git Bash here", StringComparison.OrdinalIgnoreCase)) return "在此处打开 Git Bash";
            if (text.Equals("Open Git GUI Here", StringComparison.OrdinalIgnoreCase)) return "在此处打开 Git 图形界面";
            if (text.Equals("Open in Windows Terminal", StringComparison.OrdinalIgnoreCase)) return "在 Windows 终端中打开";
            if (text.Equals("Open PowerShell window here", StringComparison.OrdinalIgnoreCase)) return "在此处打开 PowerShell 窗口";
            if (text.Equals("Scan with Microsoft Defender...", StringComparison.OrdinalIgnoreCase) || text.Equals("Scan with Microsoft Defender…", StringComparison.OrdinalIgnoreCase)) return "使用 Microsoft Defender 扫描";
            if (text.Equals("Pin to Quick access", StringComparison.OrdinalIgnoreCase)) return "固定到快速访问";
            if (text.Equals("Unpin from Quick access", StringComparison.OrdinalIgnoreCase)) return "从快速访问取消固定";
            if (text.Equals("Open", StringComparison.OrdinalIgnoreCase)) return "打开";
            if (text.Equals("Edit", StringComparison.OrdinalIgnoreCase)) return "编辑";
            if (text.Equals("Print", StringComparison.OrdinalIgnoreCase)) return "打印";
            if (text.Equals("Share", StringComparison.OrdinalIgnoreCase)) return "共享";
            Match editWith = Regex.Match(text, @"^Edit\s+with\s+(?<app>.+)$", RegexOptions.IgnoreCase);
            if (editWith.Success) return "使用 " + TrimEnglishDecoration(editWith.Groups["app"].Value) + " 编辑";
            Match contextMenu = Regex.Match(text, @"^(?<app>.+?)\s+Context\s+menu$", RegexOptions.IgnoreCase);
            if (contextMenu.Success) return TrimEnglishDecoration(contextMenu.Groups["app"].Value) + " 右键菜单";
            Match openIn = Regex.Match(text, @"^Open\s+(?:Folder\s+)?in\s+(?<app>.+)$", RegexOptions.IgnoreCase);
            if (openIn.Success) return "在 " + TrimEnglishDecoration(openIn.Groups["app"].Value) + " 中打开";
            if (text.StartsWith("Open with ", StringComparison.OrdinalIgnoreCase)) return "使用 " + text.Substring(10).Trim() + " 打开";
            Match scanWith = Regex.Match(text, @"^Scan\s+with\s+(?<app>.+)$", RegexOptions.IgnoreCase);
            if (scanWith.Success) return "使用 " + TrimEnglishDecoration(scanWith.Groups["app"].Value) + " 扫描";
            Match compareWith = Regex.Match(text, @"^Compare\s+with\s+(?<app>.+)$", RegexOptions.IgnoreCase);
            if (compareWith.Success) return "使用 " + TrimEnglishDecoration(compareWith.Groups["app"].Value) + " 比较";
            Match uploadTo = Regex.Match(text, @"^Upload\s+to\s+(?<app>.+)$", RegexOptions.IgnoreCase);
            if (uploadTo.Success) return "上传到 " + TrimEnglishDecoration(uploadTo.Groups["app"].Value);
            Match addTo = Regex.Match(text, @"^Add\s+to\s+(?<target>.+)$", RegexOptions.IgnoreCase);
            if (addTo.Success) return "添加到 " + TrimEnglishDecoration(addTo.Groups["target"].Value);
            Match sendTo = Regex.Match(text, @"^Send\s+to\s+(?<target>.+)$", RegexOptions.IgnoreCase);
            if (sendTo.Success) return "发送到 " + TrimEnglishDecoration(sendTo.Groups["target"].Value);
            return text;
        }

        public static string EnsureChineseContextMenuName(string value, string softwareName, string scene)
        {
            string translated = ContextMenuName(value);
            if (HasChinese(translated)) return translated;
            string software = SoftwareName(softwareName);
            if (!string.IsNullOrWhiteSpace(software) && software != "来源未确认" && software != "正在识别…") return software + "右键菜单";
            string location = string.IsNullOrWhiteSpace(scene) ? "" : scene.Trim();
            return (HasChinese(location) ? location : "第三方软件") + "右键菜单";
        }

        public static string SoftwareName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "来源未确认";
            string text = value.Trim();
            if (HasChinese(text)) return text;
            if (text.IndexOf("WPS Office", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Kingsoft", StringComparison.OrdinalIgnoreCase) >= 0) return "WPS / 金山";
            if (text.IndexOf("PyCharm", StringComparison.OrdinalIgnoreCase) >= 0) return "PyCharm 开发工具";
            if (text.IndexOf("Notepad++", StringComparison.OrdinalIgnoreCase) >= 0) return "Notepad++ 文本编辑器";
            if (text.IndexOf("WinRAR", StringComparison.OrdinalIgnoreCase) >= 0) return "WinRAR 压缩软件";
            if (text.IndexOf("Beyond Compare", StringComparison.OrdinalIgnoreCase) >= 0) return "Beyond Compare 文件比较工具";
            if (text.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("Operating System", StringComparison.OrdinalIgnoreCase) >= 0) return "Windows 系统组件";
            if (text.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0) return "AMD Radeon 显卡软件";
            if (text.Equals("Git", StringComparison.OrdinalIgnoreCase)) return "Git 版本管理工具";
            if (text.Equals("Source", StringComparison.OrdinalIgnoreCase) || text.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return "来源未确认";
            return text + " 软件";
        }

        public static bool HasChinese(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Any(delegate(char character) { return character >= '\u3400' && character <= '\u9fff'; });
        }

        private static string TrimEnglishDecoration(string value)
        {
            return (value ?? string.Empty).Trim().TrimEnd('.', '…').Trim();
        }

        public static string RegistryView(string view)
        {
            if (string.Equals(view, "Registry32", StringComparison.OrdinalIgnoreCase)) return "32 位注册表";
            if (string.Equals(view, "Registry64", StringComparison.OrdinalIgnoreCase)) return "64 位注册表";
            if (string.Equals(view, "Default", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return view ?? string.Empty;
        }

        public static string SystemShortcutName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            Match match = Regex.Match(value, @"^(?<prefix>\d+[a-z]?(?:-\d+)?\s*-\s*)?(?<name>.+)$", RegexOptions.IgnoreCase);
            string prefix = match.Success ? match.Groups["prefix"].Value : string.Empty;
            string name = match.Success ? match.Groups["name"].Value.Trim() : value.Trim();
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Desktop", "桌面" }, { "Run", "运行" }, { "Search", "搜索" }, { "Windows Explorer", "文件资源管理器" },
                { "Control Panel", "控制面板" }, { "Task Manager", "任务管理器" }, { "Computer Management", "计算机管理" },
                { "Disk Management", "磁盘管理" }, { "NetworkStatus", "网络状态" }, { "Network Connections", "网络连接" },
                { "Programs and Features", "程序和功能" }, { "Mobility Center", "移动中心" }, { "Event Viewer", "事件查看器" },
                { "Device Manager", "设备管理器" }, { "Command Prompt", "命令提示符" }
            };
            string translated;
            return names.TryGetValue(name, out translated) ? prefix + translated : value;
        }

        public static string GroupName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            Match match = Regex.Match(value, @"^Group(?<number>\d+)$", RegexOptions.IgnoreCase);
            return match.Success ? "第" + match.Groups["number"].Value + "组" : value;
        }
    }

    internal sealed class ScanErrorReport
    {
        public string StartedAt { get; set; }
        public string FailedAt { get; set; }
        public string ProductVersion { get; set; }
        public string ExecutablePath { get; set; }
        public string ExecutableDirectory { get; set; }
        public string DataDirectory { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public string StackTrace { get; set; }
    }

    internal sealed class ScanWarning
    {
        public string Stage { get; set; }
        public string TechnicalLocation { get; set; }
        public string ErrorType { get; set; }
        public string Message { get; set; }
    }

    internal sealed class ScanEvidenceReport
    {
        public string ScannedAt { get; set; }
        public string ProductVersion { get; set; }
        public int FindingCount { get; set; }
        public int WarningCount { get; set; }
        public List<Finding> Findings { get; set; }
        public List<ScanWarning> Warnings { get; set; }
    }

    internal sealed class RestoreBatchResult
    {
        public int Total { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public List<string> Messages { get; set; }

        public bool AllSucceeded
        {
            get { return Failed == 0; }
        }
    }
    internal static class AdminUtil
    {
        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        // 原版通过 runas 重启自身提权；本应用（TubaWinUi3）在未打包模式启动时已自动提权，
        // 故不再提供重新提权入口。打包模式权限不足时，扫描以只读降级、清理项报错处理。
    }
    internal interface IProgressSink
    {
        void Stage(string text);
        void Finding(Finding finding);
    }

    // 共享 JSON 序列化选项（原版 JavaScriptSerializer 的 MaxJsonLength 对应项）
    internal static class JsonOptions
    {
        public static readonly JsonSerializerOptions Value = new JsonSerializerOptions { MaxDepth = 256 };
    }

}

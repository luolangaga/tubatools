namespace Tubatools.AiJunkCleanup.Models;

/// <summary>用于安全检查的系统目录。测试可以传入自定义根目录，生产环境由 <see cref="CreateDefault"/> 生成。</summary>
internal sealed record JunkSafetyEnvironment(
    string? Windows,
    string? ProgramFiles,
    string? ProgramFilesX86,
    string? ProgramData,
    string? UserProfile,
    string? AppData,
    string? LocalAppData,
    string? RoamingAppData,
    string? Temp,
    string? Desktop,
    string? Documents,
    string? Downloads,
    string? Pictures,
    string? Music,
    string? Videos,
    string? OneDrive,
    string? AppDirectory,
    string? DataDirectory)
{
    internal static JunkSafetyEnvironment CreateDefault(string? dataDirectory = null) => new(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Path.GetTempPath(),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        Environment.GetEnvironmentVariable("OneDrive"),
        Path.GetDirectoryName(Environment.ProcessPath),
        dataDirectory);
}

internal sealed record JunkSafetyResult(
    JunkRisk Risk,
    bool Blocked,
    bool Exists,
    bool IsDirectory,
    string Note,
    string? Escalation,
    string Path = "");

/// <summary>
/// 清单展示前和真正删除前的最后一道防线。AI 的等级只升不降；系统目录、个人目录根、
/// 磁盘根、链接/挂载点本身、工具箱自身目录一律不可删除。
/// 注意：上级目录是链接（例如用户把「下载」重定向到 D:\Download）不等于危险——按真实路径复核后照常放行，
/// 否则这类机器上的「下载」目录会整片变成「不建议」，一条都勾不了。
/// </summary>
internal static class JunkSafety
{
    private static readonly HashSet<string> SensitiveProfileFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ssh", ".gnupg", ".aws", ".azure", ".kube", ".docker", ".config"
    };

    private static readonly HashSet<string> UserDataVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Google", "Microsoft", "Mozilla", "Apple", "Steam", "Discord", "Slack", "Tencent", "WeChat", "Weixin"
    };

    private static readonly HashSet<string> SystemFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr", "bootnxt", "bootsect.bak", "dumpstack.log.tmp"
    };

    internal static JunkSafetyResult Evaluate(JunkPlanItem item, JunkSafetyEnvironment env)
    {
        var aiRisk = item.Risk;

        string full;
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables((item.Path ?? "").Trim().Trim('"'));
            if (expanded.StartsWith(@"\\?\", StringComparison.Ordinal)) expanded = expanded[4..];
            if (!Path.IsPathFullyQualified(expanded)) return Blocked(aiRisk, "路径不是绝对路径：" + item.Path);
            if (expanded.StartsWith(@"\\", StringComparison.Ordinal)) return Blocked(aiRisk, "不支持直接删除网络共享路径。");
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Blocked(aiRisk, "路径格式无效：" + item.Path);
        }

        if (full.Length == 0) return Blocked(aiRisk, "路径为空。");
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || string.Equals(full, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
            return Blocked(aiRisk, "这是磁盘根目录，不能整个删除。");

        var fileName = Path.GetFileName(full);
        if (SystemFiles.Contains(fileName)) return Blocked(aiRisk, "这是系统启动/内存文件，不能直接删除。" );

        var existsFile = File.Exists(full);
        var existsDirectory = Directory.Exists(full);
        if (!existsFile && !existsDirectory) return Blocked(aiRisk, "路径不存在，可能已经被清理。", exists: false);
        var isDirectory = existsDirectory;

        // 待删项目本身就是链接/挂载点：删除它会移除链接本身，或被递归删除穿过去删目标里的内容；必须拒绝。
        if (IsLink(full))
            return Blocked(aiRisk, "这本身就是链接/挂载点，删除会移除链接或穿过它删除目标内容。", exists: true, isDirectory: isDirectory);

        // 上级目录是链接（用户把「下载」「桌面」重定向到别的盘很常见）：展开成真实路径后再校验一次。
        // 真实路径落在受保护位置就拒绝；否则按原路径（用户看到的路径）照常评估，只附一条说明。
        var resolved = ResolveLinkedAncestors(full);
        if (resolved is null)
            return Blocked(aiRisk, "无法确认上级路径是否经过链接/挂载点，已阻止删除。", exists: true, isDirectory: isDirectory);

        var result = EvaluateCore(full, isDirectory, aiRisk, env);
        if (result.Blocked) return result;

        if (!Same(resolved, full))
        {
            var real = EvaluateCore(resolved, isDirectory, aiRisk, env);
            if (real.Blocked)
                return real with { Note = "该路径经链接重定向到「" + resolved + "」。" + real.Note };
            return result with { Note = "该路径经链接重定向到「" + resolved + "」，已按真实路径复核。" + result.Note };
        }

        return result;
    }

    /// <summary>按逻辑路径做风险/保护判定（不处理链接）。</summary>
    private static JunkSafetyResult EvaluateCore(string full, bool isDirectory, JunkRisk aiRisk, JunkSafetyEnvironment env)
    {
        var risk = aiRisk;
        string? escalation = null;

        void Raise(JunkRisk candidate, string note)
        {
            if (candidate <= risk) return;
            risk = candidate;
            escalation = note;
        }

        // 任意磁盘上的系统关键一级目录都要拒绝，避免只保护系统盘而放过第二块盘上的 Windows/Program Files。
        var driveRoot = Path.GetPathRoot(full) ?? "";
        var firstSegment = RelativeTo(full, driveRoot).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (firstSegment.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("Recovery", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("Boot", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("EFI", StringComparison.OrdinalIgnoreCase))
            return Blocked(aiRisk, "这是系统卷目录：" + firstSegment + "，不能删除。", exists: true, isDirectory: isDirectory);

        if (firstSegment.Equals("Windows", StringComparison.OrdinalIgnoreCase))
        {
            var systemWindows = env.Windows ?? "";
            var sameDrive = string.Equals(Path.GetPathRoot(systemWindows), driveRoot, StringComparison.OrdinalIgnoreCase);
            var knownCache = sameDrive &&
                (IsSameOrInside(full, Path.Combine(systemWindows, "Temp")) ||
                 IsSameOrInside(full, Path.Combine(systemWindows, "SoftwareDistribution", "Download")) ||
                 IsSameOrInside(full, Path.Combine(systemWindows, "Prefetch")));
            if (!knownCache)
                return Blocked(aiRisk, "这是 Windows 系统目录，删除可能破坏系统。", exists: true, isDirectory: isDirectory);
        }

        if (firstSegment.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase))
            return Blocked(aiRisk, "这是程序安装目录，删除会破坏已安装软件。", exists: true, isDirectory: isDirectory);

        if (firstSegment.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) &&
            !IsSameOrInside(full, env.ProgramData))
            return Blocked(aiRisk, "这是另一个磁盘上的 ProgramData，不能直接删除。", exists: true, isDirectory: isDirectory);

        if (IsSameOrInside(full, env.AppDirectory))
            return Blocked(aiRisk, "这是图吧工具箱自身目录，不能删除。", exists: true, isDirectory: isDirectory);
        if (IsSameOrInside(full, env.DataDirectory))
            return Blocked(aiRisk, "这是图吧工具箱的数据目录，不能删除。", exists: true, isDirectory: isDirectory);

        var perUserPrograms = Path.Combine(env.LocalAppData ?? "", "Programs");
        if (!string.IsNullOrEmpty(env.LocalAppData) && IsSameOrInside(full, perUserPrograms))
            return Blocked(aiRisk, "这是按用户安装的程序目录，删除会破坏已安装软件。", exists: true, isDirectory: isDirectory);

        // 个人目录根、Windows/Program Files/ProgramData、AppData 根等：整目录不可删。
        foreach (var (name, path) in ProtectedRoots(env))
            if (Same(path, full))
                return Blocked(aiRisk, "这是受保护目录：" + name + "，不能整个删除。", exists: true, isDirectory: isDirectory);

        // 磁盘根下的一级项目：可能是用户留下的意外大文件/旧目录，允许但一律按高风险处理。
        // 系统关键目录已在上面的 ProtectedRoots 与 SystemVolumeFolders 中被拒绝。
        if (IsTopLevel(full))
        {
            if (isDirectory && Path.GetFileName(full).Equals("Windows.old", StringComparison.OrdinalIgnoreCase))
                Raise(JunkRisk.High, "Windows.old 是旧系统备份，删除后无法回退到上一个 Windows 版本。");
            else
                Raise(JunkRisk.High, "位于磁盘根目录下的一级项目，可能是意外留下的大文件或旧目录；请确认它不是正在使用的程序或数据。");
        }

        // Windows 目录：只放行三个已知缓存位置。
        if (IsSameOrInside(full, env.Windows))
        {
            if (IsSameOrInside(full, Path.Combine(env.Windows!, "Temp")))
            {
                Raise(JunkRisk.Low, "Windows 临时目录，可由系统重新创建。");
            }
            else if (IsSameOrInside(full, Path.Combine(env.Windows!, "SoftwareDistribution", "Download")))
            {
                Raise(JunkRisk.Medium, "Windows 更新下载缓存，删除后更新会重新下载。");
            }
            else if (IsSameOrInside(full, Path.Combine(env.Windows!, "Prefetch")))
            {
                Raise(JunkRisk.Medium, "预读取缓存，删除后程序首次启动会稍慢。");
            }
            else
            {
                return Blocked(aiRisk, "这是 Windows 系统目录，删除可能破坏系统。", exists: true, isDirectory: isDirectory);
            }
        }

        if (IsSameOrInside(full, env.ProgramFiles) || IsSameOrInside(full, env.ProgramFilesX86))
            return Blocked(aiRisk, "这是程序安装目录，删除会破坏已安装软件。", exists: true, isDirectory: isDirectory);

        if (IsSameOrInside(full, env.ProgramData))
            Raise(JunkRisk.High, "位于 ProgramData，可能包含其他程序共享的数据。");

        if (IsSameOrInside(full, env.UserProfile))
        {
            var relative = RelativeTo(full, env.UserProfile!);
            var first = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (SensitiveProfileFolders.Contains(first))
                return Blocked(aiRisk, "这是密钥/配置文件目录，不能删除。", exists: true, isDirectory: isDirectory);

            foreach (var (name, path) in PersonalFolders(env))
            {
                if (!IsSameOrInside(full, path)) continue;
                if (Same(path, full))
                    return Blocked(aiRisk, "这是「" + name + "」根目录，不能整个删除。", exists: true, isDirectory: isDirectory);
                // 个人文件夹里的内容至少按中风险处理；整个子目录按高风险。
                if (IsSameOrInside(full, path) && IsDirectChild(full, path!))
                    Raise(isDirectory ? JunkRisk.High : JunkRisk.Medium,
                        "位于「" + name + "」，已提高风险等级，删除前请确认不再需要。");
                else
                    Raise(JunkRisk.Medium, "位于「" + name + "」，已提高风险等级，删除前请确认不再需要。");
                break;
            }

            foreach (var (name, path) in AppDataRoots(env))
            {
                if (!Same(path, full)) continue;
                return Blocked(aiRisk, "这是 " + name + " 根目录，不能整个删除。", exists: true, isDirectory: isDirectory);
            }

            if (IsSameOrInside(full, env.LocalAppData) || IsSameOrInside(full, env.RoamingAppData))
            {
                var appDataRoot = IsSameOrInside(full, env.LocalAppData) ? env.LocalAppData! : env.RoamingAppData!;
                var relativeApp = RelativeTo(full, appDataRoot);
                var segments = relativeApp.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 1)
                {
                    if (!segments[0].Equals("Temp", StringComparison.OrdinalIgnoreCase))
                    {
                        Raise(UserDataVendors.Contains(segments[0]) ? JunkRisk.High : JunkRisk.Medium,
                            "这是应用数据目录的第一层，可能包含登录状态和设置，已提高风险等级。");
                    }
                }
                else if (segments.Length >= 2 && segments[0].Equals("Packages", StringComparison.OrdinalIgnoreCase) && segments.Length == 2)
                {
                    Raise(JunkRisk.High, "这是某个应用包的全部本地数据，删除后应用可能重置。");
                }
                else if (relativeApp.EndsWith(@"\User Data", StringComparison.OrdinalIgnoreCase) ||
                         relativeApp.EndsWith(@"\User Data\Default", StringComparison.OrdinalIgnoreCase))
                {
                    Raise(JunkRisk.High, "这是浏览器用户配置目录，包含书签、密码和登录状态。");
                }
                else if (relativeApp.EndsWith(@"\Profiles", StringComparison.OrdinalIgnoreCase) ||
                         (relativeApp.Contains(@"\Profiles\", StringComparison.OrdinalIgnoreCase) && segments.Length <= 4))
                {
                    Raise(JunkRisk.High, "这是浏览器用户配置目录，包含书签、密码和登录状态。");
                }
            }
        }

        foreach (var (name, path) in SystemVolumeFolders(env))
            if (Same(path, full))
                return Blocked(aiRisk, "这是系统卷目录：" + name + "，不能删除。", exists: true, isDirectory: isDirectory);

        return new JunkSafetyResult(risk, false, true, isDirectory, "", escalation, full);
    }

    private static JunkSafetyResult Blocked(JunkRisk risk, string note, bool exists = true, bool isDirectory = false) =>
        new(risk == JunkRisk.Low ? JunkRisk.High : risk, true, exists, isDirectory, note, null);

    /// <summary>路径自身是不是链接/挂载点。读不到属性时按「是」处理（失败关闭）。</summary>
    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return true; }
    }

    /// <summary>
    /// 把上级目录中的链接/挂载点展开成真实路径：例如「下载」被重定向到 D:\Download 时，
    /// C:\Users\me\Downloads\x 会解析成 D:\Download\x。项目自身是链接时原样返回（由调用方单独拒绝）。
    /// 无法确认（读不到属性或解析失败）返回 null，由调用方按失败关闭处理。
    /// </summary>
    private static string? ResolveLinkedAncestors(string full)
    {
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return full;
        var segments = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            var candidate = Path.Combine(current, segments[index]);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(candidate); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return null; }

            if ((attributes & FileAttributes.ReparsePoint) == 0) { current = candidate; continue; }
            if (index == segments.Length - 1) return full; // 项目自身是链接，交给外层拒绝。

            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(candidate)
                : new FileInfo(candidate);
            FileSystemInfo? target;
            try { target = info.ResolveLinkTarget(returnFinalTarget: true); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return null; }
            if (target is null) return null;
            current = Path.TrimEndingDirectorySeparator(target.FullName);
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    private static IEnumerable<(string Name, string? Path)> ProtectedRoots(JunkSafetyEnvironment env)
    {
        yield return ("Windows 目录", env.Windows);
        yield return ("Program Files", env.ProgramFiles);
        yield return ("Program Files (x86)", env.ProgramFilesX86);
        yield return ("ProgramData", env.ProgramData);
        yield return ("Users 目录", Path.GetDirectoryName(env.UserProfile ?? ""));
        yield return ("用户目录", env.UserProfile);
        var users = Path.GetDirectoryName(env.UserProfile ?? "");
        if (!string.IsNullOrEmpty(users))
        {
            yield return ("公共用户目录", Path.Combine(users, "Public"));
            yield return ("默认用户模板", Path.Combine(users, "Default"));
            yield return ("默认用户模板", Path.Combine(users, "Default User"));
            yield return ("所有用户", Path.Combine(users, "All Users"));
        }
        yield return ("AppData", env.AppData);
        yield return ("AppData\\Local", env.LocalAppData);
        yield return ("AppData\\Roaming", env.RoamingAppData);
        yield return ("回收站", Path.Combine(env.LocalAppData ?? "", "$Recycle.Bin"));
        yield return ("System Volume Information", Path.Combine(Path.GetPathRoot(env.UserProfile ?? "C:\\") ?? "C:\\", "System Volume Information"));
    }

    private static IEnumerable<(string Name, string? Path)> PersonalFolders(JunkSafetyEnvironment env)
    {
        yield return ("桌面", env.Desktop);
        yield return ("文档", env.Documents);
        yield return ("下载", env.Downloads);
        yield return ("图片", env.Pictures);
        yield return ("音乐", env.Music);
        yield return ("视频", env.Videos);
        yield return ("OneDrive", env.OneDrive);
        var users = Path.GetDirectoryName(env.UserProfile ?? "");
        if (!string.IsNullOrEmpty(users)) yield return ("公共目录", Path.Combine(users, "Public"));
    }

    private static IEnumerable<(string Name, string? Path)> AppDataRoots(JunkSafetyEnvironment env)
    {
        yield return ("AppData\\Local\\Packages", Path.Combine(env.LocalAppData ?? "", "Packages"));
        yield return ("AppData\\Local\\Microsoft", Path.Combine(env.LocalAppData ?? "", "Microsoft"));
        yield return ("AppData\\Roaming\\Microsoft", Path.Combine(env.RoamingAppData ?? "", "Microsoft"));
        yield return ("AppData\\Local\\Programs", Path.Combine(env.LocalAppData ?? "", "Programs"));
    }

    private static IEnumerable<(string Name, string? Path)> SystemVolumeFolders(JunkSafetyEnvironment env)
    {
        var root = Path.GetPathRoot(env.UserProfile ?? "C:\\") ?? "C:\\";
        yield return ("$Recycle.Bin", Path.Combine(root, "$Recycle.Bin"));
        yield return ("Recovery", Path.Combine(root, "Recovery"));
        yield return ("Boot", Path.Combine(root, "Boot"));
        yield return ("EFI", Path.Combine(root, "EFI"));
        yield return ("System Volume Information", Path.Combine(root, "System Volume Information"));
        yield return ("Windows\\System32", Path.Combine(env.Windows ?? "", "System32"));
        yield return ("Windows\\SysWOW64", Path.Combine(env.Windows ?? "", "SysWOW64"));
        yield return ("Windows\\WinSxS", Path.Combine(env.Windows ?? "", "WinSxS"));
        yield return ("Windows\\Installer", Path.Combine(env.Windows ?? "", "Installer"));
    }

    private static bool IsTopLevel(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return false;
        var relative = path[root.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relative.Length > 0 && !relative.Contains(Path.DirectorySeparatorChar);
    }

    private static bool IsDirectChild(string path, string parent)
    {
        var relative = RelativeTo(path, parent);
        return relative.Length > 0 && !relative.Contains(Path.DirectorySeparatorChar);
    }

    private static string RelativeTo(string path, string parent)
    {
        var normalizedParent = Path.TrimEndingDirectorySeparator(parent);
        if (path.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase))
            return path[normalizedParent.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return "";
    }

    private static bool Same(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
        return string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrInside(string path, string? parent)
    {
        if (string.IsNullOrEmpty(parent)) return false;
        var normalizedParent = Path.TrimEndingDirectorySeparator(parent);
        var normalizedPath = Path.TrimEndingDirectorySeparator(path);
        if (string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = normalizedParent.EndsWith(Path.DirectorySeparatorChar) ? normalizedParent : normalizedParent + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}

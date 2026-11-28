using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Tubatools.MinecraftLauncher.Services;

internal sealed record JavaRuntime(string Home, string JavaExe, string JavawExe, int Major, string Version, string Arch, string Vendor)
{
    public string Display => $"Java {Version}（{Arch}）";
    public string Detail => $"{Display} · {Home}";
}

/// <summary>Java 检测（注册表 / JAVA_HOME / 常见安装目录 / 游戏自带 runtime），对齐 PCL 的 Java 选择逻辑。</summary>
internal static class McJava
{
    public static IReadOnlyList<JavaRuntime> Detect(string? preferredPath, string? gameDirectory)
    {
        var homes = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddHome(string? home)
        {
            if (string.IsNullOrWhiteSpace(home)) return;
            var normalized = home.Trim().TrimEnd(Path.DirectorySeparatorChar);
            if (seen.Add(normalized)) homes.Add(normalized);
        }

        AddHome(ResolveHome(preferredPath));
        AddHome(Environment.GetEnvironmentVariable("JAVA_HOME"));
        AddHome(Environment.GetEnvironmentVariable("JDK_HOME"));

        foreach (var root in JavaRoots())
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var directory in Directory.EnumerateDirectories(root))
                    if (LooksLikeJavaHome(directory)) AddHome(directory);
            }
            catch
            {
                // 权限不足的目录直接跳过。
            }
        }

        AddRegistryHomes(AddHome);

        if (!string.IsNullOrWhiteSpace(gameDirectory))
        {
            foreach (var candidate in FindBundledRuntimes(Path.Combine(gameDirectory, "runtime")))
                AddHome(candidate);
        }

        var runtimes = new List<JavaRuntime>();
        foreach (var home in homes)
        {
            var runtime = Inspect(home);
            if (runtime is not null) runtimes.Add(runtime);
        }

        runtimes.Sort((left, right) =>
        {
            var compare = right.Major.CompareTo(left.Major);
            return compare != 0 ? compare : string.Compare(right.Version, left.Version, StringComparison.OrdinalIgnoreCase);
        });
        return runtimes;
    }

    public static string? ResolveHome(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (File.Exists(path))
            {
                var directory = Path.GetDirectoryName(path);
                return string.Equals(Path.GetFileName(directory), "bin", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(directory)
                    : directory;
            }

            if (Directory.Exists(path))
            {
                if (File.Exists(Path.Combine(path, "bin", "java.exe"))) return path;
                if (string.Equals(Path.GetFileName(path), "bin", StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(path);
            }
        }
        catch
        {
            // 路径非法时视为未提供。
        }

        return null;
    }

    private static bool LooksLikeJavaHome(string directory)
    {
        try
        {
            return File.Exists(Path.Combine(directory, "bin", "java.exe")) ||
                   File.Exists(Path.Combine(directory, "bin", "javaw.exe"));
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> JavaRoots()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] names =
        [
            "Java", "Eclipse Adoptium", "Eclipse Foundation", "Microsoft", "Zulu", "BellSoft",
            "Amazon Corretto", "Semeru", "AdoptOpenJDK", "SapMachine", "Temurin", "OpenJDK"
        ];

        foreach (var root in new[] { programFiles, programFilesX86 })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            foreach (var name in names)
                yield return Path.Combine(root, name);
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            foreach (var name in new[] { "Eclipse Adoptium", "Microsoft", "Programs\\Eclipse Adoptium", "JetBrains\\Toolbox" })
                yield return Path.Combine(localAppData, name);
        }

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return Path.Combine(userProfile, ".jdks");
            yield return Path.Combine(userProfile, "scoop", "apps");
        }
    }

    private static void AddRegistryHomes(Action<string> add)
    {
        string[] keyPaths =
        [
            @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\JavaSoft\Java Development Kit",
            @"SOFTWARE\JavaSoft\JRE",
            @"SOFTWARE\JavaSoft\JDK"
        ];

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var keyPath in keyPaths)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = baseKey.OpenSubKey(keyPath);
                    if (key is null) continue;
                    foreach (var versionName in key.GetSubKeyNames())
                    {
                        using var versionKey = key.OpenSubKey(versionName);
                        var home = versionKey?.GetValue("JavaHome") as string;
                        if (!string.IsNullOrWhiteSpace(home)) add(home);
                    }
                }
                catch
                {
                    // 忽略注册表读取异常。
                }
            }
        }
    }

    private static IEnumerable<string> FindBundledRuntimes(string runtimeRoot)
    {
        if (!Directory.Exists(runtimeRoot)) yield break;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 5,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(runtimeRoot, "java.exe", options);
        }
        catch
        {
            yield break;
        }

        foreach (var java in files)
        {
            var home = ResolveHome(java);
            if (home is not null) yield return home;
        }
    }

    private static JavaRuntime? Inspect(string home)
    {
        string javaExe = Path.Combine(home, "bin", "java.exe");
        string javawExe = Path.Combine(home, "bin", "javaw.exe");
        if (!File.Exists(javaExe) && !File.Exists(javawExe)) return null;
        if (!File.Exists(javaExe)) javaExe = javawExe;

        var major = 0;
        var version = "";
        var arch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
        var vendor = "";

        try
        {
            var release = Path.Combine(home, "release");
            if (File.Exists(release))
            {
                foreach (var line in File.ReadLines(release))
                {
                    var index = line.IndexOf('=');
                    if (index <= 0) continue;
                    var key = line[..index].Trim().Trim('"');
                    var value = line[(index + 1)..].Trim().Trim('"');
                    switch (key)
                    {
                        case "JAVA_VERSION":
                            version = value;
                            major = McRules.ParseMajorJava(value);
                            break;
                        case "OS_ARCH":
                            arch = value.Contains("64", StringComparison.Ordinal) ? "64-bit" : "32-bit";
                            break;
                        case "IMPLEMENTOR":
                            vendor = value;
                            break;
                    }
                }
            }
        }
        catch
        {
            // 继续尝试执行 java -version。
        }

        if (major == 0)
        {
            var output = QueryJavaVersion(javaExe);
            var match = Regex.Match(output, "version \"([^\"]+)\"");
            if (match.Success) version = match.Groups[1].Value;
            major = McRules.ParseMajorJava(version);
        }

        if (major <= 0) return null;
        if (vendor.Length == 0) vendor = "Java";

        return new JavaRuntime(home, javaExe, File.Exists(javawExe) ? javawExe : javaExe, major, version, arch, vendor);
    }

    private static string QueryJavaVersion(string javaExe)
    {
        try
        {
            var startInfo = new ProcessStartInfo(javaExe, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is null) return "";
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(4000))
            {
                try { process.Kill(true); } catch { }
                return "";
            }

            return error.Wait(1000) ? error.Result + output.Result : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>按 MC 版本推导所需 Java 主版本（JSON 的 javaVersion 优先）。</summary>
    public static int RequiredMajor(JsonObject versionJson, DateTime releaseTime)
    {
        var fromJson = versionJson.Obj("javaVersion")?.Int("majorVersion", 0) ?? 0;
        if (fromJson > 0) return fromJson;
        if (releaseTime >= new DateTime(2024, 4, 2)) return 21;
        if (releaseTime >= new DateTime(2021, 11, 16)) return 17;
        if (releaseTime >= new DateTime(2021, 5, 11)) return 16;
        return 8;
    }

    /// <summary>选择 Java：优先精确主版本，其次不低于需求的最近版本；都没有时退回最高版本并给出提示。</summary>
    public static (JavaRuntime? Runtime, string Note) Select(IReadOnlyList<JavaRuntime> runtimes, int required,
        string? preferredPath = null)
    {
        if (runtimes.Count == 0)
            return (null, $"未检测到 Java {required}，请安装或手动指定 java.exe。");

        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            var preferredHome = ResolveHome(preferredPath);
            var preferred = runtimes.FirstOrDefault(runtime =>
                string.Equals(runtime.Home, preferredHome, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return (preferred, preferred.Major < required ? $"指定的 Java {preferred.Major} 低于推荐的 Java {required}" : "");
        }

        var exact = runtimes.Where(runtime => runtime.Major == required)
            .OrderByDescending(runtime => runtime.Version, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (exact is not null) return (exact, "");

        var above = runtimes.Where(runtime => runtime.Major >= required)
            .OrderBy(runtime => runtime.Major)
            .ThenByDescending(runtime => runtime.Version, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (above is not null)
            return (above, $"推荐 Java {required}，已自动选择 Java {above.Major}");

        var below = runtimes.OrderByDescending(runtime => runtime.Major).First();
        return (below, $"该版本推荐 Java {required}，当前最高只有 Java {below.Major}");
    }
}

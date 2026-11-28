using System.Text;
using System.Text.Json.Nodes;

namespace Tubatools.MinecraftLauncher.Services;

internal sealed record McLaunchContext(
    string GameDirectory,
    string MineCraftDirectory,
    string AssetsRoot,
    string AssetIndexId,
    string? AssetsName,
    string VersionName,
    string VersionType,
    string PlayerName,
    string Uuid,
    string AccessToken,
    string ClassPath,
    string NativesDirectory,
    string LibrariesDirectory,
    int Width,
    int Height,
    string? LoggingConfigPath,
    string CustomGameArgs,
    IReadOnlyList<string> ExtraGameArgs,
    string? Server = null)
{
    public string LauncherName => "TubaWinUi3";
    public string LauncherVersion => "1.0";
    public string LegacyAssetsRoot => Path.Combine(AssetsRoot, "virtual", "legacy");
}

/// <summary>
/// 复刻 PCL 的启动参数生成：新版 arguments.jvm/game、旧版 minecraftArguments、
/// rules 过滤、占位符替换、内存与编码参数。
/// </summary>
internal static class McArguments
{
    public static List<string> BuildJvm(JsonObject merged, McLaunchContext context, int memoryMb, int javaMajor)
    {
        var args = new List<string>();

        if (merged.Arr("arguments", "jvm") is { } jvmArguments)
        {
            Collect(jvmArguments, args, McOsInfo.Current);
        }
        else
        {
            args.Add($"-Djava.library.path={context.NativesDirectory}");
            args.Add("-cp");
            args.Add(context.ClassPath);
        }

        var memoryArgs = new List<string>
        {
            $"-Xmn{Math.Max(128, (int)(memoryMb * 0.15))}m",
            $"-Xmx{memoryMb}m"
        };
        args.InsertRange(0, memoryArgs);

        if (!args.Any(arg => arg.Contains("-Dlog4j2.formatMsgNoLookups", StringComparison.OrdinalIgnoreCase)))
            args.Add("-Dlog4j2.formatMsgNoLookups=true");

        if (javaMajor > 8)
        {
            if (!args.Any(arg => arg.Contains("-Dstdout.encoding=", StringComparison.Ordinal)))
                args.Insert(0, "-Dstdout.encoding=UTF-8");
            if (!args.Any(arg => arg.Contains("-Dstderr.encoding=", StringComparison.Ordinal)))
                args.Insert(0, "-Dstderr.encoding=UTF-8");
        }

        if (javaMajor >= 18 && !args.Any(arg => arg.Contains("-Dfile.encoding=", StringComparison.Ordinal)))
            args.Insert(0, "-Dfile.encoding=COMPAT");

        if (!args.Any(arg => arg.StartsWith("-Djava.library.path=", StringComparison.Ordinal)))
            args.Add($"-Djava.library.path={context.NativesDirectory}");

        if (!args.Any(arg => string.Equals(arg, "-cp", StringComparison.Ordinal) ||
                              string.Equals(arg, "-classpath", StringComparison.Ordinal)))
        {
            args.Add("-cp");
            args.Add(context.ClassPath);
        }

        var loggingArgument = merged.Obj("logging")?.Obj("client")?.Str("argument");
        if (!string.IsNullOrWhiteSpace(context.LoggingConfigPath) && !string.IsNullOrWhiteSpace(loggingArgument))
            args.Add(loggingArgument.Replace("${path}", context.LoggingConfigPath, StringComparison.Ordinal));

        var mainClass = merged["mainClass"]?.ToString();
        if (string.IsNullOrWhiteSpace(mainClass))
            throw new InvalidDataException("版本 JSON 缺少 mainClass，无法启动。");
        args.Add(mainClass);
        return args;
    }

    public static List<string> BuildGame(JsonObject merged, McLaunchContext context, DateTime releaseTime)
    {
        var args = new List<string>();
        if (merged.Arr("arguments", "game") is { } gameArguments)
            Collect(gameArguments, args, McOsInfo.Current);
        else if (merged["minecraftArguments"]?.ToString() is { Length: > 0 } legacy)
            args.AddRange(SplitCommandLine(legacy));

        var hasWidth = args.Any(arg => arg.Contains("--width", StringComparison.Ordinal));
        var hasHeight = args.Any(arg => arg.Contains("--height", StringComparison.Ordinal));
        if (!hasWidth && !hasHeight && releaseTime >= new DateTime(2013, 6, 17))
        {
            args.Add("--width");
            args.Add(context.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Add("--height");
            args.Add(context.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(context.CustomGameArgs))
            args.AddRange(SplitCommandLine(context.CustomGameArgs));
        args.AddRange(context.ExtraGameArgs);

        if (!string.IsNullOrWhiteSpace(context.Server))
        {
            var server = context.Server.Trim();
            if (releaseTime > new DateTime(2023, 4, 4))
            {
                args.Add("--quickPlayMultiplayer");
                args.Add(server);
            }
            else
            {
                var parts = server.Split(':', 2);
                args.Add("--server");
                args.Add(parts[0]);
                args.Add("--port");
                args.Add(parts.Length > 1 ? parts[1] : "25565");
            }
        }

        return args;
    }

    private static void Collect(JsonArray source, List<string> destination, McOsInfo os)
    {
        foreach (var node in source)
        {
            if (node is null) continue;
            if (node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.String)
            {
                destination.Add(value.ToString());
                continue;
            }

            if (node is not JsonObject entry) continue;
            if (!McRules.Check(entry["rules"], os)) continue;
            var valueNode = entry["value"];
            if (valueNode is JsonArray values)
                foreach (var item in values)
                    if (item is not null)
                        destination.Add(item.ToString());
            else if (valueNode is not null)
                destination.Add(valueNode.ToString());
        }
    }

    public static List<string> ApplyPlaceholders(IEnumerable<string> args, McLaunchContext context)
    {
        var map = CreatePlaceholders(context);
        var source = args.ToList();

        // --versionType 为空时整对丢弃（PCL 的 _RemoveEmptyVersionType）。
        if (string.IsNullOrWhiteSpace(context.VersionType))
        {
            for (var i = 0; i < source.Count - 1; i++)
            {
                if (!string.Equals(source[i], "--versionType", StringComparison.Ordinal)) continue;
                if (!string.Equals(source[i + 1], "${version_type}", StringComparison.Ordinal)) continue;
                source.RemoveAt(i + 1);
                source.RemoveAt(i);
                i--;
            }
        }

        var result = new List<string>();
        foreach (var arg in source)
        {
            if (arg.Length == 0) continue;
            var replaced = arg;
            foreach (var (key, value) in map)
                replaced = replaced.Replace(key, value, StringComparison.Ordinal);
            if (replaced.Length == 0) continue;
            result.Add(replaced);
        }

        return result;
    }

    public static Dictionary<string, string> CreatePlaceholders(McLaunchContext context) => new(StringComparer.Ordinal)
    {
        ["${classpath_separator}"] = ";",
        ["${classpath}"] = context.ClassPath,
        ["${natives_directory}"] = context.NativesDirectory,
        ["${library_directory}"] = context.LibrariesDirectory,
        ["${libraries_directory}"] = context.LibrariesDirectory,
        ["${launcher_name}"] = context.LauncherName,
        ["${launcher_version}"] = context.LauncherVersion,
        ["${version_name}"] = context.VersionName,
        ["${version_type}"] = context.VersionType,
        ["${game_directory}"] = context.GameDirectory,
        ["${assets_root}"] = context.AssetsRoot,
        ["${game_assets}"] = context.LegacyAssetsRoot,
        ["${assets_index_name}"] = context.AssetIndexId,
        ["${auth_player_name}"] = context.PlayerName,
        ["${auth_uuid}"] = context.Uuid,
        ["${auth_access_token}"] = context.AccessToken,
        ["${access_token}"] = context.AccessToken,
        ["${auth_session}"] = context.AccessToken,
        ["${user_type}"] = "msa",
        ["${clientid}"] = "",
        ["${auth_xuid}"] = "",
        ["${user_properties}"] = "{}",
        ["${resolution_width}"] = context.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["${resolution_height}"] = context.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    public static List<string> SplitCommandLine(string text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }
}

/// <summary>Windows 命令行转义。</summary>
internal static class McCommandLine
{
    public static string Join(IEnumerable<string> args) =>
        string.Join(' ', args.Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length == 0) return "\"\"";
        if (argument.Length >= 2 && argument[0] == '"' && argument[^1] == '"')
            return argument;
        if (!argument.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\t'))
            return argument;

        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            backslashes = 0;
            builder.Append(c);
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }
}

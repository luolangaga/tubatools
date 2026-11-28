using System.Text.Json.Nodes;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;
using Tubatools.MinecraftLauncher;
using Tubatools.MinecraftLauncher.Services;

namespace TubaWinUi3.Tests;

public sealed class MinecraftLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tubatools-mc-" + Guid.NewGuid().ToString("N"));
    private static readonly McOsInfo Windows = new("windows", false, "10.0.22631");

    public MinecraftLauncherTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    private string GameDirectory => Path.Combine(_root, ".minecraft");

    [Fact]
    public void NativeClassifiers_AreNotAddedToClasspath()
    {
        var libraries = JsonNode.Parse("""
            {"libraries":[
              {"name":"com.mojang:jtracy:1.14.38","downloads":{"artifact":{"path":"com/mojang/jtracy/1.14.38/jtracy-1.14.38.jar","url":"https://libraries.minecraft.net/jtracy.jar","size":1}}},
              {"name":"com.mojang:jtracy:1.14.38:natives-windows","downloads":{"artifact":{"path":"com/mojang/jtracy/1.14.38/jtracy-1.14.38-natives-windows.jar","url":"https://libraries.minecraft.net/jtracy-natives.jar","size":1}},"rules":[{"action":"allow","os":{"name":"windows"}}]}
            ]}
            """)!.AsObject();
        var resolved = McLibraryResolver.Resolve(libraries, _root, Windows);
        Assert.Contains(resolved, library => library.Name == "com.mojang:jtracy:1.14.38" && !library.IsNatives);
        Assert.Contains(resolved, library => library.Name == "com.mojang:jtracy:1.14.38:natives-windows" && library.IsNatives);
        Assert.DoesNotContain(resolved, library => library.LocalPath.Contains("natives-windows", StringComparison.OrdinalIgnoreCase) && !library.IsNatives);
    }

    [Fact]
    public void NativeLibraries_AreExtractedToNativesDirectory()
    {
        var libraries = JsonNode.Parse("""
            {"libraries":[
              {"name":"com.mojang:jtracy:1.14.38:natives-windows","downloads":{"artifact":{"path":"com/mojang/jtracy/1.14.38/jtracy-1.14.38-natives-windows.jar","url":"x","size":1}},"rules":[{"action":"allow","os":{"name":"windows"}}]}
            ]}
            """)!.AsObject();
        var resolved = McLibraryResolver.Resolve(libraries, _root, Windows);
        var native = Assert.Single(resolved.Where(library => library.IsNatives));
        Assert.Contains("natives-windows", native.LocalPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameLanguage_WritesOptions()
    {
        var gameDir = Path.Combine(_root, "language-game");
        McGameLauncher.ApplyGameLanguage(gameDir, "zh_cn");
        Assert.Contains("lang:zh_cn", File.ReadAllLines(Path.Combine(gameDir, "options.txt")));
        File.WriteAllLines(Path.Combine(gameDir, "options.txt"), ["fov:70", "lang:en_us"]);
        McGameLauncher.ApplyGameLanguage(gameDir, "zh_cn");
        var lines = File.ReadAllLines(Path.Combine(gameDir, "options.txt"));
        Assert.Contains("fov:70", lines);
        Assert.Contains("lang:zh_cn", lines);
    }

    [Fact]
    public void ModpackIndex_ParsesDependenciesAndFiles()
    {
        var packPath = Path.Combine(_root, "test.mrpack");
        using (var archive = System.IO.Compression.ZipFile.Open(packPath,
                   System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("modrinth.index.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""
                {"name":"Test Pack","versionId":"1.0.0","dependencies":{"minecraft":"1.20.1","fabric-loader":"0.15.11"},
                 "files":[{"path":"mods/example.jar","downloads":["https://example.invalid/mod.jar"],"hashes":{"sha1":"abc"},"fileSize":12},
                          {"path":"mods/server.jar","downloads":["https://example.invalid/server.jar"],"env":{"client":"unsupported"}}]}
                """);
        }

        var index = McModpackInstaller.ReadIndex(packPath);
        Assert.Equal("Test Pack", index.Name);
        Assert.Equal("1.20.1", index.Minecraft);
        Assert.Equal(McLoaderKind.Fabric, index.Loader);
        Assert.Equal("0.15.11", index.LoaderVersion);
        Assert.Equal(2, index.Files.Count);
        Assert.True(index.Files[1].Unsupported);
    }

    [Fact]
    public void Rules_FilterByOsArchAndFeatures()
    {
        var windowsOnly = JsonNode.Parse("""[{"action":"allow","os":{"name":"windows"}}]""");
        Assert.True(McRules.Check(windowsOnly, Windows));
        Assert.False(McRules.Check(windowsOnly, new McOsInfo("linux", false, "6.0")));

        var linuxDeny = JsonNode.Parse("""
            [{"action":"allow"},{"action":"disallow","os":{"name":"linux"}}]
            """);
        Assert.True(McRules.Check(linuxDeny, Windows));
        Assert.False(McRules.Check(linuxDeny, new McOsInfo("linux", false, "6.0")));

        var versionRule = JsonNode.Parse("""[{"action":"allow","os":{"name":"windows","version":"^10\\."}}]""");
        Assert.True(McRules.Check(versionRule, new McOsInfo("windows", false, "10.0.19045")));
        Assert.False(McRules.Check(versionRule, new McOsInfo("windows", false, "6.1.7601")));

        var archRule = JsonNode.Parse("""[{"action":"allow","os":{"arch":"x86"}}]""");
        Assert.False(McRules.Check(archRule, Windows));
        Assert.True(McRules.Check(archRule, new McOsInfo("windows", true, "10.0")));

        var demoRule = JsonNode.Parse("""[{"action":"allow","features":{"is_demo_user":true}}]""");
        Assert.False(McRules.Check(demoRule, Windows));
        var quickPlay = JsonNode.Parse("""[{"action":"allow","features":{"has_quick_play_singleplayer":true}}]""");
        Assert.False(McRules.Check(quickPlay, Windows));
    }

    [Fact]
    public void JsonMerge_ObjectsRecursiveArraysAppendScalarsOverride()
    {
        var parent = McJson.ParseObject("""
            {"id":"1.20.1","mainClass":"old.Main","libraries":[{"name":"a"}],"arguments":{"game":["--a"]},"assetIndex":{"id":"5"}}
            """);
        var child = McJson.ParseObject("""
            {"mainClass":"new.Main","libraries":[{"name":"b"}],"arguments":{"game":["--b"]},"assetIndex":{"url":"u"}}
            """);
        McJson.Merge(parent, child);

        Assert.Equal("new.Main", parent["mainClass"]!.ToString());
        Assert.Equal(2, (parent["libraries"] as JsonArray)!.Count);
        Assert.Equal("a", parent["libraries"]![0]!["name"]!.ToString());
        Assert.Equal("b", parent["libraries"]![1]!["name"]!.ToString());
        Assert.Equal(2, (parent.Arr("arguments", "game"))!.Count);
        Assert.Equal("5", parent.Obj("assetIndex")!.Str("id"));
        Assert.Equal("u", parent.Obj("assetIndex")!.Str("url"));
    }

    [Fact]
    public void LibraryResolver_ResolvesArtifactsNativesAndRules()
    {
        var json = McJson.ParseObject("""
            {"libraries":[
              {"name":"org.example:demo:1.0","downloads":{"artifact":{"path":"org/example/demo/1.0/demo-1.0.jar","url":"https://libraries.minecraft.net/org/example/demo/1.0/demo-1.0.jar","size":42,"sha1":"abc"}}},
              {"name":"org.lwjgl:lwjgl:3.3.1","natives":{"windows":"natives-windows"},"downloads":{"classifiers":{"natives-windows":{"path":"org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar","url":"https://libraries.minecraft.net/natives.jar","size":9,"sha1":"def"}}}},
              {"name":"org.old:demo:1.0","rules":[{"action":"allow","os":{"name":"linux"}}]},
              {"name":"org.example:demo:1.2","downloads":{"artifact":{"path":"org/example/demo/1.2/demo-1.2.jar","url":"https://libraries.minecraft.net/new.jar","size":43}}},
              {"name":"net.fabricmc:fabric-loader:0.15.0"}
            ]}
            """);

        var libraries = McLibraryResolver.Resolve(json, @"C:\mc", Windows);
        Assert.Equal(3, libraries.Count);
        var demo = libraries.Single(library => library.Name == "org.example:demo:1.2");
        Assert.EndsWith(Path.Combine("org", "example", "demo", "1.2", "demo-1.2.jar"), demo.LocalPath);
        var native = libraries.Single(library => library.IsNatives);
        Assert.EndsWith("lwjgl-3.3.1-natives-windows.jar", native.LocalPath);
        Assert.Equal(9, native.Size);
        Assert.Equal("net.fabricmc:fabric-loader:0.15.0",
            libraries.Single(library => library.Name.StartsWith("net.fabricmc", StringComparison.Ordinal)).Name);
        Assert.Equal("org/example/demo/1.0/demo-1.0.jar", McLibraryResolver.MavenRelativePath("org.example:demo:1.0"));
    }

    [Fact]
    public void Arguments_ReplacePlaceholdersAndBuildMemoryAndClasspath()
    {
        var context = CreateContext();
        var json = McJson.ParseObject("""
            {
              "mainClass":"net.minecraft.client.main.Main",
              "arguments":{
                "jvm":[
                  {"rules":[{"action":"allow","os":{"name":"windows"}}],"value":["-Djava.library.path=${natives_directory}","-cp","${classpath}"]},
                  {"rules":[{"action":"allow","os":{"name":"linux"}}],"value":["-Dlinux=1"]}
                ],
                "game":[
                  "--username","${auth_player_name}",
                  {"rules":[{"action":"allow","os":{"name":"windows"}}],"value":["--demo-windows"]},
                  {"rules":[{"action":"allow","os":{"name":"linux"}}],"value":["--demo-linux"]},
                  "--versionType","${version_type}"
                ]
              }
            }
            """);

        var jvm = McArguments.BuildJvm(json, context, 4096, 17);
        Assert.Equal("net.minecraft.client.main.Main", jvm[^1]);
        Assert.Contains("-Xmx4096m", jvm);
        Assert.Contains("-Dlog4j2.formatMsgNoLookups=true", jvm);
        Assert.Contains("-Dstdout.encoding=UTF-8", jvm);
        Assert.Contains("-cp", jvm);
        Assert.Contains("${classpath}", jvm);
        Assert.DoesNotContain("-Dlinux=1", jvm);

        var game = McArguments.BuildGame(json, context, new DateTime(2014, 1, 1));
        var replaced = McArguments.ApplyPlaceholders(game, context);
        Assert.Contains("Steve", replaced);
        Assert.Contains("--demo-windows", replaced);
        Assert.DoesNotContain("--demo-linux", replaced);
        Assert.DoesNotContain("--versionType", replaced);
        Assert.Contains("--width", replaced);
        Assert.Contains("1920", replaced);
        Assert.Contains(context.ClassPath, McArguments.ApplyPlaceholders(jvm, context));
    }

    [Fact]
    public void Arguments_LegacyMinecraftArgumentsStillWork()
    {
        var context = CreateContext();
        var json = McJson.ParseObject("""
            {"minecraftArguments":"--username ${auth_player_name} --assetsDir ${game_assets} --version ${version_name}"}
            """);
        var args = McArguments.ApplyPlaceholders(McArguments.BuildGame(json, context, new DateTime(2012, 3, 1)), context);
        Assert.Contains("Steve", args);
        Assert.Contains(context.LegacyAssetsRoot, args);
        Assert.Contains("1.20.1", args);
        Assert.DoesNotContain("--width", args);
    }

    [Fact]
    public void CommandLine_QuotesArgumentsWithSpaces()
    {
        Assert.Equal("\"C:\\a b\\c\"", McCommandLine.Quote(@"C:\a b\c"));
        Assert.Equal(@"C:\mc", McCommandLine.Quote(@"C:\mc"));
        Assert.Equal("\"a\\\"b\"", McCommandLine.Quote("a\"b"));
        Assert.Equal("a b c", McCommandLine.Join(["a", "b", "c"]));
    }

    [Fact]
    public void OfflineUuid_MatchesMinecraftOfflineAlgorithm()
    {
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", McOfflineUuid.Create("Notch"));
        Assert.Equal("a01e3843-e521-3998-958a-f459800e4d11", McOfflineUuid.Create("Player"));
        Assert.True(McOfflineUuid.IsValidName("Steve_01"));
        Assert.False(McOfflineUuid.IsValidName("ab"));
        Assert.False(McOfflineUuid.IsValidName("张三"));
        Assert.False(McOfflineUuid.IsValidName("has space"));
    }

    [Fact]
    public void Java_RequiredMajorAndSelection()
    {
        Assert.Equal(17, McJava.RequiredMajor(McJson.ParseObject("""{"javaVersion":{"majorVersion":17}}"""), DateTime.MinValue));
        Assert.Equal(21, McJava.RequiredMajor(new JsonObject(), new DateTime(2024, 6, 1)));
        Assert.Equal(17, McJava.RequiredMajor(new JsonObject(), new DateTime(2022, 1, 1)));
        Assert.Equal(8, McJava.RequiredMajor(new JsonObject(), new DateTime(2019, 1, 1)));

        Assert.Equal(8, McRules.ParseMajorJava("1.8.0_382"));
        Assert.Equal(21, McRules.ParseMajorJava("21.0.1"));
        Assert.Equal(0, McRules.ParseMajorJava(""));

        var runtimes = new List<JavaRuntime>
        {
            new(@"C:\j8", @"C:\j8\bin\java.exe", @"C:\j8\bin\javaw.exe", 8, "1.8.0_382", "64-bit", "Oracle"),
            new(@"C:\j17", @"C:\j17\bin\java.exe", @"C:\j17\bin\javaw.exe", 17, "17.0.10", "64-bit", "Microsoft"),
            new(@"C:\j21", @"C:\j21\bin\java.exe", @"C:\j21\bin\javaw.exe", 21, "21.0.1", "64-bit", "Microsoft")
        };
        Assert.Equal(17, McJava.Select(runtimes, 17).Runtime!.Major);
        Assert.Equal(21, McJava.Select(runtimes, 18).Runtime!.Major);
        Assert.Equal(21, McJava.Select(runtimes, 21).Runtime!.Major);
        var fallback = McJava.Select(runtimes, 99);
        Assert.Equal(21, fallback.Runtime!.Major);
        Assert.NotEmpty(fallback.Note);
        Assert.Null(McJava.Select([], 17).Runtime);
    }

    [Fact]
    public void Mirror_RewritesOfficialUrlsAndProvidesFallback()
    {
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar",
            McMirror.Rewrite("https://libraries.minecraft.net/org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar", McMirror.Auto));
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/assets/ab/abcdef",
            McMirror.Rewrite("https://resources.download.minecraft.net/ab/abcdef", McMirror.Bmclapi));
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/v1/packages/abc/5.json",
            McMirror.Rewrite("https://piston-meta.mojang.com/v1/packages/abc/5.json", McMirror.Auto));
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/v1/packages/abc/pre-1.6.json",
            McMirror.Rewrite("https://launchermeta.mojang.com/v1/packages/abc/pre-1.6.json", McMirror.Auto));
        Assert.Null(McMirror.Rewrite("https://libraries.minecraft.net/x", McMirror.Official));
        Assert.Equal("https://bmclapi2.bangbang93.com/version/1.20.1/json",
            McMirror.VersionJsonUrl("1.20.1", McMirror.Auto));

        var (primary, fallback) = McMirror.Resolve("https://resources.download.minecraft.net/ab/abcdef", McMirror.Auto);
        Assert.StartsWith(McMirror.BmclapiHost, primary);
        Assert.Equal("https://resources.download.minecraft.net/ab/abcdef", fallback);
        var official = McMirror.Resolve("https://libraries.minecraft.net/x", McMirror.Official);
        Assert.Equal("https://libraries.minecraft.net/x", official.Primary);
        Assert.Null(official.Fallback);
    }

    [Fact]
    public void MirrorHealth_StopsTryingMirrorAfterRepeatedFailures()
    {
        var mirror = McMirror.BmclapiHost + "/assets/ab/abcdef";
        McMirrorHealth.ReportSuccess(mirror);
        Assert.False(McMirrorHealth.MirrorDown);
        McMirrorHealth.ReportFailure(mirror);
        McMirrorHealth.ReportFailure(mirror);
        Assert.False(McMirrorHealth.MirrorDown);
        McMirrorHealth.ReportFailure(mirror);
        Assert.True(McMirrorHealth.MirrorDown);
        McMirrorHealth.ReportSuccess(mirror);
        Assert.False(McMirrorHealth.MirrorDown);
        Assert.True(McMirrorHealth.IsMirror(mirror));
        Assert.False(McMirrorHealth.IsMirror("https://libraries.minecraft.net/x"));
    }

    [Fact]
    public void Fabric_ParsesLoadersAndProfileIdentity()
    {
        var loaders = McFabric.ParseLoaders("""
            [{"loader":{"version":"0.16.9","stable":true},"intermediary":{"version":"1.20.1"}},
             {"loader":{"version":"0.17.0-beta.2","stable":false}}]
            """);
        Assert.Equal(2, loaders.Length);
        Assert.Equal("0.16.9", loaders[0].Version);
        Assert.True(loaders[0].Stable);
        Assert.False(loaders[1].Stable);

        var profile = McJson.ParseObject("""{"id":"fabric-loader-0.16.9-1.20.1","inheritsFrom":"1.20.1"}""");
        Assert.Equal("fabric-loader-0.16.9-1.20.1", McFabric.GetProfileId(profile, "1.20.1", "0.16.9"));
        Assert.Equal("fabric-loader-0.16.9-1.20.1",
            McFabric.GetProfileId(new JsonObject(), "1.20.1", "0.16.9"));
    }

    [Fact]
    public void LoaderHelpers_ParseMavenMetadataAndNeoForgePrefixes()
    {
        const string xml = """
            <metadata><versioning><versions>
              <version>1.20.1-47.4.0</version>
              <version>1.20.1-47.3.0</version>
              <version>1.19.2-43.2.0</version>
            </versions></versioning></metadata>
            """;
        var forge = McLoaderInstaller.ParseMavenVersions(xml, "1.20.1-");
        Assert.Equal(["47.4.0", "47.3.0"], forge);
        Assert.Empty(McLoaderInstaller.ParseMavenVersions(xml, "1.21-"));
        Assert.Equal("20.2.", McLoaderInstaller.NeoForgePrefix("1.20.2"));
        Assert.Equal("21.1.", McLoaderInstaller.NeoForgePrefix("1.21.1"));
        Assert.Equal("21.10.", McLoaderInstaller.NeoForgePrefix("1.21.10"));
        Assert.Null(McLoaderInstaller.NeoForgePrefix("1.20.1"));
        Assert.Equal("Fabric", McLoaderInstaller.DisplayName(McLoaderKind.Fabric));
        Assert.Equal("原版", McLoaderInstaller.DisplayName(McLoaderKind.Vanilla));
    }

    [Fact]
    public void Modrinth_FormatsDownloadCounts()
    {
        Assert.Equal("999", McModrinth.FormatDownloads(999));
        Assert.Equal("1.2万", McModrinth.FormatDownloads(12_000));
        Assert.Equal("1.2亿", McModrinth.FormatDownloads(120_000_000));
    }

    [Fact]
    public void Manifest_ParsesLatestAndVersions()
    {
        var manifest = McVersionManifestClient.Parse("""
            {"latest":{"release":"1.21.4","snapshot":"25w02a"},"versions":[
              {"id":"1.21.4","type":"release","url":"https://piston-meta.mojang.com/v1/packages/a/1.21.4.json","time":"2024-12-03T10:12:04+00:00","releaseTime":"2024-12-03T10:12:04+00:00","sha1":"abc"},
              {"id":"25w02a","type":"snapshot","url":"https://piston-meta.mojang.com/v1/packages/b/25w02a.json","releaseTime":"2025-01-08T12:00:00+00:00","sha1":"def"}
            ]}
            """);
        Assert.Equal("1.21.4", manifest.LatestRelease);
        Assert.Equal("25w02a", manifest.LatestSnapshot);
        Assert.Equal(2, manifest.Versions.Count);
        Assert.Equal("release", manifest.Versions[0].Type);
        Assert.True(manifest.Versions[0].ReleaseTime > new DateTime(2024, 1, 1));
    }

    [Fact]
    public void InstallPlanner_BuildsClientLibrariesAndAssetIndex()
    {
        var versionJson = McJson.ParseObject("""
            {
              "downloads":{"client":{"url":"https://piston-data.mojang.com/v1/objects/abc/client.jar","sha1":"c1","size":10}},
              "libraries":[{"name":"org.example:demo:1.0","downloads":{"artifact":{"path":"org/example/demo/1.0/demo-1.0.jar","url":"https://libraries.minecraft.net/demo.jar","size":5,"sha1":"l1"}}}],
              "assetIndex":{"id":"5","url":"https://piston-meta.mojang.com/v1/packages/aa/5.json","sha1":"i1","size":20},
              "logging":{"client":{"file":{"id":"client-1.12.xml","url":"https://piston-data.mojang.com/log.xml","sha1":"g1","size":3}}}
            }
            """);

        var plan = McInstallPlanner.Plan(versionJson, "1.20.1", @"C:\mc", @"C:\mc\versions\1.20.1", McMirror.Auto, Windows);
        Assert.Equal(4, plan.Files.Count);
        var client = plan.Files[0];
        Assert.Equal("https://bmclapi2.bangbang93.com/version/1.20.1/client", client.Url);
        Assert.Equal("https://piston-data.mojang.com/v1/objects/abc/client.jar", client.FallbackUrl);
        Assert.EndsWith(Path.Combine("versions", "1.20.1", "1.20.1.jar"), client.TargetPath);
        var library = plan.Files.Single(item => item.Label == "org.example:demo:1.0");
        Assert.EndsWith(Path.Combine("libraries", "org", "example", "demo", "1.0", "demo-1.0.jar"), library.TargetPath);
        Assert.Equal(@"C:\mc\assets\indexes\5.json", plan.AssetIndexPath);
        Assert.EndsWith(Path.Combine("assets", "log_configs", "client-1.12.xml"), plan.LoggingConfigPath);
        var indexItem = plan.Files.Single(item => item.Label.StartsWith("资源索引", StringComparison.Ordinal));
        Assert.Contains("/v1/packages/", indexItem.Url);
    }

    [Fact]
    public void InstallPlanner_PlansAssetObjectsWithObjectLayout()
    {
        var index = McJson.ParseObject("""
            {"objects":{
              "icons/icon.png":{"hash":"1111111111111111111111111111111111111111","size":4},
              "sounds/a.ogg":{"hash":"2222222222222222222222222222222222222222","size":9}
            }}
            """);
        var objectDirectory = Path.Combine(GameDirectory, "assets", "objects", "11");
        Directory.CreateDirectory(objectDirectory);
        File.WriteAllText(Path.Combine(objectDirectory, "1111111111111111111111111111111111111111"), "abcd");

        var assets = McInstallPlanner.PlanAssets(index, GameDirectory, McMirror.Bmclapi);
        // 已存在的文件也进入清单，由下载器按大小/SHA-1 决定跳过或修复。
        Assert.Equal(2, assets.Count);
        var planned = assets.Single(item => item.Label.Contains("sounds/a.ogg", StringComparison.Ordinal));
        Assert.EndsWith(Path.Combine("22", "2222222222222222222222222222222222222222"), planned.TargetPath);
        Assert.Equal("https://bmclapi2.bangbang93.com/assets/22/2222222222222222222222222222222222222222", planned.Url);
        Assert.Equal(9, planned.Size);
        Assert.Equal((true, false), McInstallPlanner.GetLegacyMode(McJson.ParseObject("""{"virtual":true}""")));
        Assert.Equal((false, true), McInstallPlanner.GetLegacyMode(McJson.ParseObject("""{"map_to_resources":true}""")));
    }

    [Fact]
    public void VersionStore_MergesInheritanceAndDetectsLoaders()
    {
        var versions = Path.Combine(GameDirectory, "versions");
        var vanilla = Path.Combine(versions, "1.20.1");
        var forge = Path.Combine(versions, "1.20.1-forge-47.2.0");
        var quilt = Path.Combine(versions, "quilt-loader-0.20.0-1.20.1");
        Directory.CreateDirectory(vanilla);
        Directory.CreateDirectory(forge);
        Directory.CreateDirectory(quilt);
        File.WriteAllText(Path.Combine(vanilla, "1.20.1.jar"), "fake jar");
        File.WriteAllText(Path.Combine(vanilla, "1.20.1.json"), """
            {"id":"1.20.1","type":"release","releaseTime":"2023-06-12T10:00:00+00:00","mainClass":"net.minecraft.client.main.Main",
             "assets":"5","assetIndex":{"id":"5","url":"https://piston-meta.mojang.com/v1/packages/aa/5.json"},
             "libraries":[{"name":"org.example:base:1.0"}],
             "arguments":{"game":["--username","${auth_player_name}"],"jvm":["-cp","${classpath}"]}}
            """);
        File.WriteAllText(Path.Combine(forge, "1.20.1-forge-47.2.0.json"), """
            {"id":"1.20.1-forge-47.2.0","inheritsFrom":"1.20.1",
             "mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
             "libraries":[{"name":"net.minecraftforge:forge:1.20.1-47.2.0"}],
             "arguments":{"game":["--fml.mcVersion","1.20.1"]}}
            """);
        File.WriteAllText(Path.Combine(quilt, "quilt-loader-0.20.0-1.20.1.json"), """
            {"id":"quilt-loader-0.20.0-1.20.1","inheritsFrom":"1.20.1",
             "mainClass":"org.quiltmc.loader.impl.launch.knot.KnotClient",
             "libraries":[{"name":"org.quiltmc:quilt-loader:0.20.0"}]}
            """);
        var broken = Path.Combine(versions, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "broken.json"), "{not json");

        var scan = McVersionStore.Scan(GameDirectory);
        Assert.Equal(3, scan.Versions.Count);
        Assert.Single(scan.Problems);
        Assert.Contains("broken", scan.Problems[0]);

        var forgeVersion = scan.Versions.Single(version => version.Id == "1.20.1-forge-47.2.0");
        Assert.Equal("1.20.1", forgeVersion.VanillaName);
        Assert.True(forgeVersion.HasForge);
        Assert.Contains("Forge", forgeVersion.Loaders);
        Assert.Equal("cpw.mods.bootstraplauncher.BootstrapLauncher", forgeVersion.MainClass);
        Assert.Equal("5", forgeVersion.AssetIndexId);
        Assert.True(forgeVersion.HasClientJar);
        Assert.EndsWith(Path.Combine("versions", "1.20.1", "1.20.1.jar"), forgeVersion.ClientJarPath);
        Assert.Equal(2, forgeVersion.Json.Arr("libraries")!.Count);
        Assert.Equal(4, forgeVersion.Json.Arr("arguments", "game")!.Count);

        var vanillaVersion = scan.Versions.Single(version => version.Id == "1.20.1");
        Assert.True(vanillaVersion.IsComplete);
        Assert.Equal("release", vanillaVersion.Type);
        Assert.False(vanillaVersion.HasForge);

        var quiltVersion = scan.Versions.Single(version => version.Id == "quilt-loader-0.20.0-1.20.1");
        Assert.Equal("1.20.1", quiltVersion.VanillaName);
        Assert.Contains("Quilt", quiltVersion.Loaders);
        Assert.True(quiltVersion.HasFabric);
        Assert.Equal("org.quiltmc.loader.impl.launch.knot.KnotClient", quiltVersion.MainClass);
    }

    [Fact]
    public void Settings_RoundTripAndClamp()
    {
        var settings = LauncherSettings.Load(_root);
        Assert.Equal(LauncherSettings.DefaultGameDirectory, settings.GameDirectory);
        settings.GameDirectory = GameDirectory;
        settings.MemoryMb = 2048;
        settings.VersionIsolation = false;
        settings.JvmArgs = "-Dtest=1";
        settings.WindowWidth = 1280;
        settings.WindowHeight = 720;
        settings.Mirror = "bmclapi";
        settings.Save();

        var reloaded = LauncherSettings.Load(_root);
        Assert.Equal(GameDirectory, reloaded.GameDirectory);
        Assert.Equal(2048, reloaded.MemoryMb);
        Assert.False(reloaded.VersionIsolation);
        Assert.Equal("-Dtest=1", reloaded.JvmArgs);
        Assert.Equal(1280, reloaded.WindowWidth);
        Assert.Equal("bmclapi", reloaded.Mirror);

        File.WriteAllText(Path.Combine(_root, "settings.json"), "broken");
        var repaired = LauncherSettings.Load(_root);
        Assert.Equal(LauncherSettings.DefaultGameDirectory, repaired.GameDirectory);
        Assert.InRange(repaired.MemoryMb, 512, 32768);
    }

    [Fact]
    public void Accounts_AddSelectRemoveAndPersist()
    {
        var store = new McAccountStore(_root);
        Assert.Empty(store.Accounts);
        Assert.Throws<ArgumentException>(() => store.Add("ab"));

        var steve = store.Add("Steve");
        Assert.Equal("5627dd98-e6be-3c21-b8a8-e92344183641", steve.Uuid);
        Assert.Same(steve, store.Selected);

        var alex = store.Add("Alex");
        Assert.Equal(2, store.Accounts.Count);
        Assert.Same(alex, store.Selected);
        store.Select(steve);
        Assert.Same(steve, store.Selected);

        var reloaded = new McAccountStore(_root);
        Assert.Equal(2, reloaded.Accounts.Count);
        Assert.Equal("Steve", reloaded.Selected!.Name);

        reloaded.Remove(steve);
        Assert.Single(reloaded.Accounts);
        Assert.Equal("Alex", reloaded.Selected!.Name);
    }

    [Fact]
    public void VersionIsolation_UsesVersionDirectory()
    {
        var settings = new LauncherSettings { VersionIsolation = true, GameDirectory = @"C:\mc" };
        var version = new McVersion
        {
            Id = "1.20.1",
            Directory = @"C:\mc\versions\1.20.1",
            Json = new JsonObject()
        };
        Assert.Equal(@"C:\mc\versions\1.20.1", McVersionStore.ResolveGameDirectory(version, settings));
        settings.VersionIsolation = false;
        Assert.Equal(@"C:\mc", McVersionStore.ResolveGameDirectory(version, settings));
    }

    private static McLaunchContext CreateContext() => new(
        GameDirectory: @"C:\mc\versions\1.20.1",
        MineCraftDirectory: @"C:\mc",
        AssetsRoot: @"C:\mc\assets",
        AssetIndexId: "5",
        AssetsName: "5",
        VersionName: "1.20.1",
        VersionType: "",
        PlayerName: "Steve",
        Uuid: "5627dd98-e6be-3c21-b8a8-e92344183641",
        AccessToken: "5627dd98-e6be-3c21-b8a8-e92344183641",
        ClassPath: @"C:\mc\libraries\a.jar;C:\mc\versions\1.20.1\1.20.1.jar",
        NativesDirectory: @"C:\mc\versions\1.20.1\natives",
        LibrariesDirectory: @"C:\mc\libraries",
        Width: 1920,
        Height: 1080,
        LoggingConfigPath: null,
        CustomGameArgs: "",
        ExtraGameArgs: []);
}

[Collection("BuiltinToolRegistry")]
public sealed class MinecraftLauncherPluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tubatools-mc-plugin-" + Guid.NewGuid().ToString("N"));

    public MinecraftLauncherPluginTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        BuiltinToolRegistry.ReplacePluginTools("mc-test", []);
        PluginManager.ClearOpenEntry("mc-test");
        try { Directory.Delete(_root, true); }
        catch { }
    }

    [Fact]
    public void Plugin_RegistersOneMultiFeatureLauncherTool()
    {
        Directory.CreateDirectory(Path.Combine(_root, "assets"));
        File.WriteAllText(Path.Combine(_root, "assets", "icon.svg"),
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128"><rect width="128" height="128" fill="#1370F3"/></svg>""");
        var host = new PluginManager.RegistrationHost("mc-test", _root, new(2, "entry.dll", "Entry"));
        new MinecraftLauncherPlugin().Register(host);
        host.Commit();

        var tool = Assert.IsType<PluginXamlTool>(BuiltinToolRegistry.GetById("plugin:mc-test:minecraft-launcher"));
        Assert.Equal("游戏工具", tool.Category);
        Assert.Contains("Minecraft", tool.Definition.Tags);
        Assert.NotNull(tool.Definition.CreateContent);
        Assert.Null(tool.Definition.Xaml);
        Assert.True(PluginManager.HasOpenEntry("mc-test"));
    }
}

public sealed class MinecraftMicrosoftAccountTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tubatools-mc-msa-" + Guid.NewGuid().ToString("N"));

    public MinecraftMicrosoftAccountTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    [Fact]
    public void MicrosoftTokens_AreEncryptedAtRest_AndRoundTrip()
    {
        var plain = System.Text.Encoding.UTF8.GetBytes("mc-access-token-123");
        var protectedBytes = McDpapi.Transform(plain, true);
        var decrypted = System.Text.Encoding.UTF8.GetString(McDpapi.Transform(protectedBytes, false));
        Assert.Equal("mc-access-token-123", decrypted);

        var store = new McAccountStore(_root);
        var session = new McMicrosoftSession(
            "mc-access-token-123", "ms-refresh-token-456",
            DateTime.UtcNow.AddHours(1), "0123456789abcdef0123456789abcdef", "Notch");
        var account = store.UpsertMicrosoft(session);
        Assert.True(account.IsMicrosoft);
        Assert.Equal("微软正版", account.TypeLabel);

        var path = Path.Combine(_root, "accounts.json");
        var raw = File.ReadAllText(path);
        Assert.DoesNotContain("mc-access-token-123", raw);
        Assert.DoesNotContain("ms-refresh-token-456", raw);
        Assert.Contains("dpapi:v1:", raw);

        var reloaded = new McAccountStore(_root);
        var loaded = Assert.Single(reloaded.Accounts);
        Assert.True(loaded.IsMicrosoft);
        Assert.Equal(session.AccessToken, loaded.AccessToken);
        Assert.Equal(session.RefreshToken, loaded.RefreshToken);
        Assert.Equal(session.Uuid, loaded.Uuid);
        Assert.Equal(session.Name, loaded.Name);
    }

    [Fact]
    public async Task DeviceCode_FallsBackWhenPrimaryClientIsNotPublic()
    {
        var calls = 0;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            calls++;
            var body = await request.Content!.ReadAsStringAsync();
            if (calls == 1)
            {
                Assert.Contains(McMicrosoftAuth.UserClientId, body);
                return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(
                        """{"error":"invalid_client","error_description":"AADSTS70002: not a mobile client"}""")
                };
            }

            Assert.Contains(McMicrosoftAuth.FallbackClientId, body);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"device_code":"dev","user_code":"ABCD-EFGH","verification_uri":"https://www.microsoft.com/link","expires_in":900,"interval":5}""")
            };
        }));

        var code = await McMicrosoftAuth.StartDeviceCodeAsync(http, CancellationToken.None,
            [McMicrosoftAuth.UserClientId, McMicrosoftAuth.FallbackClientId]);
        Assert.Equal(McMicrosoftAuth.FallbackClientId, code.ClientId);
        Assert.Equal("ABCD-EFGH", code.UserCode);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task MicrosoftLogin_CompletesXboxAndMinecraftChain()
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            var json = url switch
            {
                var u when u.Contains("oauth2/v2.0/token") =>
                    """{"access_token":"ms-access","refresh_token":"ms-refresh","expires_in":3600}""",
                var u when u.Contains("user.auth.xboxlive.com") =>
                    """{"Token":"xbl-token","DisplayClaims":{"xui":[{"uhs":"uhs-xbl"}]}}""",
                var u when u.Contains("xsts.auth.xboxlive.com") =>
                    """{"Token":"xsts-token","DisplayClaims":{"xui":[{"uhs":"uhs-xsts"}]}}""",
                var u when u.Contains("login_with_xbox") =>
                    """{"access_token":"mc-access","expires_in":86400}""",
                var u when u.Contains("minecraft/profile") =>
                    """{"id":"abc123def456","name":"Player"}""",
                _ => throw new InvalidOperationException("unexpected URL " + url)
            };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }));

        var code = new McDeviceCode("device", "ABCD", "https://www.microsoft.com/link",
            TimeSpan.Zero, TimeSpan.FromMinutes(5), "test-client");
        var session = await McMicrosoftAuth.CompleteLoginAsync(http, code, null, CancellationToken.None);
        Assert.Equal("mc-access", session.AccessToken);
        Assert.Equal("ms-refresh", session.RefreshToken);
        Assert.Equal("abc123def456", session.Uuid);
        Assert.Equal("Player", session.Name);
        Assert.Equal("test-client", session.ClientId);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }

    [Fact]
    public void CustomMicrosoftClientId_OverridesBuiltInClient()
    {
        Assert.Equal(McMicrosoftAuth.UserClientId, McMicrosoftAuth.ResolveClientIds(null)[0]);
        Assert.Equal("11111111-2222-3333-4444-555555555555",
            McMicrosoftAuth.ResolveClientIds(" 11111111-2222-3333-4444-555555555555 ")[0]);
    }

    [Fact]
    public void OfflineAccount_KeepsLegacyUuidAndType()
    {
        var store = new McAccountStore(_root);
        var account = store.Add("Steve");
        Assert.False(account.IsMicrosoft);
        Assert.Equal("离线账户", account.TypeLabel);
        Assert.Equal(McOfflineUuid.Create("Steve"), account.Uuid);
    }
}

using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

[Collection("GlobalConfigTests")]
public sealed class AgentProjectTests
{
    [Fact]
    public void Workspace_IndexesMultipleIndependentPluginProjects_AndIgnoresBuildCopies()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuba-projects-" + Guid.NewGuid().ToString("N"));
        try
        {
            void Project(string name, string id)
            {
                var folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "tubatools.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "Plugin.csproj"), $"<Project><PropertyGroup><PackageId>{id}</PackageId><Version>1.0.0</Version></PropertyGroup></Project>");
            }
            Project("Cleanup", "Tubatools.Cleanup"); Project("Network", "Tubatools.Network");
            Project(Path.Combine("Cleanup", "obj", "copy"), "Should.Not.Appear");
            Directory.CreateDirectory(Path.Combine(root, "reflector"));
            File.WriteAllText(Path.Combine(root, "reflector", "Probe.csproj"), "<Project />");
            var projects = AgentWorkspace.FindProjects(root);
            Assert.Equal(2, projects.Count);
            Assert.Equal(new[] { "Tubatools.Cleanup", "Tubatools.Network" }, projects.Select(p => p.PackageId));
            Assert.All(projects, p => Assert.EndsWith("/Plugin.csproj", p.Path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Workspace_PluginSkillPrefersNuGetSdk_WithHostCopyFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuba-workspace-" + Guid.NewGuid().ToString("N"));
        var previous = ConfigManager.DataDirectoryOverride;
        try
        {
            ConfigManager.DataDirectoryOverride = root;

            AgentWorkspace.Prepare();

            var skill = File.ReadAllText(Path.Combine(AgentWorkspace.DirectoryPath, ".opencode", "skills", "tuba-plugin", "SKILL.md"));
            Assert.Contains("PackageReference Include=\"TubaWinUi3.PluginSdk\"", skill);
            Assert.Contains("Version=\"2.1.0\"", skill);
            Assert.Contains("../sdk/TubaWinUi3.PluginSdk.dll", skill);
            Assert.Contains("dotnet --list-sdks", skill);
            Assert.Contains("winget install --id Microsoft.DotNet.SDK.10", skill);

            var sdk = Path.Combine(AgentWorkspace.DirectoryPath, "sdk", "TubaWinUi3.PluginSdk.dll");
            Assert.True(File.Exists(sdk));
            Assert.Equal(new FileInfo(typeof(TubaWinUi3.PluginSdk.ITubaPlugin).Assembly.Location).Length, new FileInfo(sdk).Length);

            var agents = File.ReadAllText(Path.Combine(AgentWorkspace.DirectoryPath, "AGENTS.md"));
            Assert.Contains("TubaWinUi3.PluginSdk", agents);
            Assert.Contains("NuGet", agents);
            Assert.Contains("Microsoft.DotNet.SDK.10", agents);
        }
        finally
        {
            ConfigManager.DataDirectoryOverride = previous;
            Directory.Delete(root, true);
        }
    }
}

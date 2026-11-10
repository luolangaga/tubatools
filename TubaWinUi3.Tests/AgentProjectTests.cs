using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

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
}

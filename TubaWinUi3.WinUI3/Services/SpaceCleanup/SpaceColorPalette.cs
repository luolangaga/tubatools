namespace TubaWinUi3.Services.SpaceCleanup;

/// <summary>Stable categorical colours shared by the map and file tree, independent of scan order.</summary>
public static class SpaceColorPalette
{
    private static readonly double[] Hues = [210, 174, 278, 29, 340, 143, 47, 236, 190, 12, 310, 85];
    public static double Hue(SpaceNode node, bool byFileType = false, SpaceNode? viewRoot = null)
    {
        for (var p = node; p is not null; p = p.Parent)
            if (p.Tag != 0) return p.Tag switch { 1 => 354, 2 => 30, 3 => 145, _ => 210 };
        if (byFileType && !node.IsDirectory)
            return Path.GetExtension(node.Name).ToLowerInvariant() switch
            { ".mp4" or ".mkv" or ".avi" or ".mp3" or ".wav" => 278,
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => 145,
                ".zip" or ".7z" or ".rar" or ".iso" => 32, ".sys" or ".dll" => 190,
                ".exe" or ".msi" => 210, ".pdf" or ".docx" or ".xlsx" or ".pptx" => 345,
                _ => Hues[Hash(Path.GetExtension(node.Name)) % Hues.Length] };
        var branch = node;
        if (viewRoot?.IsAggregate == true) viewRoot = viewRoot.AggregateSource;
        while (branch.Parent?.Parent is not null && branch.Parent != viewRoot) branch = branch.Parent;
        double hue = branch.Name.ToLowerInvariant() switch
        {
            "windows" => 207, "users" or "用户" => 37, "program files" or "program files (x86)" => 172,
            "programdata" => 18, "xboxgames" or "steam" or "steamapps" => 278,
            "pagefile.sys" => 190, "hiberfil.sys" => 340, "swapfile.sys" => 143,
            _ => Hues[Hash(branch.Name) % Hues.Length]
        };
        return (hue + (node == branch ? 0 : ((int)(Hash(node.Name) % 5) - 2) * 5) + 360) % 360;
    }
    public static uint Hash(string value)
    { uint hash = 2166136261; foreach (char c in value) hash = unchecked((hash ^ char.ToLowerInvariant(c)) * 16777619); return hash; }
}

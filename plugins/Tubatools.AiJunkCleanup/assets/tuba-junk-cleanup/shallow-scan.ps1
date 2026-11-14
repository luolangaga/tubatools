param(
    [string]$Path = "C:\",
    [int]$Depth = 3,
    [int]$Top = 200
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath($Path)
if (-not [System.IO.Directory]::Exists($root)) {
    Write-Error "Directory not found: $root"
    exit 1
}
$depthValue = [Math]::Max(1, [Math]::Min(6, $Depth))
$topValue = [Math]::Max(20, [Math]::Min(1000, $Top))

$code = @"
using System;
using System.Collections.Generic;
using System.IO;

public sealed class ShallowDir
{
    public int Level { get; set; }
    public string Path { get; set; }
    public long Bytes { get; set; }
    public long Files { get; set; }
}

public sealed class ShallowResult
{
    public string Root { get; set; }
    public int Depth { get; set; }
    public long TotalBytes { get; set; }
    public long Files { get; set; }
    public ShallowDir[] Directories { get; set; }
}

public static class ShallowScan
{
    public static ShallowResult Scan(string root, int depth, int top)
    {
        var map = new Dictionary<string, ShallowDir>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        long files = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    try
                    {
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }
                    pending.Push(sub);
                }
            }
            catch { }
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    long length;
                    try
                    {
                        var info = new FileInfo(file);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        length = info.Length;
                    }
                    catch { continue; }
                    total += length;
                    files++;
                    var relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
                    var parts = relative.Split(Path.DirectorySeparatorChar);
                    if (parts.Length < 2) continue;
                    var max = Math.Min(depth, parts.Length - 1);
                    for (var level = 1; level <= max; level++)
                    {
                        var key = string.Join(Path.DirectorySeparatorChar.ToString(), parts, 0, level);
                        var full = Path.Combine(root, key);
                        ShallowDir stat;
                        if (!map.TryGetValue(full, out stat))
                        {
                            stat = new ShallowDir();
                            stat.Level = level;
                            stat.Path = full;
                            map[full] = stat;
                        }
                        stat.Bytes += length;
                        stat.Files++;
                    }
                }
            }
            catch { }
        }
        var list = new List<ShallowDir>(map.Values);
        list.Sort(delegate(ShallowDir a, ShallowDir b) { return b.Bytes.CompareTo(a.Bytes); });
        if (list.Count > top) list.RemoveRange(top, list.Count - top);
        var result = new ShallowResult();
        result.Root = root;
        result.Depth = depth;
        result.TotalBytes = total;
        result.Files = files;
        result.Directories = list.ToArray();
        return result;
    }
}
"@

if (-not ("ShallowScan" -as [type])) {
    Add-Type -TypeDefinition $code -Language CSharp
}

$result = [ShallowScan]::Scan($root, $depthValue, $topValue)
$out = [pscustomobject]@{
    root = $result.Root
    depth = $result.Depth
    totalBytes = $result.TotalBytes
    files = $result.Files
    directories = @($result.Directories | ForEach-Object {
        [pscustomobject]@{
            level = $_.Level
            path = $_.Path
            bytes = $_.Bytes
            files = $_.Files
        }
    })
}
$out | ConvertTo-Json -Depth 6

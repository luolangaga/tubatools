namespace TubaWinUi3.Models;

public sealed class CpuRankingEntry
{
    public int Rank { get; set; }
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Process { get; set; } = "";
    public int Rating { get; set; }
    public string Grade { get; set; } = "";
    public int SingleCore { get; set; }
    public int MultiCore { get; set; }
    public string Cores { get; set; } = "";
    public string Tdp { get; set; } = "";
    public string Cache { get; set; } = "";
    public string Category { get; set; } = "";
}

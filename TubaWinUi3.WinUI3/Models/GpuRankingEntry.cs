namespace TubaWinUi3.Models;

public sealed class GpuRankingEntry
{
    public int Rank { get; set; }
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
    public int Rating { get; set; }
    public string Grade { get; set; } = "";
    public int Gaming { get; set; }
    public int Render { get; set; }
    public string Tflops { get; set; } = "";
    public string GeekbenchOpencl { get; set; } = "";
    public string Vram { get; set; } = "";
    public string TimeSpy { get; set; } = "";
    public string Category { get; set; } = "";
}

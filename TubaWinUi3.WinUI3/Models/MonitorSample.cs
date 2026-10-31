namespace TubaWinUi3.Models;

public sealed class MonitorSample
{
    public DateTimeOffset CapturedAt { get; internal set; }
    public string? SensorError { get; internal set; }
    internal MonitorSample Copy() => (MonitorSample)MemberwiseClone();
    public float CpuLoad = -1, CpuTemp = -1, CpuClock = -1, CpuPower = -1;
    public string CpuName = "";
    /// <summary>传感器库实际创建的 CPU 温度传感器数量（0 = CPU 型号不认识，>0 但无值 = 驱动/ring0 不可用）。</summary>
    public int CpuTempSensors;

    public float GpuLoad = -1, GpuTemp = -1, GpuClock = -1, GpuPower = -1, GpuVramLoad = -1, GpuVramUsedGB = -1;
    public string GpuName = "";

    public float MemLoad = -1, MemUsedGB = -1, MemTotalGB = -1;

    public float DiskReadMBs = -1, DiskWriteMBs = -1, DiskTemp = -1;

    public float NetUpMBs = -1, NetDownMBs = -1;

    public float BatPercent = -1, BatPower = -1;
    public bool BatCharging;

    public float Fps = -1;
    public float FpsLow1 = -1;
    public float FpsLow01 = -1;
    public float FrameTimeMs = -1;
    public float RenderLatencyMs = -1;
    public string FpsProcess = "";
}

public sealed class GpuInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
}

public sealed class FurMarkGpuInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public string Memory { get; init; } = "";
    public string Driver { get; init; } = "";
}

public sealed class FpsSnapshot
{
    public string ProcessName = "";
    public double CurrentFps;
    public double AvgFps;
    public double MinFps;
    public double MaxFps;
    public double OnePercentLow;
    public double PointOnePercentLow;
    public int TotalFrames;
    public double TotalSeconds;
    public List<double> FrameTimes = [];
}
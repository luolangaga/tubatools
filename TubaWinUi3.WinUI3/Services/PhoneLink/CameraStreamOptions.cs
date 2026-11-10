namespace TubaWinUi3.Services;

/// <summary>手机请求的摄像头推流参数（全部有硬上限，避免一个请求拖垮编码）。</summary>
internal readonly record struct CameraStreamOptions(int Width, int Fps, int Quality)
{
    public static CameraStreamOptions Create(int width, int fps, int quality) =>
        new(Math.Clamp(width, 320, 1920), Math.Clamp(fps, 1, 20), Math.Clamp(quality, 30, 90));

    public static CameraStreamOptions FromSettings()
    {
        static int Read(string key, int fallback) =>
            int.TryParse(AppSettings.Get(key), out var value) ? value : fallback;
        return Create(
            Read("PhoneLinkCameraMaxWidth", 960),
            Read("PhoneLinkCameraFps", 10),
            Read("PhoneLinkCameraQuality", 60));
    }
}

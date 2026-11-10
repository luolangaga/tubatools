namespace Tubatools.SteamToolbox;

internal static class QrCodeService
{
    public static byte[] GeneratePng(string text, int pixelsPerModule = 8)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        var data = generator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var png = new QRCoder.PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}

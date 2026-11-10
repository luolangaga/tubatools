using System.Security.Cryptography;

namespace TubaWinUi3.Services;

internal sealed record DownloadIntegrityMismatch(string FileName, string ExpectedSha256, string ActualSha256,
    long? ExpectedBytes, long ActualBytes);

internal sealed class DownloadIntegrityCancelledException(CancellationToken ct)
    : OperationCanceledException("已取消安装校验不符的文件", ct);

internal static class DownloadIntegrityPolicy
{
    internal const string ConfirmationText = "仍然安装";
    internal static bool IsStrongConfirmation(bool acknowledged, string? text) =>
        acknowledged && string.Equals(text?.Trim(), ConfirmationText, StringComparison.Ordinal);

    internal static async Task VerifyAsync(Stream file, string expectedSha256, string fileName, CancellationToken ct,
        Func<DownloadIntegrityMismatch, CancellationToken, Task<bool>>? confirmMismatch = null, long? expectedBytes = null)
    {
        var expected = TrustedDownloads.ParseSha256(expectedSha256);
        file.Position = 0;
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
        file.Position = 0;
        if (CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual))
            && (expectedBytes is null || expectedBytes == file.Length)) return;
        if (confirmMismatch is null)
            throw new InvalidDataException("下载文件与发布信息不符，尚未获准继续安装");
        var mismatch = new DownloadIntegrityMismatch(Path.GetFileName(fileName), expected, actual, expectedBytes, file.Length);
        if (!await confirmMismatch(mismatch, ct))
            throw new DownloadIntegrityCancelledException(ct);
        ct.ThrowIfCancellationRequested();
        // Consent applies only to these bytes while the caller retains its file lock, never future downloads.
        file.Position = 0;
    }
}

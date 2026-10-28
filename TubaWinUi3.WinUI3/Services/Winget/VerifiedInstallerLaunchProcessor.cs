using System.Security.Cryptography;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// 安装包下载完成后先校验官方清单声明的 SHA-256，通过后再启动安装程序。
/// 参数化处理器（哈希来自清单），不注册进 PostProcessorRegistry —— 与 CommunityToolInstallProcessor 同理。
/// </summary>
public sealed class VerifiedInstallerLaunchProcessor(string expectedSha256) : IDownloadPostProcessor
{
    public string DisplayName => "校验并运行安装程序";

    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            statusProgress?.Report("正在校验安装包 SHA-256…");
            var actual = await Task.Run(() => HashFile(downloadedFilePath), ct).ConfigureAwait(false);
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "安装包 SHA-256 校验失败，已停止安装：文件与 WinGet 官方清单不一致（可能下载损坏或被替换），请重试。");
            }
        }

        await new InstallerLaunchProcessor()
            .ExecuteAsync(downloadedFilePath, destinationPath, statusProgress, ct).ConfigureAwait(false);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// 邀请码与朋友端「一键加入」脚本。
/// 邀请码是一段能直接粘进 QQ / 微信的紧凑字符串（TBG2: + Base64Url），
/// 内含房间码（网络名 + 密钥）、游戏端口与游戏名——不依赖任何账号或服务器。
/// </summary>
public static partial class GameTunnelInvite
{
    public const string CodePrefix = "TBG2:";

    // ══════════════════════ 邀请码 ══════════════════════

    private sealed class InvitePayload
    {
        public string c { get; set; } = "";
        public int p { get; set; }
        public string? g { get; set; }
        public string? r { get; set; }
        public bool l { get; set; }
    }

    public static string Encode(InviteInfo info)
    {
        var payload = new InvitePayload
        {
            c = info.RoomCode,
            p = info.Port,
            g = string.IsNullOrWhiteSpace(info.Game) ? null : info.Game.Trim(),
            r = info.Protocol switch
            {
                GameTunnelProtocol.Udp => "udp",
                GameTunnelProtocol.TcpAndUdp => "both",
                _ => "tcp"
            },
            l = info.UsesLanDiscovery
        };

        var json = JsonSerializer.Serialize(payload, PayloadOptions);
        return CodePrefix + ToBase64Url(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// 解析邀请码。兼容三种粘贴内容：纯邀请码、带说明文字的整段邀请消息，
    /// 以及只有房间码（<c>U/XXXX-XXXX-XXXX-XXXX</c>，端口由房主主机名在运行时给出）。
    /// </summary>
    public static InviteInfo? Decode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();

        var index = value.IndexOf(CodePrefix, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var rest = value[(index + CodePrefix.Length)..];
            var token = new string(rest.TakeWhile(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '=').ToArray());
            var decoded = DecodeToken(token);
            if (decoded is not null) return decoded;
        }

        // 只有房间码：仍然可以加入，端口在找到房主时从主机名解析。
        // 协议未知时按 TCP+UDP 处理（多建一条 UDP 转发是安全的，少建则 UDP 游戏会连不上）。
        var room = TunnelRoomCode.Find(value);
        return room is null
            ? null
            : new InviteInfo
            {
                RoomCode = room.Code,
                NetworkName = room.NetworkName,
                NetworkSecret = room.NetworkSecret,
                Port = 0,
                Protocol = GameTunnelProtocol.TcpAndUdp
            };
    }

    private static InviteInfo? DecodeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var json = Encoding.UTF8.GetString(FromBase64Url(token));
            var payload = JsonSerializer.Deserialize<InvitePayload>(json, PayloadOptions);
            if (payload is null) return null;

            var room = TunnelRoomCode.Find(payload.c) ?? TunnelRoomCode.TryParse(payload.c);
            if (room is null) return null;
            if (payload.p is < 0 or > 65535) return null;

            return new InviteInfo
            {
                RoomCode = room.Code,
                NetworkName = room.NetworkName,
                NetworkSecret = room.NetworkSecret,
                Port = payload.p,
                Game = payload.g ?? "",
                Protocol = payload.r switch
                {
                    "udp" => GameTunnelProtocol.Udp,
                    "both" => GameTunnelProtocol.TcpAndUdp,
                    _ => GameTunnelProtocol.Tcp
                },
                UsesLanDiscovery = payload.l
            };
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };
        return Convert.FromBase64String(padded);
    }

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ══════════════════════ 邀请文案 ══════════════════════

    /// <summary>对方复制发给朋友的整段文字。</summary>
    public static string BuildInviteText(InviteInfo info, string? inviteCode, bool hasScript)
    {
        var builder = new StringBuilder();
        builder.AppendLine("【图吧工具箱 · 联机邀请】");
        if (!string.IsNullOrWhiteSpace(info.Game)) builder.AppendLine($"游戏：{info.Game}");
        builder.AppendLine($"房间码：{info.RoomCode}");
        if (info.Port > 0) builder.AppendLine($"游戏里填：{info.Address}");
        builder.AppendLine();

        var step = 1;

        if (inviteCode is { Length: > 0 })
        {
            builder.AppendLine($"{step}) 装了图吧工具箱：打开「游戏联机助手」→「我要加入」→ 粘贴下面这行");
            builder.AppendLine(inviteCode);
            builder.AppendLine("   （邀请码里带着网络密钥，直接加入——不需要我批准，也不用你注册任何账号）");
            builder.AppendLine();
            step++;
        }

        if (hasScript)
        {
            builder.AppendLine($"{step}) 没装工具箱：双击我发你的「一键加入联机.cmd」，它会自动装好联机组件并连上我");
            builder.AppendLine();
            step++;
        }

        if (info.UsesLanDiscovery)
        {
            builder.AppendLine($"{step}) 进游戏：多人游戏 → 稍等两秒，我的房间会直接出现在「扫描局域网世界」里");
            builder.AppendLine("   （没出现时点「直接连接」，填上面的地址）");
        }
        else if (info.Port > 0)
        {
            builder.AppendLine($"{step}) 进游戏：在联机界面里填 {info.Address}");
            builder.AppendLine("   （首次连接可能需要多试一次——双方还在打洞直连）");
        }

        builder.AppendLine("   （玩完想断：在「游戏联机助手」里点「停止联机」即可）");
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    // ══════════════════════ 朋友端一键加入脚本 ══════════════════════

    public static string ScriptFileName(string gameName)
    {
        var safe = Sanitize(gameName);
        return $"{safe}-一键加入联机.cmd";
    }

    internal static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "游戏";
        var builder = new StringBuilder();
        foreach (var c in name.Trim())
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' ? '_' : c);
        }
        var result = builder.ToString().Trim().Trim('.');
        return result.Length == 0 ? "游戏" : result.Length > 40 ? result[..40] : result;
    }

    /// <summary>
    /// 生成朋友端 .cmd 脚本。
    /// 落盘编码是 <b>UTF-8 无 BOM</b>，正文以 <c>chcp 65001</c> 开头：
    /// ACP=65001（UTF-8 beta）的机器用 GBK 会直接变成乱码，而 GBK 机器在切到 65001 后也能正确读取 UTF-8。
    /// </summary>
    /// <param name="nodes">房主使用的发现节点；为空时用内置公共节点。</param>
    public static string BuildScript(InviteInfo info, IReadOnlyList<string>? nodes = null)
    {
        var gameName = string.IsNullOrWhiteSpace(info.Game) ? "游戏联机" : info.Game!;
        var port = info.Port > 0 ? info.Port : 25565;
        var protocol = info.Protocol;

        var nodeEcho = new StringBuilder();
        foreach (var node in nodes ?? GameTunnelCatalog.DefaultNodes)
        {
            nodeEcho.AppendLine(">>\"%CFG%\" echo [peer]");
            nodeEcho.AppendLine($">>\"%CFG%\" echo uri = \"{node}\"");
            nodeEcho.AppendLine(">>\"%CFG%\" echo.");
        }

        var tcpForward = $"""
"%ETCLI%" -p 127.0.0.1:%RPC% port-forward add tcp 127.0.0.1:{port} {InviteInfo.HostAddress}:{port}
""";
        var udpForward = protocol.UsesUdp()
            ? $"""
"%ETCLI%" -p 127.0.0.1:%RPC% port-forward add udp 127.0.0.1:{port} {InviteInfo.HostAddress}:{port}
"""
            : "";

        return $$"""
@echo off
chcp 65001 >nul
setlocal EnableExtensions
title 图吧工具箱 · 一键加入联机
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
    echo 正在申请管理员权限...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set "GAME={{gameName}}"
set "PORT={{port}}"
set "HOSTIP={{InviteInfo.HostAddress}}"
set "NETNAME={{info.NetworkName}}"
set "NETSEC={{info.NetworkSecret}}"
set "WORKDIR=%TEMP%\tuba-easytier"
set "ETCORE=%WORKDIR%\easytier-core.exe"
set "ETCLI=%WORKDIR%\easytier-cli.exe"
set "CFG=%WORKDIR%\guest.toml"
set "RPC=15888"

cls
echo ============================================================
echo   图吧工具箱 · 一键加入联机
echo ------------------------------------------------------------
echo   游戏：%GAME%
echo   房间：{{info.RoomCode}}
echo ============================================================
echo.

if exist "%ETCORE%" if exist "%ETCLI%" goto :has_component

echo [1/3] 下载联机组件（约 30MB）
set "ZIP=%WORKDIR%\easytier.zip"
mkdir "%WORKDIR%" 2>nul
if exist "%ZIP%" del /f /q "%ZIP%" >nul 2>&1

set "ARCH=x86_64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "ARCH=arm64"
if /i "%PROCESSOR_ARCHITECTURE%"=="x86" set "ARCH=i686"
set "ASSET=easytier-windows-%ARCH%-v{{EasyTierPackage.Version}}.zip"
set "GH=https://github.com/EasyTier/EasyTier/releases/download/v{{EasyTierPackage.Version}}/%ASSET%"
set "TRIES=0"

:download
set /a TRIES+=1
echo     第 %TRIES% 次尝试...
call :fetch "%GH%"
if exist "%ZIP%" for %%F in ("%ZIP%") do if %%~zF GTR 5000000 goto :extract
call :fetch "https://ghfast.top/%GH%"
if exist "%ZIP%" for %%F in ("%ZIP%") do if %%~zF GTR 5000000 goto :extract
call :fetch "https://gh-proxy.com/%GH%"
if exist "%ZIP%" for %%F in ("%ZIP%") do if %%~zF GTR 5000000 goto :extract
if %TRIES% GEQ 3 goto :download_failed
echo     下载没有完成，5 秒后重试...
timeout /t 5 /nobreak >nul
goto :download

:download_failed
echo.
echo   [失败] 没能自动下载联机组件。
echo   请手动从 https://github.com/EasyTier/EasyTier/releases 下载
echo   %ASSET% ，把 easytier-core.exe / easytier-cli.exe / Packet.dll
echo   放进 %WORKDIR% 后重新运行本脚本。
echo.
pause
exit /b 1

:extract
echo [2/3] 正在解包联机组件...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%ZIP%' -DestinationPath '%WORKDIR%' -Force"
del /f /q "%ZIP%" >nul 2>&1
rem 官方 zip 内的文件在 easytier-windows-<架构>\ 子目录里，统一提回根目录
for /d %%D in ("%WORKDIR%\easytier-windows-*") do (
    if exist "%%D\easytier-core.exe" copy /y "%%D\easytier-core.exe" "%WORKDIR%\" >nul
    if exist "%%D\easytier-cli.exe" copy /y "%%D\easytier-cli.exe" "%WORKDIR%\" >nul
    if exist "%%D\Packet.dll" copy /y "%%D\Packet.dll" "%WORKDIR%\" >nul
)
if not exist "%ETCORE%" goto :download_failed

:has_component
echo [3/3] 正在加入房间并建立端口转发...
> "%CFG%" echo hostname = "tuba-guest-%RANDOM%%RANDOM%"
>>"%CFG%" echo dhcp = true
>>"%CFG%" echo listeners = ["tcp://0.0.0.0:0", "udp://0.0.0.0:0"]
>>"%CFG%" echo tcp_whitelist = ["0"]
>>"%CFG%" echo udp_whitelist = ["0"]
>>"%CFG%" echo.
>>"%CFG%" echo [network_identity]
>>"%CFG%" echo network_name = "%NETNAME%"
>>"%CFG%" echo network_secret = "%NETSEC%"
>>"%CFG%" echo.
{{nodeEcho.ToString().TrimEnd()}}
>>"%CFG%" echo [flags]
>>"%CFG%" echo no_tun = true
>>"%CFG%" echo latency_first = true
>>"%CFG%" echo enable_kcp_proxy = true
>>"%CFG%" echo enable_quic_proxy = true
>>"%CFG%" echo data_compress_algo = 2
>>"%CFG%" echo private_mode = true

taskkill /IM easytier-core.exe /F >nul 2>&1
start "" /b "%ETCORE%" -c "%CFG%" -r 127.0.0.1:%RPC% --multi-thread
timeout /t 4 /nobreak >nul

{{tcpForward.TrimEnd()}}
{{udpForward.TrimEnd()}}

cls
echo ============================================================
echo   全部就绪！
echo ============================================================
echo.
echo   现在打开游戏：
{{(info.UsesLanDiscovery ? "echo     多人游戏 → 稍等两秒，房间会出现在「扫描局域网世界」里" : "echo     在联机界面里填：" + InviteInfo.HostAddress + ":" + port)}}
echo.
echo   联机组件已经在后台运行，这个窗口可以关闭。
echo   想断开联机：任务管理器里结束 easytier-core.exe 即可。
echo.
echo ============================================================
echo.
pause
exit /b 0

:fetch
if exist "%ZIP%" del /f /q "%ZIP%" >nul 2>&1
if exist "%SystemRoot%\System32\curl.exe" (
    "%SystemRoot%\System32\curl.exe" -L --fail --silent --show-error --connect-timeout 20 -o "%ZIP%" "%~1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-WebRequest -Uri '%~1' -OutFile '%ZIP%' -UseBasicParsing } catch { exit 1 }"
)
exit /b 0
""";
    }

    /// <summary>把脚本写到指定路径：UTF-8 无 BOM，换行统一成 CRLF。</summary>
    public static string WriteScript(string content, string targetPath)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        File.WriteAllBytes(targetPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(ToCrlf(content)));
        return targetPath;
    }

    /// <summary>
    /// 批处理必须是 CRLF：cmd.exe 遇到裸 LF 会把命令行拆碎成「'o' 不是内部或外部命令」这种碎片。
    /// </summary>
    internal static string ToCrlf(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

    /// <summary>写到本工具自己的脚本目录，返回路径。</summary>
    public static string WriteScriptToDataDir(InviteInfo info, IReadOnlyList<string>? nodes = null)
    {
        var path = Path.Combine(GameTunnelCatalog.GetScriptsDir(), ScriptFileName(info.Game));
        return WriteScript(BuildScript(info, nodes), path);
    }
}

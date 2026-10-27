using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using static TubaWinUi3.Pages.GameTunnelUi;

namespace TubaWinUi3.Pages;

/// <summary>新手教程与常见问题（一次性讲清楚，主页只留入口）。</summary>
public sealed class GameTunnelGuideDialog
{
    private readonly ContentDialog _dialog;

    public GameTunnelGuideDialog(XamlRoot xamlRoot)
    {
        var body = new StackPanel { Spacing = 12, MinWidth = 520 };

        body.Children.Add(SectionHeader("三步开始"));
        body.Children.Add(Card(Section(
            Line("第 1 步 · 两台电脑各点一次「准备联机组件」", 13),
            Line("会自动下载 EasyTier 联机组件（约 30MB）。不需要账号、不需要登录任何服务，只做一次。"),
            Line("第 2 步 · 主机在游戏里把游戏开起来", 13),
            Line("比如泰拉瑞亚点「开服并开始游戏」、我的世界按 Esc 选「对局域网开放」。"),
            Line("第 3 步 · 主机点「我当主机，邀请朋友」，把房间码发给朋友", 13),
            Line("朋友点「我加入朋友」粘贴邀请码即可；没装工具箱的朋友可以运行「一键加入脚本」。"))));

        body.Children.Add(SectionHeader("朋友那边的两种加入方式"));
        body.Children.Add(Card(Section(
            Line("① 邀请码 / 房间码：朋友也装了图吧工具箱 —— 粘贴即用。房间码里带着网络密钥，他直接进同一个虚拟网络，你和他都不需要点任何「同意」。"),
            Line("② 一键加入脚本：朋友什么都没装 —— 发给他的 .cmd 双击运行，自动下载组件、加入房间并建立端口转发，同样不需要同意。"))));

        body.Children.Add(SectionHeader("常见问题"));

        body.Children.Add(Expander("房间码里有什么？安全吗？",
            "房间码是一张一次性生成的加密虚拟网络的「名字 + 密钥」：前半段决定网络名，后半段是密钥。\n"
            + "只有拿到完整房间码的人才能进入这个网络；网络里的游戏数据在玩家之间加密传输，发现节点只看得到连接关系。\n"
            + "停止联机后这张网络就没了；想彻底断开，回主页点「停止联机」即可，本机不会留下虚拟网卡或后台服务。"));

        body.Children.Add(Expander("玩起来会卡吗？延迟高不高？",
            "打洞成功时是两台电脑直接连线，延迟基本等于你们之间的网络延迟；打洞失败会自动走加密中转，延迟会高一些但仍然能玩。\n想确认自己属于哪种：主页或网络检测里能看到「直连 / 中转」以及具体延迟。"));

        body.Children.Add(Expander("要开路由器端口 / 有公网 IP 吗？",
            "都不需要。联机组件会自己打洞，打不通就走中转，所以也不怕运营商不给公网 IP。\n"
            + "它只在本机转发游戏端口，不安装驱动、不创建虚拟网卡、不修改 DNS 或路由表。"));

        body.Children.Add(Expander("它和「局域网文件分享」是一回事吗？",
            "不是。局域网文件分享只在同一个 Wi-Fi 下有用；游戏联机助手是跨互联网的虚拟网络，朋友在天南海北也能连。"));

        body.Children.Add(Expander("朋友加入后，我的电脑会被别人看到吗？",
            "房主只把游戏用的那一个端口开放给房间里的成员，加入者不开放任何入站端口。\n"
            + "房间内的成员能看到彼此的虚拟地址与设备名，但访问不到你电脑上的其它服务。\n"
            + "如果连虚拟地址都不想暴露，可以在设置里打开「隐藏我的 IP 地址」，流量会全部经中转。"));

        body.Children.Add(Expander("为什么有时候刷不出朋友的房间？",
            "取决于游戏怎么找主机：靠平台联机（例如 Steam）的游戏走平台自己的通道；靠局域网广播 / 组播扫描的游戏不一定能刷到。\n"
            + "我的世界 Java 版是个例外：加入房间后本工具会用游戏原生的「对局域网开放」广播格式，把房间直接放进「扫描局域网世界」列表。\n"
            + "其它游戏刷不到时，直接用游戏里的「直接连接 / 通过 IP 加入」，填 10.144.144.1 和房主端口——这条路任何游戏都有效。"));

        body.Children.Add(Expander("朋友加入了，但游戏里连不上",
            "按顺序检查：\n"
            + "① 主机的游戏还开着吗？主页会提示端口是否还在监听。\n"
            + "② 朋友是真的加入成功了吗？让他在工具箱里看「已加入房间」和本机转发端口。\n"
            + "③ 游戏里填的是 127.0.0.1:本机转发端口 吗？（我的世界可直接从局域网列表进）\n"
            + "④ 双方做一次 TCP 连通性测试，看看是连不通还是连得通但游戏不认（后者通常是版本不一致）。\n"
            + "⑤ 个别安全软件会拦截 P2P 连接，让朋友临时关掉网络防护再试一次。"));

        body.Children.Add(Expander("会不会修改我的网络设置 / DNS？",
            "不会。联机组件以「无网卡」模式运行（no_tun）：不安装驱动、不创建虚拟网卡、不接管 DNS、不修改路由表。\n"
            + "它只做两件事：在虚拟网络里发现彼此，以及把游戏端口转发到本机。停止联机后什么都不留。"));

        body.Children.Add(Expander("游戏端口怎么查？",
            "内置了常见游戏的端口预设，选游戏时会自动填好。自定义游戏时：泰拉瑞亚 7777、我的世界（Java）25565、基岩版 19132、帕鲁 8211……\n"
            + "实在不知道就去游戏里开一次（建房 / 开服），通常在提示里会写端口号。"));

        body.Children.Add(SectionHeader("内置游戏端口速查"));
        var portLines = GameTunnelCatalog.Presets
            .Select(p => $"{p.Name} · {p.DefaultPort}（{p.Protocol.Describe()}）");
        body.Children.Add(Card(Section(portLines.Select(l => Line(l)).ToArray())));

        _dialog = new ContentDialog
        {
            Title = "使用教程",
            Content = new ScrollViewer
            {
                Content = body,
                MaxHeight = 470,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
    }

    public Task<ContentDialogResult> ShowAsync() => _dialog.ShowAsync().AsTask();

    private static Expander Expander(string header, string body) => new()
    {
        Header = header,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Content = new TextBlock
        {
            Text = body,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Subtle,
            Margin = new Thickness(0, 8, 0, 4)
        }
    };
}

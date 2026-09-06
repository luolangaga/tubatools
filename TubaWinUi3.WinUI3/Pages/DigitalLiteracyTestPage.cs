using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class DigitalLiteracyTestPage : Page
{
    private int _currentQuestion = -1; // -1 = welcome, 0..N-1 = questions, N = result
    private int _totalScore;
    private int[]? _selectedAnswers;
    private int[][]? _shuffledOrders; // per question: display position → original option index
    private bool _answered;

    private const int TotalQuestions = 25;
    private const int PointsPerQuestion = 4;
    private const int MaxScore = TotalQuestions * PointsPerQuestion; // 100

    private static readonly QuizQuestion[] Questions =
    [
        // ── 输入与操作 ──
        new("你正在学习电脑打字，正确的指法习惯是？",
            ["用两根食指配合 Shift 切换大小写，效率最高", "十指分工定位基准键位（ASDF/JKL;），实现盲打输入", "用语音输入替代键盘，属于更先进的 HCI 交互方式", "单手操作即可，人体工学研究表明过度分工反而增加腱鞘炎风险"],
            1, "十指分工盲打是标准打字姿势，各手指负责固定键位区域，基准键位定位是 touch typing 的核心。"),

        new("安装软件时弹出「按任意键继续」，你应该？",
            ["等待系统倒计时结束后自动跳过，属于非交互式安装流程", "按键盘上任意一个键（空格、回车等均可）触发安装继续", "必须精确找到标着「Any Key」的物理按键，否则会中断安装", "这是 DOS 时代的遗留指令，现代系统可以直接忽略"],
            1, "「任意键」指键盘上任意一个按键，并非某个特定按键。该提示源自 DOS 时代的交互设计。"),

        // ── 游戏与平台 ──
        new("你想在手机上玩 PC 端的《赛博朋克2077》，正确做法是？",
            ["手机芯片性能已接近 PC，直接下载 PC 版安装包就能安装运行", "PC 与移动端 ISA 架构不同，可通过云游戏平台（GeForce NOW 等）串流游玩", "使用 Vulkan 渲染层适配，部分 3A 大作已支持移动端原生运行", "下载 Switch 模拟器运行，因为 Switch 也是 ARM 架构的移动设备"],
            1, "PC 游戏基于 x86 架构，手机是 ARM 架构，ISA 不兼容无法直接运行。可通过云游戏串流方案实现。"),

        new("你要下载 Steam 平台，应该怎么做？",
            ["搜索「Steam 管家」下载，它整合了 Steam 加速和游戏管理功能", "访问 store.steampowered.com 官网下载 Steam 客户端安装包", "在网盘搜索「Steam 免安装绿色版」压缩包，解压即可使用", "在 Microsoft Store 搜索 Steam 下载 UWP 版本"],
            1, "Steam 官网是 store.steampowered.com，「Steam 管家」等均为第三方仿冒软件，可能携带捆绑或木马。"),

        // ── 安全与防护 ──
        new("新电脑到手后，关于安全防护软件你应该？",
            ["安装360安全卫士+鲁大师+驱动精灵，形成多层主动防御体系", "日常使用 Defender 即可；若有网银操作、敏感办公等需求，可加装一款专业杀软", "部署 ESET NOD32 + 卡巴斯基双引擎交叉扫描，确保零日威胁检测率", "裸机运行即可，现代 Windows 的内核隔离（VBS）已提供硬件级防护"],
            1, "Windows Defender 已足够日常防护。但涉及网银、敏感数据等场景，额外安装一款可靠的专业杀软（如 ESET、卡巴斯基）是合理的安全策略。注意：同时装多款杀软会互相冲突。"),

        new("在下载站看到「高速下载」按钮，你应该？",
            ["点击「高速下载」，它使用了 P2SP 多线程加速协议，速度更快", "找到「普通下载」或直接去软件官网下载，避免捆绑安装器", "使用 IDM 接管下载，它会自动识别页面中的真实下载链接", "复制下载链接到迅雷，利用离线下载加速"],
            1, "下载站的「高速下载」通常是捆绑安装器，会附带大量推广软件。应优先从软件官网获取安装包。"),

        // ── 硬件认知 ──
        new("电商上看到「军工级主板 + i9级CPU」整机只卖1999元，你应该？",
            ["赶紧下单，军工级用料意味着更高的稳定性和耐久度", "这是典型的电商骗局，「i9级」不等于真正的 i9，需查看 CPU 具体型号和 CPU-Z 参数", "先查一下这块主板的 VRM 供电相数，军工级通常 12 相以上", "对比一下同价位的 Cinebench R23 跑分，性价比确实很高"],
            1, "「i9级」「军工级」是商家营销话术，实际可能是淘汰服务器拆机件。务必确认 CPU 具体型号。"),

        new("商家说「这台电脑 1TB」，这里的 1TB 通常指的是？",
            ["内存（RAM）容量，1TB DDR5 已经是消费级旗舰配置", "硬盘（存储）容量，主流电脑内存通常为 16~32GB", "显卡显存容量，对应的是 RTX 4090 级别的 1TB 显存版本", "指的是 NVMe SSD 的 TBW 写入寿命指标"],
            1, "1TB 通常指硬盘存储容量。目前主流电脑内存为 16~64GB，1TB 内存属于服务器级别。"),

        new("你的电脑有独立显卡，但玩游戏帧数很低、画面卡顿，可能的原因是？",
            ["显示器刷新率只有 60Hz，拖累了显卡的渲染帧数，需要更换高刷新率屏幕", "视频线插在了主板背部的集成显卡接口上，画面实际由核显输出，应插在独立显卡的 DP/HDMI 输出接口", "显卡的 PhysX 物理加速没有开启，现代游戏引擎必须依赖 PhysX 才能渲染画面", "Windows 家庭版没有独显驱动的完整授权，需要升级专业版才能发挥独显性能"],
            1, "核显性能远弱于独显：视频线插在主板接口上会走核显输出，游戏自然掉帧。装机后务必把显示器接在独立显卡的 DP/HDMI 接口上。"),

        new("关于 CPU 性能对比，正确的理解是？",
            ["所有 i7 一定比 i5 强，因为 i7 的 L3 缓存更大、线程数更多", "需看具体代数和型号，如 i3-14100 多核性能可超过老款 i7-7700", "只看单核主频和 IPC 指标就行，多核性能对日常使用影响不大", "看 TDP 功耗就行，功耗越高代表性能越强"],
            1, "CPU 性能取决于架构、代数、核心数等综合因素，不能仅凭 i3/i5/i7 的品牌前缀判断。"),

        // ── 文件管理 ──
        new("你收到一个 report.docx 文件，「.docx」是什么意思？",
            ["是文件的 MIME Type 标识，用于 HTTP 传输时的内容类型协商", "文件扩展名，表示这是一个基于 Office Open XML 标准的 Word 文档", "是文件的哈希校验后缀，用于验证文件完整性", "说明该文件经过了 DRM 数字版权加密"],
            1, "扩展名标识文件类型和关联程序。.docx 是基于 Office Open XML 标准的 Word 文档格式。"),

        new("安装软件时，安装路径应该怎么选？",
            ["装到桌面方便快速启动，Windows 的 Shell 文件夹机制会自动管理", "装到 D 盘根目录，利用独立分区的 I/O 隔离提升读写性能", "使用默认的 Program Files 路径或在非系统盘创建专用子目录（如 D:\\Apps\\软件名）", "装到 C:\\ProgramData 目录，该目录对所有用户账户可见且具有 SYSTEM 级权限"],
            2, "软件应默认装在 Program Files，或自建专用子目录（如 D:\\Apps\\软件名），文件集中便于统一管理和卸载；装到桌面或盘符根目录只会污染目录结构，D 盘根目录也并不会带来「I/O 隔离」之类的性能收益。"),

        new("你要卸载一个不再使用的软件，正确做法是？",
            ["把桌面图标拖到回收站，Windows 会自动触发关联的卸载程序", "通过 设置 → 应用 → 已安装的应用 卸载；顽固软件可用 Geek Uninstaller、HiBit Uninstaller 等专业工具深度清理", "直接删除安装文件夹，再用 CCleaner 清理注册表残留项", "在 PowerShell 中执行 Get-AppxPackage | Remove-AppxPackage 卸载"],
            1, "系统卸载入口会调用软件自带的卸载程序并清理注册表；Geek/HiBit 等专业卸载工具原理相同，还能强制卸载、扫描残留，对付顽固软件更彻底（本工具箱「其他工具」分类就内置了 HiBit Uninstaller）。删图标、删文件夹只是删文件不算卸载。"),

        new("你要解压一个 .zip 压缩包，应该怎么做？",
            ["把 .zip 后缀改成 .txt 用记事本打开，查看压缩包内部结构", "使用 7-Zip、WinRAR 或系统自带功能右键解压到指定目录", "用浏览器直接打开 .zip，Chrome 内置了 ZIP 解码器", "通过 WSL 的 unzip 命令行工具解压，兼容性比 GUI 工具更好"],
            1, "压缩文件需用解压工具处理。推荐 7-Zip（开源免费）或 WinRAR，Windows 11 也自带右键解压。"),

        // ── 系统使用 ──
        new("你想截取屏幕上的内容发给朋友，正确做法是？",
            ["用手机对着屏幕拍照，手机摄像头的 HDR 算法可以补偿屏幕反光", "按 Win+Shift+S 调用系统截图工具框选区域，或按 Print Screen 截取全屏", "使用远程桌面连接到自己的电脑，然后在远程会话中截图", "用 PowerShell 的 Add-Type 截图 API 编写脚本自动化截图"],
            1, "Win+Shift+S 可以框选任意区域截图并自动复制到剪贴板，远比手机拍照清晰。"),

        new("你想把 Word 文档中的标题居中对齐，正确做法是？",
            ["用空格键逐个敲入半角空格，通过等宽字体的字符宽度对齐到页面中心", "选中文字后按 Ctrl+E 或点击段落组的居中对齐按钮", "在标尺上拖动左缩进和右缩进标记到对称位置实现视觉居中", "插入一个单列表格，将标题放入单元格并设置单元格水平居中"],
            1, "Ctrl+E 是段落居中对齐的快捷键，也可在「开始」选项卡的段落组中点击居中按钮。"),

        new("电脑运行变卡，正确的排查思路是？",
            ["C 盘剩余空间不足导致虚拟内存（Page File）无法扩展，清理 C 盘即可", "打开任务管理器查看 CPU、内存、磁盘、GPU 占用率，定位高占用进程", "运行 sfc /scannow 修复系统文件，再用 DISM /Online /Cleanup-Image 还原组件存储", "进入安全模式卸载最近安装的驱动程序，回滚到上一个还原点"],
            1, "正确做法是用任务管理器（Ctrl+Shift+Esc）定位瓶颈，区分 CPU/内存/磁盘/GPU 哪个是瓶颈。"),

        new("有人说「电脑加速器能让电脑变快」，这种说法？",
            ["正确，加速器通过清理注册表碎片和优化内存分配提升系统响应速度", "大部分「加速器」是噱头，真正有效的是优化启动项、升级 SSD/内存等硬件", "加速器通过修改 Windows 的 NTFS 分区簇大小来提升磁盘 I/O 性能", "加速器能超频 CPU 和 GPU，相当于免费提升硬件性能"],
            1, "市面上的「加速器」多为伪优化。真正提速应从减少启动项、升级 SSD/内存、重装系统入手。"),

        // ── 网络与下载 ──
        new("你要从百度网盘下载别人分享的文件，正确做法是？",
            ["点击「在线解压」直接预览，百度网盘服务端会实时解压并推流到浏览器", "先保存到自己的网盘，再通过客户端下载到本地磁盘", "使用油猴脚本绕过限速，配合 IDM 多线程加速下载", "通过百度网盘的 WebDAV 接口挂载为本地磁盘直接读取"],
            1, "网盘文件应先保存到自己网盘再下载到本地。「在线解压」可能受限且无法保证完整性。"),

        new("你收到一个磁力链接（magnet:?xt=...），想下载对应资源，应该？",
            ["把磁力链接当成普通网址粘贴到浏览器地址栏，浏览器会自动解析并下载文件", "使用 qBittorrent 等 BT 客户端导入磁力链接，通过 DHT 网络获取元数据", "把 magnet 链接转换为 HTTP 链接，用 wget 或 curl 命令行下载", "在搜索引擎中搜索该磁力链接对应的 .torrent 种子文件再下载"],
            1, "磁力链接是 BT 协议的资源标识，需专用 BT 客户端（如 qBittorrent）解析下载。"),

        new("你要重装 Windows 系统，正确的做法是？",
            ["使用「老毛桃」PE 工具箱，内置了万能驱动和系统优化脚本，一键部署", "从微软官网下载 Media Creation Tool 制作官方启动 U 盘，确保镜像纯净", "下载第三方 Ghost 镜像（如雨林木风），已预装常用软件省去配置时间", "通过 DISM++ 直接将 WIM 镜像释放到硬盘分区，跳过安装向导更快"],
            1, "第三方装机工具常捆绑推广甚至植入后门。微软官方 Media Creation Tool 可制作纯净启动盘。"),

        // ── 常识与概念 ──
        new("Excel 中需要对一列数字求和，最高效的方法是？",
            ["用计算器算好后手动输入结果，避免公式导致文件体积膨胀", "选中数据区域下方单元格，按 Alt+= 快捷键插入 SUM 函数，配合自动填充批量处理", "使用 VLOOKUP 函数配合 IF 条件判断实现动态求和", "将数据导入 Power Query 编辑器，通过 M 语言编写聚合查询"],
            1, "Alt+= 可快速插入 SUM 求和公式，配合自动填充（拖拽单元格右下角）可批量处理多列数据。"),

        new("别人通过网盘给你分享了一个 .exe 文件，你应该？",
            ["直接运行，网盘平台已经做了文件安全扫描", "保持警惕，先用杀毒软件扫描确认安全后再运行，优先从可信渠道获取软件", "只要文件带有有效的数字签名就绝对安全，无需扫描直接运行", "用沙箱（Sandboxie）运行，即使有病毒也不会影响宿主系统"],
            1, "来路不明的 .exe 文件可能携带木马。应先用杀毒软件扫描，确认来源可信后再执行。"),

        new("以下关于「电子邮箱」的描述，最准确的是？",
            ["邮箱就是手机号码的网络化映射，通过 SMS 网关实现邮件收发", "电子邮箱（Email）是基于 SMTP/IMAP 协议的网络通信地址，可收发邮件、注册账号、接收验证码", "邮箱是一种即时通讯工具，功能类似微信但使用异步消息队列", "邮箱是电脑本地的用户账户凭证，存储在 SAM 数据库中"],
            1, "电子邮箱（如 xxx@qq.com）基于 SMTP/IMAP 协议，是互联网基础通信工具，几乎所有网络服务都需要。"),

        new("你用手机下载了一个 .exe 文件，提示无法打开，原因是？",
            ["手机的 ARM 处理器缺少 x86 指令集的微码支持，无法解析 PE 格式", ".exe 是 Windows PE（Portable Executable）格式，Android/iOS 系统无法运行", "文件在下载过程中因网络丢包导致二进制校验失败", "手机的 SELinux 安全策略阻止了未签名二进制文件的执行"],
            1, ".exe 是 Windows 平台的 PE（Portable Executable）可执行格式，Android 使用 ELF/APK，架构完全不同。"),
    ];

    public DigitalLiteracyTestPage()
    {
        InitializeComponent();
        _selectedAnswers = new int[TotalQuestions];
        for (int i = 0; i < TotalQuestions; i++)
            _selectedAnswers[i] = -1;
        Loaded += (_, _) => ShowWelcome();
    }

    #region Welcome Screen

    private void ShowWelcome()
    {
        _currentQuestion = -1;
        ContentPanel.Children.Clear();

        var stack = new StackPanel
        {
            Spacing = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 560
        };

        // Icon
        var iconBorder = new Border
        {
            Width = 120,
            Height = 120,
            CornerRadius = new CornerRadius(28),
            Background = new SolidColorBrush(Color.FromArgb(30, 96, 165, 250)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 96, 165, 250)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = "🖥️",
                FontSize = 56,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        stack.Children.Add(iconBorder);

        // Title
        stack.Children.Add(new TextBlock
        {
            Text = "电子文盲等级测试",
            FontSize = 32,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        // Description
        stack.Children.Add(new TextBlock
        {
            Text = "只要连成一条线，说明你是电子文盲。\n共 25 道选择题，满分 100 分，测测你的电脑基础知识水平！",
            FontSize = 15,
            Foreground = new SolidColorBrush(ThemeColors.SecondaryText),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center
        });

        // Scoring rules card
        var rulesCard = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24, 20, 24, 20)
        };
        var rulesStack = new StackPanel { Spacing = 12 };
        rulesStack.Children.Add(new TextBlock
        {
            Text = "📊 评分标准",
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });

        var levels = new (string Emoji, string Label, string Range, Color Color)[]
        {
            ("🏆", "电脑高手", "≥ 80 分", ThemeColors.AccentGreen),
            ("😐", "普通电子文盲", "60 ~ 79 分", ThemeColors.AccentOrange),
            ("💀", "超级电子文盲", "< 60 分", ThemeColors.AccentRed)
        };

        foreach (var (emoji, label, range, color) in levels)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new TextBlock
            {
                Text = emoji,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(color)
            });
            row.Children.Add(new TextBlock
            {
                Text = range,
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                VerticalAlignment = VerticalAlignment.Center
            });
            rulesStack.Children.Add(row);
        }

        rulesCard.Child = rulesStack;
        stack.Children.Add(rulesCard);

        // Start button
        var startBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE768", FontSize = 16 },
                    new TextBlock { Text = "开始测试", FontSize = 16 }
                }
            },
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Padding = new Thickness(40, 12, 40, 12),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        startBtn.Click += (_, _) =>
        {
            _currentQuestion = 0;
            _totalScore = 0;
            _answered = false;
            for (int i = 0; i < TotalQuestions; i++)
                _selectedAnswers![i] = -1;
            _shuffledOrders = new int[TotalQuestions][];
            for (int i = 0; i < TotalQuestions; i++)
                _shuffledOrders[i] = CreateShuffle(Questions[i].Options.Length);
            ShowQuestion();
        };
        stack.Children.Add(startBtn);

        ContentPanel.Children.Add(stack);
        PlayTransition();
    }

    #endregion

    #region Question Screen

    private void ShowQuestion()
    {
        ContentPanel.Children.Clear();
        _answered = false;
        var q = Questions[_currentQuestion];

        var stack = new StackPanel
        {
            Spacing = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 600,
            Width = 600
        };

        // Native ProgressBar
        var progressRow = new StackPanel { Spacing = 8 };
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = TotalQuestions,
            Value = _currentQuestion + 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 6
        };
        progressRow.Children.Add(progressBar);

        var progressText = new TextBlock
        {
            Text = $"第 {_currentQuestion + 1} 题 / 共 {TotalQuestions} 题  ·  当前得分 {_totalScore} 分",
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        progressRow.Children.Add(progressText);
        stack.Children.Add(progressRow);

        // Question number + text
        var questionHeader = new TextBlock
        {
            Text = $"第 {_currentQuestion + 1} 题（{PointsPerQuestion} 分）",
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.AccentBlue),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        stack.Children.Add(questionHeader);

        stack.Children.Add(new TextBlock
        {
            Text = q.Question,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });

        // Options（显示顺序已按洗牌结果排列，正确项不一定在 B）
        var optionsPanel = new StackPanel { Spacing = 10 };
        string[] labels = ["A", "B", "C", "D"];
        var order = _shuffledOrders![_currentQuestion];

        for (int i = 0; i < q.Options.Length; i++)
        {
            int displayIndex = i;
            int originalIndex = order[i];
            var optionCard = BuildOptionCard(labels[displayIndex], q.Options[originalIndex], originalIndex, q);
            optionsPanel.Children.Add(optionCard);
        }
        stack.Children.Add(optionsPanel);

        // Feedback card (hidden initially)
        var feedbackCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14, 18, 14),
            Visibility = Visibility.Collapsed,
            Name = "FeedbackCard"
        };
        var feedbackStack = new StackPanel { Spacing = 6 };
        var feedbackTitle = new TextBlock
        {
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Name = "FeedbackTitle"
        };
        var feedbackText = new TextBlock
        {
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.SecondaryText),
            Name = "FeedbackText"
        };
        feedbackStack.Children.Add(feedbackTitle);
        feedbackStack.Children.Add(feedbackText);
        feedbackCard.Child = feedbackStack;
        stack.Children.Add(feedbackCard);

        // Next button (hidden initially)
        var nextBtn = new Button
        {
            Content = _currentQuestion < TotalQuestions - 1 ? "下一题" : "查看结果",
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Padding = new Thickness(36, 10, 36, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Name = "NextBtn"
        };
        nextBtn.Click += (_, _) =>
        {
            if (_currentQuestion < TotalQuestions - 1)
            {
                _currentQuestion++;
                ShowQuestion();
            }
            else
            {
                ShowResult();
            }
        };
        stack.Children.Add(nextBtn);

        ContentPanel.Children.Add(stack);
        PlayTransition();
    }

    private Border BuildOptionCard(string label, string text, int originalIndex, QuizQuestion q)
    {
        bool isCorrect = originalIndex == q.CorrectIndex;

        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 14, 16, 14),
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            Tag = originalIndex
        };

        // 两列 Grid 约束宽度：横向 StackPanel 不约束子元素宽度，长选项文本会直接溢出卡片
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Label badge
        var labelBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(30, 96, 165, 250)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(ThemeColors.AccentBlue),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(labelBorder, 0);
        row.Children.Add(labelBorder);

        // Option text：单行省略，悬停卡片时走马灯滚动展示全文
        var optionText = new MarqueeText { Text = text };
        Grid.SetColumn(optionText, 1);
        row.Children.Add(optionText);

        card.Child = row;

        // Hover
        card.PointerEntered += (_, _) =>
        {
            optionText.Start();
            if (!_answered)
            {
                card.BorderBrush = new SolidColorBrush(ThemeColors.AccentBlue);
                card.Background = new SolidColorBrush(Color.FromArgb(15, 96, 165, 250));
            }
        };
        card.PointerExited += (_, _) =>
        {
            optionText.Stop();
            if (!_answered)
            {
                card.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);
                card.Background = new SolidColorBrush(ThemeColors.CardBg);
            }
        };

        // Click
        card.Tapped += (_, _) =>
        {
            if (_answered) return;
            _answered = true;
            _selectedAnswers![_currentQuestion] = originalIndex;

            if (isCorrect)
                _totalScore += PointsPerQuestion;

            // Highlight all options
            var optionsPanel = (StackPanel)card.Parent;
            foreach (var child in optionsPanel.Children)
            {
                if (child is Border optCard)
                {
                    int idx = (int)optCard.Tag!;
                    bool thisCorrect = idx == q.CorrectIndex;
                    bool thisSelected = idx == originalIndex;

                    if (thisCorrect)
                    {
                        optCard.Background = new SolidColorBrush(Color.FromArgb(30, 74, 222, 128));
                        optCard.BorderBrush = new SolidColorBrush(ThemeColors.AccentGreen);
                        if (optCard.Child is Grid sp && sp.Children[0] is Border lb)
                        {
                            lb.Background = new SolidColorBrush(Color.FromArgb(40, 74, 222, 128));
                            if (lb.Child is TextBlock lt)
                                lt.Foreground = new SolidColorBrush(ThemeColors.AccentGreen);
                        }
                    }
                    else if (thisSelected && !thisCorrect)
                    {
                        optCard.Background = new SolidColorBrush(Color.FromArgb(30, 248, 113, 113));
                        optCard.BorderBrush = new SolidColorBrush(ThemeColors.AccentRed);
                        if (optCard.Child is Grid sp && sp.Children[0] is Border lb)
                        {
                            lb.Background = new SolidColorBrush(Color.FromArgb(40, 248, 113, 113));
                            if (lb.Child is TextBlock lt)
                                lt.Foreground = new SolidColorBrush(ThemeColors.AccentRed);
                        }
                    }
                    else
                    {
                        optCard.Opacity = 0.5;
                    }
                }
            }

            // Show feedback
            var parent = (StackPanel)ContentPanel.Children[0];
            foreach (var child in parent.Children)
            {
                if (child is Border fb && fb.Name == "FeedbackCard")
                {
                    fb.Visibility = Visibility.Visible;
                    if (isCorrect)
                    {
                        fb.Background = new SolidColorBrush(Color.FromArgb(25, 74, 222, 128));
                        ((TextBlock)((StackPanel)fb.Child).Children[0]).Text = "✅ 回答正确！";
                        ((TextBlock)((StackPanel)fb.Child).Children[0]).Foreground = new SolidColorBrush(ThemeColors.AccentGreen);
                    }
                    else
                    {
                        fb.Background = new SolidColorBrush(Color.FromArgb(25, 248, 113, 113));
                        ((TextBlock)((StackPanel)fb.Child).Children[0]).Text = "❌ 回答错误";
                        ((TextBlock)((StackPanel)fb.Child).Children[0]).Foreground = new SolidColorBrush(ThemeColors.AccentRed);
                    }
                    ((TextBlock)((StackPanel)fb.Child).Children[1]).Text = q.Explanation;
                }
                if (child is Button btn && btn.Name == "NextBtn")
                {
                    btn.Visibility = Visibility.Visible;
                }
            }
        };

        return card;
    }

    #endregion

    #region Result Screen

    private void ShowResult()
    {
        ContentPanel.Children.Clear();

        double percentage = (double)_totalScore / MaxScore * 100;

        string level;
        string emoji;
        string description;
        Color levelColor;

        if (percentage >= 80)
        {
            level = "电脑高手";
            emoji = "🏆";
            description = "恭喜你！你对电脑基础知识掌握得很好，完全不是电子文盲！你已经超越了绝大多数用户，继续保持！";
            levelColor = ThemeColors.AccentGreen;
        }
        else if (percentage >= 60)
        {
            level = "普通电子文盲";
            emoji = "😐";
            description = "你有一些电脑基础，但还有很多需要学习的地方。建议多了解系统操作、安全知识和常用技巧，告别电子文盲指日可待！";
            levelColor = ThemeColors.AccentOrange;
        }
        else
        {
            level = "超级电子文盲";
            emoji = "💀";
            description = "你的电脑基础知识亟需加强！别担心，每个人都是从零开始的。建议从基础操作学起，多练习常用功能，慢慢就能上手了。";
            levelColor = ThemeColors.AccentRed;
        }

        var stack = new StackPanel
        {
            Spacing = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 560
        };

        // Big emoji
        var emojiBorder = new Border
        {
            Width = 140,
            Height = 140,
            CornerRadius = new CornerRadius(32),
            Background = new SolidColorBrush(Color.FromArgb(25, levelColor.R, levelColor.G, levelColor.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, levelColor.R, levelColor.G, levelColor.B)),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = emoji,
                FontSize = 64,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        stack.Children.Add(emojiBorder);

        // Level title
        stack.Children.Add(new TextBlock
        {
            Text = level,
            FontSize = 32,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(levelColor),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        // Score card
        var scoreCard = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(28, 24, 28, 24),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var scoreStack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };

        var scoreNumber = new TextBlock
        {
            Text = $"{_totalScore}",
            FontSize = 56,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(levelColor),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        scoreStack.Children.Add(scoreNumber);

        scoreStack.Children.Add(new TextBlock
        {
            Text = $"/ {MaxScore} 分",
            FontSize = 16,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        // Native ProgressBar for result
        var resultBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = MaxScore,
            Value = _totalScore,
            Width = 300,
            HorizontalAlignment = HorizontalAlignment.Center,
            Height = 8
        };
        scoreStack.Children.Add(resultBar);

        scoreStack.Children.Add(new TextBlock
        {
            Text = $"正确率 {percentage:F0}%  ·  答对 {_totalScore / PointsPerQuestion} / {TotalQuestions} 题",
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.SecondaryText),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        scoreCard.Child = scoreStack;
        stack.Children.Add(scoreCard);

        // Description
        stack.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 15,
            Foreground = new SolidColorBrush(ThemeColors.SecondaryText),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            MaxWidth = 480
        });

        // Answer summary
        var summaryCard = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16, 20, 16)
        };
        var summaryStack = new StackPanel { Spacing = 10 };
        summaryStack.Children.Add(new TextBlock
        {
            Text = "📋 答题详情",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });

        for (int i = 0; i < TotalQuestions; i++)
        {
            bool isCorrect = _selectedAnswers![i] == Questions[i].CorrectIndex;

            var detailRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            detailRow.Children.Add(new TextBlock
            {
                Text = isCorrect ? "✅" : "❌",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            });
            detailRow.Children.Add(new TextBlock
            {
                Text = $"第 {i + 1} 题：{Questions[i].Question}",
                FontSize = 13,
                Foreground = new SolidColorBrush(isCorrect ? ThemeColors.AccentGreen : ThemeColors.AccentRed),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 400
            });
            summaryStack.Children.Add(detailRow);
        }

        summaryCard.Child = summaryStack;
        stack.Children.Add(summaryCard);

        // Retry button
        var retryBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 16 },
                    new TextBlock { Text = "重新测试", FontSize = 15 }
                }
            },
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Padding = new Thickness(36, 10, 36, 10),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        retryBtn.Click += (_, _) => ShowWelcome();
        stack.Children.Add(retryBtn);

        ContentPanel.Children.Add(stack);
        PlayTransition();
    }

    #endregion

    #region Animation

    private void PlayTransition()
    {
        var fadeIn = new DoubleAnimation
        {
            From = 0, To = 1,
            Duration = TimeSpan.FromMilliseconds(350),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, ContentPanel);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");

        var slideUp = new DoubleAnimation
        {
            From = 30, To = 0,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideUp, ContentPanelTransform);
        Storyboard.SetTargetProperty(slideUp, "Y");

        var sb = new Storyboard();
        sb.Children.Add(fadeIn);
        sb.Children.Add(slideUp);
        ContentPanel.Opacity = 0;
        sb.Begin();
    }

    #endregion

    // Fisher-Yates 洗牌：返回 [0..count) 的一个随机排列
    private static int[] CreateShuffle(int count)
    {
        var order = Enumerable.Range(0, count).ToArray();
        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    #region Data Model

    // 单行走马灯文本：外层 Grid 手动裁剪，内层 TextBlock 按自然宽度全文渲染，
    // Start() 后平移来回滚动展示被裁掉的部分，Stop() 复位
    private sealed class MarqueeText : Grid
    {
        private readonly TextBlock _inner = new()
        {
            FontSize = 15,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        private readonly TranslateTransform _shift = new();
        private Storyboard? _story;

        public MarqueeText()
        {
            _inner.RenderTransform = _shift;
            Children.Add(_inner);
            SizeChanged += (_, _) => ApplyClip();
            Unloaded += (_, _) => Stop();
        }

        public string Text
        {
            get => _inner.Text;
            set
            {
                Stop();
                _inner.Text = value;
                _inner.Width = double.NaN; // 恢复自动宽度
            }
        }

        // Grid 默认不裁剪溢出内容，必须手动挂矩形 Clip，滚动才能只在本列范围内显示
        private void ApplyClip()
        {
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, ActualWidth, ActualHeight) };
        }

        public void Start()
        {
            if (_story is not null) return;
            UpdateLayout();
            ApplyClip();
            // 约束布局下的 DesiredSize 可能被钳到容器宽，手动用无限宽量一次拿文本自然宽度
            _inner.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            double natural = _inner.DesiredSize.Width;
            double overflow = natural - ActualWidth;
            if (overflow <= 4) return;

            _inner.Width = natural; // 固定为自然宽度，确保被裁部分的文字真实渲染
            var anim = new DoubleAnimation
            {
                To = -overflow,
                Duration = TimeSpan.FromSeconds(Math.Max(1.5, overflow / 50)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(anim, _shift);
            Storyboard.SetTargetProperty(anim, "X");
            _story = new Storyboard();
            _story.Children.Add(anim);
            _story.Begin();
        }

        public void Stop()
        {
            _story?.Stop();
            _story = null;
            _shift.X = 0;
        }
    }

    private sealed class QuizQuestion(
        string question,
        string[] options,
        int correctIndex,
        string explanation)
    {
        public string Question { get; } = question;
        public string[] Options { get; } = options;
        public int CorrectIndex { get; } = correctIndex;
        public string Explanation { get; } = explanation;
    }

    #endregion
}

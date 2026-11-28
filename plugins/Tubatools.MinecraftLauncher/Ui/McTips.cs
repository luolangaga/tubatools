namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>启动页「你知道吗」卡片使用的提示（原创文案）。</summary>
internal static class McTips
{
    private static readonly string[] Tips =
    [
        "现代 Minecraft 风格：模糊全景背景、圆角玻璃卡片与左侧启动面板，所有内容都在一个窗口里完成。",
        "Forge、NeoForge、Fabric、Quilt 版本会继承原版的 JSON，启动器会自动合并依赖并识别加载器。",
        "版本隔离开启后，每个版本使用 versions/<版本名>/ 作为游戏目录，存档与模组互不干扰。",
        "第一次启动某个版本时，启动器会解压 Natives；如果游戏突然闪退，先检查日志里是否有缺失库。",
        "Java 21 是 1.20.5 及以上版本的最低要求；更老的版本通常需要 Java 8。",
        "在设置里把 Java 指定为某个 javaw.exe，可以绕过自动检测选错版本的问题。",
        "内存并不是越大越好：原版 2–4 GB 足够，大型整合包再按需提高。",
        "下载页的「自动」源会优先使用 BMCLAPI 镜像，失败后自动回退官方源。",
        "离线账户的 UUID 由用户名计算，换名字会被服务器当成另一个玩家。",
        "缺少资源文件时游戏仍然能启动，但可能没有声音或语言文件不完整。",
        "启动日志卡片会实时显示 Java 输出，排查模组冲突时很有用。",
        "版本管理页可以直接打开版本文件夹，方便手动放入模组或替换配置。",
        "版本 JSON 的 inheritsFrom 支持多层继承：Forge 版本常常再继承一个原版版本。",
        "如果下载中途取消，重新安装时会跳过已经完整下载的文件。",
        "原版客户端 JAR 位于 versions/<版本名>/<版本名>.jar，删掉它游戏就无法启动。",
        "OptiFine 与 Forge 同时安装时，启动参数里的 tweakClass 顺序很关键。",
        "在设置里修改游戏目录后，版本列表会重新扫描新目录。",
        "旧版 Minecraft（1.6 及以下）使用 virtual/legacy 资源目录，安装时会自动整理。",
        "启动脚本会写入插件数据目录的 last-launch.bat，方便复现启动参数。",
        "请不要把 .minecraft 放在含有特殊符号或分号的路径下，老版本 Java 容易解析失败。",
        "游戏进程退出后，启动日志仍会保留在插件数据目录的 logs/ 下。",
        "安装版本时，单个文件失败不会中断整个安装，完成汇总里会列出被跳过的条目。",
        "资源页使用 Modrinth 搜索模组、光影和资源包，选好实例后点击一次即可装进对应目录。"
    ];

    public static string Random() => Tips[System.Random.Shared.Next(Tips.Length)];
}

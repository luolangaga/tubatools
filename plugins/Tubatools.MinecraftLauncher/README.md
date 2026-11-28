# 我的世界启动器（图吧工具箱插件）

现代 Minecraft 风格的启动器插件：模糊全景背景、小圆角卡片、左侧启动面板，
安装流程参考 PCL CE 的「选择版本 → 选择加载器 → 一键安装」思路，并把模组 / 光影 / 资源包
的浏览与安装收进同一个下载页。

## 一键安装（游戏版本）

- 选择 **Minecraft 版本**、**加载器**、**加载器版本**，点一次「开始安装」即可：
  - 原版：自动下载版本 JSON、客户端、支持库、资源索引和全部资源对象。
  - **Fabric / Quilt**：先确保原版完整，再写入 Meta profile 并下载加载器依赖。
  - **Forge / NeoForge**：先确保原版完整，再运行官方 installer（`--installClient`），
    自动创建 `launcher_profiles.json`、下载依赖并生成版本配置。
- 已存在且校验通过的文件会自动跳过；单个文件失败不会中断整个安装，完成后汇总被跳过的条目。
- 安装完成后自动选中新版本，回到「启动」页即可开始游戏。
- 支持安装进度、取消安装和安装失败详情（Forge/NeoForge 安装器日志会写入启动日志）。

## 模组 / 光影 / 资源包

- 下载页顶部切换到 **整合包 / 模组 / 光影 / 资源包**，数据来自 [Modrinth](https://modrinth.com)。
- **整合包**：自动读取 `.mrpack` 的 Minecraft 版本和 Fabric / Quilt / Forge / NeoForge 依赖，
  创建独立实例，下载全部模组、配置与 overrides；安装进度显示在下载页和统一下载队列。
- 自动按当前实例的 Minecraft 版本与加载器筛选：
  - Fabric / Quilt 实例使用 Fabric 兼容文件；
  - Forge / NeoForge 实例使用对应加载器文件；
  - 原版实例不允许直接安装模组（会给出先安装加载器的提示）。
- 点一次「安装」即下载最新兼容版本到实例目录：
  - 模组 → `mods/`
  - 光影 → `shaderpacks/`
  - 资源包 → `resourcepacks/`
- **自动安装必装依赖**（Modrinth `required` 依赖，最多递归 16 个），无需手动找前置。
- 支持搜索、排序、加载更多，以及直接打开目标文件夹。

## 启动

- 扫描 `.minecraft/versions`，合并 `inheritsFrom` 继承链，识别原版 / Forge / NeoForge / Fabric / Quilt / OptiFine / LiteLoader。
- PCL 规则处理 `libraries` 的 `rules`、natives 解压、`arguments.jvm/game`、资源索引与占位符替换。
- 自动侦测 Java（注册表 / JAVA_HOME / 常见安装目录 / 游戏自带 runtime）；
  1.20.5+ 选择 Java 21，1.18+ 选择 Java 17，更老版本优先 Java 8。
- 版本隔离默认开启：每个版本使用 `versions/<版本名>/` 作为游戏目录。
- 游戏语言默认跟随系统（中文系统为简体中文），也可在设置 → 启动选项里选择；
  启动前写入 `options.txt`，不会再次出现默认英文。
- **微软正版登录**：账户管理 →「微软正版登录」，按提示在浏览器输入设备代码即可；
  登录成功后使用 Minecraft Services 的 access token 启动，支持自动续期。
- 离线账户（`OfflinePlayer:<name>` MD5 v3 UUID）、多账户切换。
- 表格化启动日志、实时 Java 输出、`last-launch.bat` 启动脚本。

## 微软正版登录说明

- 默认使用已获准用于 Minecraft 的公共客户端 ID。普通 Azure 应用即使开启「允许公共客户端流」，
  也通常只能完成 OAuth；Minecraft Services 会以 `Invalid app registration` / 403 拒绝。
  这是微软对 `XboxLive.signin` 的限制，不是插件能绕过的。
- 你截图里的 Azure 应用 `e04809bd-5675-406f-850d-e69b7ae0387e` 需要在
  [Minecraft Java Edition Game Service API 审核表单](https://forms.cloud.microsoft/Pages/ResponsePage.aspx?id=v4j5cvGGr0GRqy180BHbR-ajEQ1td1ROpz00KtS8Gd5UNVpPTkVLNFVROVQxNkdRMEtXVjNQQjdXVC4u)
  里提交 Client ID；获批后会收到 Mojang Enforcement 的邮件。
- 获批后到「设置 → 账户 → 自定义 Microsoft 客户端 ID」粘贴该 ID 并保存，就会使用你的应用；
  留空则继续使用内置的已获准公共客户端。Azure 清单里同时把 `isFallbackPublicClient` 设为 `true`。
- OAuth 换取 Minecraft access token 后会保存到 `<数据目录>/accounts.json`；
  access token / refresh token 均使用 Windows DPAPI（CurrentUser）加密，换 Windows 用户后需重新登录。
- 令牌在启动前自动检查，剩余不足 5 分钟时用 refresh token 自动续期；续期失败会提示重新登录。

## 设置

游戏目录、Java 指定、内存、版本隔离、窗口尺寸、下载源（官方 / BMCLAPI / 自动）、
附加 JVM / 游戏参数、账户管理与日志目录。

## 说明与限制

- 仅支持 Windows（宿主为 WinUI 3）。
- 微软登录需要账户已购买 Minecraft: Java Edition；未购买时只支持离线账户。
- OptiFine / LiteLoader 需要用户使用官方安装器；安装完成后会自动出现在版本列表。
- Forge / NeoForge 安装依赖网络与合适的 Java；安装器本身来自官方 Maven。
- 界面布局和安装流程参考 [PCL CE](https://github.com/PCL-Community/PCL-CE)，
  代码按行为重写；全景、方块与图标均为本项目生成或使用开源图标，不含 Mojang 版权素材。
- Modrinth 图标使用 **Six Labors ImageSharp 3.1.12** 解码为 PNG（Six Labors Split License 1.0；
  本项目为 GPL-3.0 开源项目，适用 Apache 2.0 分支），详见 `THIRD-PARTY-NOTICES.md`。

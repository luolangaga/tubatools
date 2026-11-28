# macOS 安装助手（Tubatools 插件）

一个原生 C# + WinUI 3 的 macOS 安装辅助工具，入口在「图吧工具箱 → 系统工具 → macOS 安装助手」。它把黑苹果安装前最容易出错的几件事串成一条可检查的流程：

1. **检查本机硬件**：CPU、主板、BIOS、显卡、网卡、NVMe/存储控制器、电池与机型。
2. **判断兼容性并给出机型**：按 CPU 代际、核显/独显、笔记本/台式机选择 SMBIOS 与所需 kext、ACPI。
3. **构建 OpenCore EFI**：解压官方 OpenCorePkg，复制 ACPI 与 kext，生成 `config.plist`，最后调用官方 `ocvalidate` 校验。
4. **生成 BIOS 与安装步骤**：按主板厂商给出进入 BIOS/启动菜单的按键，列出 Secure Boot、CSM、VT-d、VMD、Resizable BAR 等开关。
5. **制作安装 U 盘**：从 Apple 恢复服务器下载 `BaseSystem.dmg` 与 `chunklist`，校验分块 SHA-256，重建 U 盘 GPT + FAT32 分区，写入 `EFI/` 与 `com.apple.recovery.boot/`。
6. **启动失败诊断**：读取 `opencore-*.txt` / `panic-*.txt`，结合用户输入的屏幕最后一行，给出按优先级排列的修复步骤。
7. **EFI 体检**：选择已有的 EFI 文件夹，用 `ocvalidate` 检查 `config.plist`。

## 新手怎么用

打开插件后会自动检查这台电脑，不需要先找按钮：

1. **看第 1 步的结论**。显示「这台电脑可以继续」或「可以继续，但只能用基础显示」就可以点「下一步：生成 EFI」；红色错误才需要先按提示处理。
2. **第 2 步点「一键生成 EFI」**。组件都在插件里，不用装 Python；成功后直接点「下一步：设置 BIOS」。
3. **第 3 步照着图文指南改 BIOS**。Secure Boot、CSM、VT-d、VMD 等开关都在指南里，不同品牌叫法不同，找意思相同的项。
4. **第 4 步插 U 盘、点「插入好了，重新查找 U 盘」**。程序只列 U 盘，制作会清空它，先备份。
5. 启动失败时点「遇到问题」，可以读取 U 盘里的 OpenCore 日志，也可以只填屏幕最后一行文字。

> 这是面向折腾/调试的通用构建器，不是商业级装机服务。特定主板 quirk、笔记本 I2C 触控板、音频 layout、Wi-Fi、AMD CPU 内核补丁仍需要按真机迭代。

## 引擎原理（原生实现）

没有嵌入 Python，也没有调用外部安装器。整个流程由 C# 编排：

```
WMI/CIM 硬件扫描
      ↓
CompatibilityRules（CPU/GPU/网卡/存储规则）
      ↓
PayloadManager（官方 OpenCorePkg + kext 压缩包 → 本地缓存）
      ↓
EfiBuilder（复制 EFI、ACPI、kext；macserial 生成 SMBIOS）
      ↓
ConfigGenerator（改写官方 Sample.plist）
      ↓
ocvalidate（官方校验器，失败则不产出可用结果）
      ↓
RecoveryImageService（Apple RecoveryImage 协议 + CNKL 分块校验）
      ↓
UsbService（diskpart 重建 GPT/FAT32 + C# 流式复制 + 进度）
      ↓
Diagnostics（日志特征匹配 → 修复步骤）
```

关键点：

- **OpenCore 部分**使用 OpenCorePkg 官方发布包中的 `Sample.plist`、`macserial`、`ocvalidate`、`BOOTx64.efi`、`OpenRuntime.efi`、`OpenHfsPlus.efi` 和 ACPI 样例；插件只做参数化生成，不修改 OpenCore 源码。
- **kext 部分**使用各项目官方 release 压缩包，按硬件规则选择；VoodooPS2 的插件 kext 保持 `VoodooPS2Controller.kext/Contents/PlugIns/...` 的嵌套结构，不拆散到 `Kexts` 根目录。
- **SMBIOS**优先调用官方 `macserial -m <model>`；如果它没有返回可用值，会使用占位序列号并明确警告（安装通常可继续，但 iMessage/App Store 可能无法激活）。
- **恢复镜像**走 Apple 的 `osrecovery.apple.com` 会话与 `InstallationPayload/RecoveryImage` 接口，下载 `BaseSystem.chunklist` 后用内置 Apple EFI ROM 公钥验证 CNKL 签名，再按 chunk 做 SHA-256 校验；支持离线缓存校验，并会尝试 `.part` 断点续传（Apple CDN 对非零 Range 返回空响应时自动丢掉断点从 0 重下）。下载页可填 HTTP 代理。
- **U 盘写入**只通过 `diskpart` 做分区/格式化，实际文件复制由 C# `FileStream` 完成，带总字节进度与取消检查；格式化前会重新枚举磁盘并比对容量/UniqueId/序列号，拒绝系统盘、只读盘、离线盘和空间不足的盘。

## 构建

```powershell
dotnet build .\plugins\Tubatools.MacOSInstaller\Tubatools.MacOSInstaller.csproj -c Release
dotnet pack .\plugins\Tubatools.MacOSInstaller\Tubatools.MacOSInstaller.csproj -c Release -o .\plugins\Tubatools.MacOSInstaller\dist
```

插件包不携带 WinUI 框架 DLL，运行时使用宿主提供的 WinUI / Windows App SDK 类型。`assets/payload/` 下的官方压缩包会随 nupkg 分发。

## 安装

在工具箱的插件页安装 `dist/Tubatools.MacOSInstaller.<version>.nupkg`，或把包内容解压到：

```
%LOCALAPPDATA%\TubaWinUi3\Plugins\tubatools.macosinstaller\<version>\
```

并在同目录 `installed.json` 注册插件记录。插件注册的工具 Id 是 `macos-installer`。

## 运行数据

默认在插件数据目录下：

```
payload/                 解压后的 OpenCore 与 kext 缓存（带组件签名，组件变化会自动重建）
build/EFI/               最近一次构建的 EFI
build/BIOS.html          生成的 BIOS/安装步骤页
recovery/<darwin>/       BaseSystem.dmg、BaseSystem.chunklist、BaseSystem.dmg.part
logs/                    PowerShell / macserial / ocvalidate / diskpart 日志
```

## 使用建议

1. 兼容性报告里的红色问题优先处理。确实要继续尝试时，才在「高级选项」里勾选「跳过兼容性检查」；生成的 EFI 不保证能启动。
2. 制作 U 盘会**抹掉整块磁盘**。程序会弹两次确认并在写入前复核磁盘身份，但无法替你判断盘里是否有重要数据。
3. 恢复镜像（BaseSystem.dmg）通常约 0.8～1.5 GB，请至少使用 2 GB U 盘，建议 8 GB 以上；程序会在格式化前按实际镜像大小检查空间。下载失败可在 U 盘页填写 HTTP 代理（如 `http://127.0.0.1:7890`）后重试。
4. 安装阶段优先使用有线网络。Wi-Fi 没有通用驱动，Intel 无线网卡需要额外的 AirportItlwm/itlwm 且版本必须匹配。
5. 装完后把 U 盘里的 `EFI` 复制到装 macOS 那块 SSD 的 EFI 分区，不要覆盖 Windows 盘，再脱离 U 盘启动。

## 已知边界

- 仅对 **Intel** 平台生成通用 EFI；AMD CPU 需要专门的内核补丁集，本版本明确拒绝。
- 默认安装版本：第 11 代及更新的 Intel 平台自动选 macOS 26 Tahoe，其他平台选 macOS 15 Sequoia；可在第 1 步「高级选项」里改。
- Intel 核显只覆盖 UHD 630（Coffee Lake / Comet Lake 常见 device id）。Intel Arc、Iris Xe、UHD 770 等没有 macOS 驱动；本工具仍允许继续生成，但只能用固件提供的基础显示，可能低分辨率、无图形加速。建议把 macOS 装到独立 SSD，不要动内置硬盘和 Windows。社区有面向虚拟机的 Xe3 实验驱动，但缺少可分发许可证，未内置。
- AMD 独显只做通用 WhateverGreen/Navi 启动参数，不覆盖所有专业卡、MXM 卡与笔记本独显内屏。
- 笔记本 PS/2 键鼠/触控板使用 VoodooPS2Controller；I2C 精密触控板需要按机型定制 VoodooI2C，未内置。
- 音频只写 `layout-id` 与 `alcid`，具体 layout 需要按声卡型号试。
- Apple 恢复镜像走 `osrecovery.apple.com` → `oscdn.apple.com`；国内部分网络下 oscdn 会在 1 MB 处提前断流（实测 2026-11），需要配置可用的系统代理或换网络后重试。下载器会识别这种无效断点并从 0 重下。
- 真实 U 盘写入与真机启动必须在有 U 盘/目标机器的环境中验证；本仓库测试覆盖硬件扫描、EFI 构建、ocvalidate、chunklist 签名与分块校验，以及在当前网络可完成的部分恢复镜像下载。

## 第三方组件

见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。插件不包含任何 Apple 系统文件；恢复镜像在用户制作 U 盘时从 Apple 服务器下载。

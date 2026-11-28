# 第三方组件与许可说明

本插件把下列开源组件作为**独立发布包**内置在 `assets/payload/` 中，并在运行时解压使用。各组件的版权与许可归其上游作者所有；具体条款以压缩包内的 `LICENSE` / `COPYING` 文件为准。

| 组件 | 版本/文件 | 上游 | 许可（以包内为准） | 用途 |
| --- | --- | --- | --- | --- |
| OpenCorePkg | `OpenCore-1.0.9-DEBUG.zip` | https://github.com/acidanthera/OpenCorePkg | BSD-3-Clause | `BOOTx64.efi`、`OpenCore.efi`、`Sample.plist`、ACPI 样例、`macserial`、`ocvalidate`、`OpenRuntime.efi`、`OpenHfsPlus.efi` |
| Lilu | `Lilu-1.7.2-RELEASE.zip` | https://github.com/acidanthera/Lilu | BSD-3-Clause | kext 注入基础 |
| VirtualSMC | `VirtualSMC-1.3.8-RELEASE.zip` | https://github.com/acidanthera/VirtualSMC | BSD-3-Clause | SMC 模拟及 `SMCProcessor` / `SMCSuperIO` / `SMCBatteryManager` / `SMCLightSensor` 插件 |
| WhateverGreen | `WhateverGreen-1.7.1-RELEASE.zip` | https://github.com/acidanthera/WhateverGreen | BSD-3-Clause | 显卡帧缓冲/补丁 |
| AppleALC | `AppleALC-1.9.8-RELEASE.zip` | https://github.com/acidanthera/AppleALC | BSD-3-Clause | 音频 layout 注入 |
| NVMeFix | `NVMeFix-1.1.3-RELEASE.zip` | https://github.com/acidanthera/NVMeFix | BSD-3-Clause | NVMe 电源/兼容性修补 |
| IntelMausi | `IntelMausi-1.0.8-RELEASE.zip` | https://github.com/acidanthera/IntelMausi | BSD-3-Clause | Intel 有线网卡 |
| RestrictEvents | `RestrictEvents-1.1.6-RELEASE.zip` | https://github.com/acidanthera/RestrictEvents | BSD-3-Clause | MacPro7,1 等机型的事件/内存提示修补 |
| VoodooPS2Controller | `VoodooPS2Controller-2.3.7-RELEASE.zip` | https://github.com/acidanthera/VoodooPS2 | GPL-2.0 | 笔记本 PS/2 键盘、鼠标、触控板 |
| RealtekRTL8111 | `RealtekRTL8111-V3.0.0.zip` | https://github.com/Mieze/RTL8111_driver_for_OS_X | GPL-2.0 | Realtek 有线网卡 |

## Apple 恢复镜像

插件不打包、不托管任何 Apple 系统文件。用户在「制作 U 盘」时，插件通过 Apple 的恢复服务查询并下载 `BaseSystem.dmg` / `BaseSystem.chunklist`；下载内容只保存在用户本机的插件数据目录，使用方式受 Apple 的软件许可与条款约束。

`chunklist` 的签名校验使用 OpenCore 项目中公开的 Apple EFI ROM 公钥与 CNKL 格式实现；该公钥仅用于验证 Apple 生成的恢复镜像清单，不包含 Apple 私钥。

## 免责声明

- 黑苹果安装可能违反 Apple 的软件许可；请自行确认所在地区的法律与许可要求。
- U 盘制作会删除整块磁盘的数据；请自行备份。
- 生成的 EFI 是通用起点，不保证在任何特定主板上都能启动。因使用本插件造成的硬件、数据或系统损坏，由使用者自行承担。

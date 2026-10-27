# 第三方内容声明（THIRD-PARTY NOTICE）

> 本文件用于说明本仓库中第三方内容的权利边界与使用注意事项，避免将其误解为由本项目统一按 GPL-3.0 授权。

## 1. 适用范围（Scope）

- 根目录 [LICENSE](LICENSE)（GPL-3.0）适用于本项目原创部分：源代码、脚本、配置、文档与原创资源。
- 以下内容**不自动适用 GPL-3.0**：
  - `TubaWinUi3.WinUI3/Tools/` 目录中的第三方工具及其附带文件；
  - 运行时下载或调用的外部工具、外部资源；
  - 第三方二进制文件、商标、品牌名称、图标、数据与其他非原创内容。

## 2. 第三方内容的许可与分发要求

- 第三方内容仅按其各自许可证、版权声明或权利人授权使用。
- 分发本仓库产物（源码包、安装包、便携包、镜像等）时，分发者应自行核对并遵守对应第三方条款。
- 本项目不对第三方内容进行再许可，也不将其声明为本项目原创。

## 3. 许可证查阅指引

- 典型许可证文本可在第三方工具目录内查阅，例如（非穷举）：
  - `TubaWinUi3.WinUI3/Tools/处理器工具/C2CLatency/LICENSE.txt`
  - `TubaWinUi3.WinUI3/Tools/内存工具/ZenTimings/InpOut.LICENSE.txt`
  - `TubaWinUi3.WinUI3/Tools/综合检测/AIDA64/license.txt`
- 另有部分第三方组件许可证位于其他路径（例如 `Assets/`、`Services/` 对应组件目录），请以实际随附文件为准。

## 4. 权利状态不明确内容的处理

- 对许可证未知、版权归属不明确或分发条件不清晰的第三方内容，本项目文档不作“已获授权”的断言。
- 维护者与分发者应在发布前自行核验权利来源，并在必要时移除或替换相关内容。

## 5. 法律提示

- 本文件及仓库中的许可证说明仅用于项目管理与合规提示，不构成法律意见。
- 如需商业分发，或对具体第三方工具/资源的权利状态存在疑问，请咨询相应权利人或专业法律顾问。

## 6. 移植自第三方开源项目的实现

以下功能为参照上游开源项目**重新实现**的托管代码（未搬运其源代码文件），在此保留署名：

- **文件占用查看**（内置工具 `file-locksmith`，`TubaWinUi3.WinUI3/Services/FileLock/`）
  参照 Microsoft **PowerToys** 的 File Locksmith 模块（`src/modules/FileLocksmith/`）实现，
  MIT License，Copyright (c) Microsoft Corporation。
  上游项目：https://github.com/microsoft/PowerToys

- **环境变量**（内置工具 `environment-variables`，`TubaWinUi3.WinUI3/Services/EnvVars/`）
  参照 Microsoft **PowerToys** 的 Environment Variables 模块（`src/modules/EnvironmentVariables/`）实现，
  MIT License，Copyright (c) Microsoft Corporation。
  上游项目：https://github.com/microsoft/PowerToys

- **文件占用查看的右键菜单处理程序**（`TubaWinUI3.ShellExtension/`，Win11 新版菜单 `IExplorerCommand`）
  按 Microsoft Learn 官方文档《Add a File Explorer context menu command to a packaged desktop app》
  的接口契约实现（接口 GUID 均为 Windows SDK 公共定义，未复制示例源码）；
  包清单中 `windows.comServer` / `windows.fileExplorerContextMenus` / 执行别名的组合形态
  参照 **NanaZip** 的公开包清单实践（MIT License，Copyright (c) Mouri Naruto），未复制其源码。
  文档：https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer
  参照项目：https://github.com/M2Team/NanaZip

## 7. 运行时下载的第三方组件

以下组件**不随本仓库/安装包分发**，由用户在首次使用时按需下载，属于独立程序，按其自身许可证授权：

- **EasyTier**（游戏联机助手使用的 P2P 虚拟网络组件，`TubaWinUi3.WinUI3/Services/GameTunnel/EasyTier/`）
  LGPL-3.0。项目地址：https://github.com/EasyTier/EasyTier
  本程序只调用其官方发布的未修改版本，并在下载后逐个文件校验 SHA-256。
  需要替换/重新链接该组件的用户可从上述地址获取对应版本的完整源码，
  或直接删除 `<数据目录>/GameTunnel/EasyTier/` 后用自行编译的 `easytier-core.exe` / `easytier-cli.exe` 替换
  （文件名与所在目录保持不变即可被本程序使用）。
  联机机制（邀请码=网络名+密钥、端口转发、Minecraft 局域网广播）参照 **Campfire**（https://github.com/Xocio/Campfire）
  的公开实现重新编写，未复制其源代码；Campfire 采用何种许可请以其仓库声明为准。

---

# Third-Party Notice (English)

This file clarifies rights boundaries for third-party content in this repository and avoids implying that all bundled content is relicensed under GPL-3.0 by this project.

## 1. Scope

- [LICENSE](LICENSE) (GPL-3.0) applies to original project materials (source code, scripts, configs, docs, and original assets).
- The following are **not automatically covered by GPL-3.0**:
  - Third-party tools and bundled files under `TubaWinUi3.WinUI3/Tools/`;
  - Externally downloaded/called tools and resources;
  - Third-party binaries, trademarks, brand names, icons, data, and other non-original content.

## 2. Licensing and Redistribution Requirements

- Third-party content is used only under each item’s own license, copyright notice, or rightsholder authorization.
- Redistributors (source/package/installer/portable/mirror) must independently verify and comply with all applicable third-party terms.
- This project does not relicense third-party content and does not claim it as original work.

## 3. Where to Find Third-Party License Texts

- Example locations (non-exhaustive):
  - `TubaWinUi3.WinUI3/Tools/处理器工具/C2CLatency/LICENSE.txt`
  - `TubaWinUi3.WinUI3/Tools/内存工具/ZenTimings/InpOut.LICENSE.txt`
  - `TubaWinUi3.WinUI3/Tools/综合检测/AIDA64/license.txt`
- Additional notices may also exist in other component directories (for example under `Assets/` or `Services/`).

## 4. Unknown or Unclear Rights Status

- For items with unknown license or unclear rights status, this repository does not assert blanket authorization.
- Maintainers and redistributors should verify rights provenance before release and remove/replace items when necessary.

## 5. Legal Notice

- This file and related repository notices are informational and are not legal advice.
- For commercial redistribution or rights uncertainty regarding specific third-party tools/resources, consult the relevant rightsholders or professional legal counsel.

## 6. Implementations Ported from Third-Party Open-Source Projects

The following features are managed-code **re-implementations** modelled on upstream open-source projects
(no source files were copied from them). Attribution is kept here:

- **File Locksmith** (built-in tool `file-locksmith`, `TubaWinUi3.WinUI3/Services/FileLock/`)
  Modelled on the File Locksmith module of Microsoft **PowerToys** (`src/modules/FileLocksmith/`),
  MIT License, Copyright (c) Microsoft Corporation.
  Upstream: https://github.com/microsoft/PowerToys

- **Environment Variables** (built-in tool `environment-variables`, `TubaWinUi3.WinUI3/Services/EnvVars/`)
  Modelled on the Environment Variables module of Microsoft **PowerToys** (`src/modules/EnvironmentVariables/`),
  MIT License, Copyright (c) Microsoft Corporation.
  Upstream: https://github.com/microsoft/PowerToys

- **File-locksmith Explorer context-menu handler** (`TubaWinUI3.ShellExtension/`, Windows 11 modern menu `IExplorerCommand`)
  Implemented against the interface contract from Microsoft Learn's "Add a File Explorer context menu command to a
  packaged desktop app" (all interface GUIDs are public Windows SDK definitions; no sample source was copied).
  The packaging combination of `windows.comServer` / `windows.fileExplorerContextMenus` / execution alias in the
  package manifest follows the publicly available manifest practice of **NanaZip** (MIT License,
  Copyright (c) Mouri Naruto); no source files were copied from it.
  Docs: https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer
  Reference project: https://github.com/M2Team/NanaZip

## 7. Components Downloaded at Runtime

The following components are **not distributed** with this repository or installer. They are downloaded on first use
and are independent programs governed by their own licenses:

- **EasyTier** (P2P virtual-network component used by the Game Tunnel tool,
  `TubaWinUi3.WinUI3/Services/GameTunnel/EasyTier/`) — LGPL-3.0.
  Upstream: https://github.com/EasyTier/EasyTier
  This program calls the official, unmodified release and verifies each file against a pinned SHA-256 hash.
  Users who wish to replace or relink the component may obtain the complete corresponding source from the upstream
  project, or delete `<DataDir>/GameTunnel/EasyTier/` and drop in their own build of `easytier-core.exe` /
  `easytier-cli.exe` (same file names and directory layout are enough for this program to use it).
  The room-code / port-forwarding / Minecraft LAN-discovery design is modelled on the public implementation of
  **Campfire** (https://github.com/Xocio/Campfire); no source code was copied. Campfire's own license terms are
  those stated in its repository.

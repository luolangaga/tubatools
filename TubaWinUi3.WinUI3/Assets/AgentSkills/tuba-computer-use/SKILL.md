---
name: tuba-computer-use
description: 用 Microsoft Hybrid Computer Use 的 AX（UI Automation 语义树）和 GUI（真实截图与键鼠）操作 Windows 桌面。需要读取或操作任意程序界面、令牌式控件、对话框和画布时使用。
compatibility: Windows 10/11；图吧工具箱的 computer-use MCP 已连接；无需 Node.js、Python 或额外驱动。
---

# Windows 电脑操作（Hybrid Computer Use）

MCP 服务器提供两个工具：`AX`（读取/操作 UI Automation 树）和 `GUI`（整桌面截图与真实键鼠）。工具名可能带服务器前缀（如 `computer_use_AX`）。这是真实桌面操作，不是工具箱业务 API。

## 先选通道

| 目标 | 用哪个通道 |
| --- | --- |
| 文件、配置、脚本、可用命令行完成的事 | shell，不要绕道界面 |
| 菜单、对话框、按钮、输入框、列表 | `AX` 优先 |
| 自绘界面、画布、图表、纯像素判断、拖动 | `GUI` |
| 网页/已登录站点 | 优先 `browser_use` MCP（`navigate`/`snapshot`/`click`/`type`）；不要用 AX/GUI 点浏览器窗口 |

不要为了读一个值去点开整条菜单；也不要在 AX 读不到时反复猜坐标——先截图。

## AX

- `{"mode":"apps"}` 列出窗口：`w1`、`w2`…（带标题、进程和焦点标记）。
- `{"mode":"observe","window":"w1"}` 读取控件树：`r1`、`r2`…；可加 `scope:"content"` 只看内容区。
- `{"mode":"find","window":"w1","role":"button","name_contains":"保存"}` 查找控件，返回 `rN`。
- `{"mode":"act","actions":[…]}` 执行动作。一次最多 64 步，支持 `click`、`double_click`、`type`、`keypress`、`select`、`set_value`、`activate`、`close`、`scroll` 和 `wait`。
- **只使用刚返回的 ref**。ref 是当前状态的句柄，不是持久选择器；被别的通道改动后会 `stale_ref`，重新 `observe`/`find` 即可。
- 同一个窗口能确定的连续动作放进一次 `act`；下一步依赖新界面时才拆开。
- `click`/`double_click` 在 Windows 上走真实指针：目标窗口必须在前台，控件中心不能被其他窗口遮挡。被拒绝为 `runtime_error` / `point_owner_mismatch` 时不要重复同一点击；先 `activate` 目标窗口，输入框优先用 `set_value`/`type`，列表/单元格用 `select`，只有确实需要指针时才改用 `GUI`。
- 结果先给 `effect`（每个目标的字段变化）和 `windows`（新开/关闭/获得焦点的窗口，带新的 `wN`），再给 diff。**以这两行为准判断是否生效**，不要仅凭调用成功就宣布完成。
- `wait` 不发送输入：等窗口用 `{"type":"wait","window":"导出"}`；等控件状态用 `ref` + `value/state/text`；不知道下一界面标题时用 `{"type":"wait","settle":true}`。单次调用所有等待之和不超过 60 秒。
- 需要看像素时加 `observe:"full"`（同时返回整桌面截图，可直接作为 GUI 坐标基准）。

## GUI

- `{"actions":[{"type":"screenshot"}]}` 获取整桌面截图；图片按比例缩放到不超过 1366×768。
- `screenshot` 必须单独一批：要滚动/点击就直接发输入批，输入批会返回最终截图，不要在同一批里再加 `screenshot`。`zoom` 也必须单独调用。
- `screenshot`/`zoom` 是纯观察动作：每个 action 必须带 `type`，并且不能带 `settle_ms`；`settle_ms` 只允许放在**含输入动作的 GUI 调用的顶层**，且必须是数字（不要写成字符串）。纯截图调用也不要带顶层 `settle_ms`。
- **截图始终是整桌面**：HCU 的 GUI 不能按窗口截图，AX 的 `observe` 也只缩小控件树、不裁剪图片。宿主会在本次截图对应的窗口上闪一下红色边框，方便用户看到 AI 正在读哪里；该边框已排除在截图之外，不会影响你看到的画面。
- `x`、`y` 永远是**最近一次返回图片**的像素坐标，不是屏幕物理坐标；不要用截图缩放前的位置。
- `actions` 必须写成 JSON 数组，`region` 是数字数组，`keys` 是字符串数组；不要把数组序列化成字符串。桥接层会兜底纠正常见的字符串写法，但正确写法最可靠。
- 同一张截图能决定的点击/输入放一个 `actions` 数组；出现新窗口、窗口移动/缩放、切换程序后重新截图再规划。
- `{"actions":[{"type":"zoom","region":[x0,y0,x1,y1]}]}` 放大区域读小字/图标，必须单独调用；它不发送输入。
- 截图会建立布局租约；若点击位置的窗口已变化，调用会以 `gui_frame_stale` 被拒绝且**什么都没发送**，按返回的新截图重新规划。
- 输入前确认目标窗口在前台；屏幕保护、锁屏和 UAC 安全桌面不能被自动化，交还用户处理。

## 宿主控制提示与停止（必须遵守）

1. 每次 `AX`/`GUI` 调用前，宿主会显示置顶的「AI 正在操作电脑」提示；调用会阻塞到提示真正显示后才放行。这是产品安全边界，不能绕过。
2. 目标程序不在工具箱前台时，右上角会显示任务进度卡片和「停止」按钮；等待提问/审批时还会出现「回答 / 审批」。不要遮挡、移动或忽略这些覆盖层。
3. 用户点击「立即停止」后，后续调用返回 `USER_STOPPED_COMPUTER_CONTROL`。此时停止所有电脑操作，不得自行重新获取控制；只有用户明确发出新的请求/继续后才能重新操作。
4. 收到 `USER_CONTROL_BANNER_NOT_SHOWN` 表示工具箱未运行或提示无法显示：停止操作并请用户打开工具箱，不要尝试自行输入。

## 结果与错误

每个 AX/GUI 结果首行是 JSON 状态：`ok`、`code`、`hint`、`input_emitted`、`completed`。失败时按 `code` 处理：

- `invalid_argument`：没有发送输入。按 `hint`/`path` 修正参数。
- `stale_ref`：丢弃该结果的 ref，重新观察或查找。
- `gui_frame_stale`：没有发送输入。桌面布局在截图后发生了变化（新窗口、窗口移动/尺寸变化），按返回的新截图重新规划；不要重复原坐标。截图里出现「AI 正在操作电脑」提示条/进度卡属于宿主 UI，不是目标应用，不要点击或把它们当操作对象。
- `window_changed`：批次已执行到 `completed` 步后停止；先观察，再补剩余步骤。
- `wait_timeout`：等待前的步骤已执行、等待后的没有；读当前状态后调整计划，不重发同一批。
- `effect_unknown`：可能已发送输入，先检查界面再决定是否重试，避免重复执行。
- `window_not_found` / `ambiguous_window`：用 `apps` 返回的 `wN` 或更长的标题。
- `no_focused_window`：批次第一步用 `activate` 激活目标窗口。
- `app_unavailable`：程序未启动或不可访问；先等它的窗口，或者改用 GUI 读取。
- `permission_denied`：操作系统拒绝访问；恢复权限并重启提供程序，重复截图不会解决。
- `runtime_error`：按消息判断；读失败可重试一次，写操作前先观察状态。`point_owner_mismatch` 表示点击点不属于目标控件（窗口不在前台或被遮挡），先 `activate` 或换语义动作，不要重试同一坐标。

同一假设连续两次失败就换通道或换方案：不要微调坐标、补回车、加等待或重启程序来"试出来"。

## 证据原则

- 用控件值、新截图、文件内容或程序自身日志验证结果；AX/GUI 返回成功只代表输入被送达，不代表诊断、修复或测试成功。
- 不要编造硬件、温度、功耗、跑分或平台数据。读不到就明确说明缺什么、为什么。
- 高负载测试、写盘、修改系统设置按用户已给出的具体授权执行；授权不足时用原生提问卡片确认。

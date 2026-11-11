---
name: tuba-browser
description: 用 browser_use MCP 操作内置 WebView2 网页。需要打开网址、读取页面、点击链接/按钮、填写表单、截图或等待页面变化时使用。
compatibility: Windows 10/11；图吧工具箱 AI 助手已连接；使用内置 WebView2，不需要 Node.js 或额外浏览器。
---

# 浏览器控制（browser_use）

浏览器不是普通窗口：优先使用 `browser_use` 提供的语义工具，**不要**用 `computer_use` 的 AX/GUI 去点浏览器窗口。

## 当前引擎

- 唯一引擎是**内置 WebView2**：使用工具箱专属用户数据目录，**不是**用户日常 Chrome/Edge 的配置，不包含用户的登录状态、书签和历史。需要登录时，让用户在弹出的浏览器窗口里自己完成登录；不要向用户索要账号密码。
- 每次工具结果首行都会标注「内置 WebView2」和专属用户数据目录；看到 `USER_STOPPED_BROWSER_CONTROL` 就说明用户停止控制。
- `browser_status` 显示是否已启动、页面 URL/标题和用户是否已停止控制。

## 推荐流程

1. `browser_status` 确认连接。
2. `browser_navigate` 打开网址（自动补全 https）。默认会返回页面文本和可交互元素。
3. 需要操作页面时调用 `browser_snapshot`，拿到 `e1`、`e2`… ref。
4. 用 `browser_click` / `browser_type` / `browser_select` / `browser_press` 操作。
5. 页面变化后**重新 snapshot**，不要复用旧 ref。

`navigate` 之后默认已经带快照；后续点击/输入不会自动返回完整快照，需要时再调用 `browser_snapshot`。

## 工具

- `browser_navigate`：`url`，可选 `observe`（默认 true）。
- `browser_snapshot`：返回 URL、标题、可见文本和可交互元素；`include_text:false` 只看元素。
- `browser_click`：`ref` 优先；也可用 `selector` 或可见文字 `text`。匹配到多个元素时会返回候选 ref，改用 ref。
- `browser_type`：`ref`/`selector` + `text`；`clear:true` 先清空，`submit:true` 输入后按 Enter。
- `browser_press`：Enter、Tab、Escape、ArrowDown、PageDown、Control+A 等。
- `browser_scroll`：`dy` 正数向下；可带 `ref` 滚动元素内部。
- `browser_select`：原生 `<select>`，按 `value` 或可见文字 `label`。
- `browser_screenshot`：只截网页视口，用于视觉确认；截图不是操作通道。
- `browser_wait`：等待 selector 或文字出现，最多 60 秒。
- `browser_history`：`back` / `forward` / `reload`。
- `browser_eval`：只在语义操作无法完成时使用；不要用它绕过用户停止，也不要读取本机任意文件。

## 元素 ref 规则

- ref 是页面上的临时标记，只对**最近一次 snapshot** 的页面状态有效。
- 页面导航、局部刷新、弹窗变化后，旧 ref 可能失效；重新 snapshot 或观察结果里的错误。
- 跨域 iframe 内部不会出现在 snapshot 中；这时用 `browser_screenshot` 确认，再考虑坐标或让用户协助。

## 停止与安全

1. 用户可以在 AI 浏览器窗口点击「停止 AI 控制」。之后所有浏览器工具返回 `USER_STOPPED_BROWSER_CONTROL`。
2. 收到停止后立刻停止浏览器操作，不得自行恢复；只有用户明确要求继续或发送新消息后才能再次操作。
3. 登录、支付、删除账号等敏感步骤要让用户确认；不要替用户输入密码或用聊天索要密码。
4. 不要用浏览器工具访问本机文件系统、内网管理页面或绕过网站权限；只完成用户明确要求的网页任务。

## 常见错误

- `BROWSER_HOST_UNAVAILABLE`：工具箱主程序或内置 WebView2 服务未就绪；请用户保持工具箱打开，或在 设置 → AI 浏览器 点「一键修复」。
- `ELEMENT_NOT_FOUND` / `ELEMENT_AMBIGUOUS`：重新 snapshot，用 ref 或更精确的 selector。
- `WAIT_TIMEOUT`：页面没有在时限内出现目标内容；先 snapshot 看当前状态，再决定是否继续等待。
- `SCREENSHOT_FAILED`：内置 WebView2 截图失败；先用 snapshot 读取页面，或让用户点「一键修复」。
- `BROWSER_DISCONNECTED`：浏览器窗口被关闭或控制连接中断；重新调用 `browser_status` 或让用户修复。

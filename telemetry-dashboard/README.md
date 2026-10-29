# 遥测看板（telemetry-dashboard）

图吧工具箱匿名遥测的查看面板：在线设备、活跃趋势、版本分布、崩溃排行、工具热度。
数据源是 Azure Application Insights（资源 `tubatool-telemetry`），通过只读 API 密钥查询，
部署在 Cloudflare Workers 上（自定义域 **https://stats.tubawinui3.cn/**），默认每 60 秒自动刷新。

## 两级鉴权

| 视图 | 访问方式 | 能看到 |
| --- | --- | --- |
| 游客 | 直接打开网址，无需口令 | 聚合口径：在线设备、今日/7 日/30 日活跃、累计设备、**报错率**、24h 报错次数、24h 在线曲线、30 天活跃曲线、版本分布、**累计下载量**（GitHub + GitCode） |
| 管理 | 点「解锁完整数据」输入口令（本机记住） | 全部游客数据 **+** 崩溃排行（异常类型、次数、影响设备、最近发生、示例消息）、工具使用热度 |

- 口令以 Worker Secret 保存（`DASHBOARD_TOKEN`），请求时经 `x-dashboard-token` 头或 `?token=` 传入；
  错误口令返回 401 且**不泄露**任何面板数据。
- 想调整「哪些面板对游客开放」：改 `src/index.js` 顶部的 `PUBLIC_PANELS` 白名单即可。
- 轮换口令：`npx wrangler secret put DASHBOARD_TOKEN`（改完立即生效，旧口令自动失效）。

## 结构

- `src/index.js` —— Worker：两级鉴权 + KQL 面板查询（30 秒 / 5 分钟缓存）+ 下载量外部面板（6 小时缓存）
- `public/index.html` —— 单页看板：**Fluent 2 设计语言**（与应用同一套色板与圆角/层次规范，支持亮/暗/跟随系统），
  零外部依赖（不引 CDN，国内秒开），图表全部为手绘 SVG/HTML
- 页面由 Worker 以文本模块直接吐出并设 `no-cache`，改版即时生效（不走会被缓存的静态资源托管）

### 图表一览

| 图表 | 口径 | 可见性 |
| --- | --- | --- |
| 在线设备（面积图） | 近 24 小时 · 30 分钟粒度 | 游客 |
| 活跃设备（柱状图） | 近 30 天 · 按天 | 游客 |
| 新增设备（柱状图） | 近 30 天 · 首次出现 | 游客 |
| 崩溃趋势（双线图） | 近 30 天 · 次数 + 影响设备 | 游客 |
| 报错率趋势（面积图） | 近 30 天 · 报错设备 / 活跃设备 | 游客 |
| 版本分布（环形图） | 近 7 天 · 按设备数 | 游客 |
| 活跃时段（热力图 7×24） | 近 7 天 · 北京时间 | 游客 |
| 渠道下载量（对比条） | 累计 · GitHub + GitCode | 游客 |
| 工具热度（横向条） | 近 7 天 · 启动次数 | 管理员 |
| 崩溃排行（表格 + 可展开堆栈） | 近 7 天 · 类型/次数/设备/示例消息/**调用栈**（点行展开，可一键复制） | 管理员 |

> **数据范围**：遥测是随 **1.6.5** 版本首次引入的，因此在线/活跃/新增设备/崩溃/报错率/版本分布/活跃时段
> 都只统计 1.6.5 及之后的版本；更早版本不产生任何数据。页面上有对应提示条。
> 「渠道下载量」是 Release 全量累计，不受版本限制。

## 部署

```bash
cd telemetry-dashboard

# 1. 写入密钥（只需一次）
npx wrangler secret put AI_API_KEY        # Azure 门户 → App Insights → API 访问 → 只读密钥
npx wrangler secret put DASHBOARD_TOKEN   # 面板访问口令（自定义）
npx wrangler secret put GITHUB_TOKEN      # 见下方说明（强烈建议，否则 GitHub 下载量常被限流）
# 或一次性写入：[{"AI_API_KEY":"...","DASHBOARD_TOKEN":"...","GITHUB_TOKEN":"..."}] 存成 json 后
# npx wrangler secret bulk secrets.json

# 2. 部署（首次会自动创建 stats.tubawinui3.cn 的解析记录）
npx wrangler deploy
```

### 关于 GITHUB_TOKEN

下载量要读 `api.github.com`。**未认证时 GitHub 按出口 IP 限 60 次/小时，而 Worker 出口是
Cloudflare 共享 IP，很容易 403**（表现为看板上 GitHub 数字空着）。处理方式：

- **推荐**：在 <https://github.com/settings/tokens> 建一个 classic 令牌（**不需要勾任何权限**，
  公开仓库读取即可，配额 5000 次/小时），用上面的命令写入 `GITHUB_TOKEN`。
- 不配也能跑：某个源失败时看板会**沿用最近一次成功值**并在注释里标明「暂时不可用」，
  两个源都失败才会整块显示取不到。
- 下载量缓存 6 小时（次数变化慢，也顺便省配额）。

需要 Cloudflare 授权：交互式终端跑 `npx wrangler login`，或设置环境变量
`CLOUDFLARE_API_TOKEN`（权限：Workers 脚本编辑 + Workers 路由/自定义域编辑 + 区域读取）。

## 本地预览

```bash
cd telemetry-dashboard
printf 'AI_API_KEY=...\nDASHBOARD_TOKEN=...\n' > .dev.vars   # 本地密钥文件，勿提交
npx wrangler dev
```

注意：本机 `wrangler dev` 走本机网络，国内对 `api.applicationinsights.io` 的 DNS 常被污染，
本地可能查不出数据（线上 Worker 在 Cloudflare 边缘查询，不受影响）。

## 数据口径

- 设备 = `user_Id`（客户端生成的匿名设备标识，机器 GUID 加盐哈希）
- "在线" = 最近 15 分钟内有 `heartbeat`（每 10 分钟一次）或 `app_start` 事件
- 异常文本在客户端上报前已脱敏（用户名/用户目录路径 → `<用户>`）
- Application Insights 有 1~3 分钟入库延迟；数据保留 30 天

## 自定义

- 面板与 KQL：`src/index.js` 顶部的 `PANELS`
- 配色与刷新间隔：`public/index.html` 的 `:root` 变量与 `REFRESH_MS`

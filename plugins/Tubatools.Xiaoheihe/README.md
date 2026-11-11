# 小黑盒插件（Tubatools.Xiaoheihe）

图吧工具箱的第三方小黑盒社区客户端，API v2 插件。界面按桌面论坛客户端设计：
左侧固定导航、顶部全局搜索/刷新/账号栏、右侧铺满内容区的卡片流；帖子详情使用独立阅读页与评论楼层。

## 功能

- **登录**
  - 原生扫码登录：本地渲染二维码，轮询扫码状态，登录态落盘；同时捕获服务端 `Set-Cookie` 并多处兜底解析 `pkey / heybox_id`。
  - 网页登录：内置 WebView2 打开小黑盒官方网页，一键读取 Cookie，同时取得帖子详情可能需要的风控令牌 `x_xhh_tokenid`。
  - 手动导入 Cookie（高级）。
  - 登录态保存在插件私有目录 `session.json`，退出登录/清除本地登录态。
- **推荐**：首页推荐流、换一批、加载更多、帖子卡片（封面、作者、等级、话题、评论/赞/投币数）。
- **社区**：社区树（为你推荐 / 近期热门 / 我的偏好 / 全部分类）、话题帖子流、热门/最新/默认排序；**切换话题只更新选中态，不重建左侧列表**。
- **搜索**：综合/帖子/游戏/用户/话题、热门搜索词、加载更多。
- **帖子详情**：正文、图片画廊、视频、点赞、收藏、投币、复制链接、浏览器打开；
  文章帖使用原生富文本渲染，保留标题/粗体/斜体/链接，图片按正文宽度铺满；图片点击打开**按原图比例自适应、可缩放的大图对话框**；视频用 `MediaPlayerElement` 内置播放；评论楼层、楼中楼展开、评论点赞/取消赞、回复、发布评论、热门/最新/最早排序。
- **表情**：支持小黑盒 `[cube_xxx]` / `[heygirl_xxx]` 表情标记内联解析，评论和发帖编辑器提供表情选择器。
- **页面切换**：普通页面淡入 + 横向滑动；从帖子卡片/游戏卡片进入详情时使用 ConnectedAnimation 共享元素过渡（标题/封面），列表到详情不再生硬跳转。
- **查看器**：图片和视频使用自绘全屏查看器（不是 ContentDialog）；覆盖整个窗口内容，图片默认按完整比例适配全屏、支持缩放/滚动/查看原图，视频为 16:9 自适应、统一使用原生 `MediaPlayerElement`（HLS 走 `AdaptiveMediaSource`），第一段游戏视频静音自动预览。查看器工具栏位于窗口标题栏下方，避免和系统最小化/最大化/关闭按钮重叠。
- **我的**：个人资料、每日签到与任务状态、消息中心（回复/赞与收藏/消息中心）、
  我的帖子、收藏、浏览历史、Steam 游戏库。签到使用 `POST /task/sign_v3/sign`（`is_cy=0`），并提供刷新状态；已修复 `post_links` / `history_visit[].content` / `messages` / `sign_list` 的真实字段解析。
- **用户主页**：资料、关注/取关、用户帖子列表。
- **游戏**：热门游戏榜、游戏搜索、我的 Steam 游戏库、游戏详情（评分、价格、发售日期、
  在线人数、开发商/发行商、简介与配置需求、截图、关注游戏）。PC/主机走 `/game/get_game_detail/`，移动端游戏走 `/game/mobile/get_game_detail/`；视频排在截图最前面，第一段静音自动播放，点击放大。简介会按“最低配置/推荐配置/游戏介绍”等标题拆分，配置项以键值卡片展示，长段落自动分段。
- **发帖**
  - 自动获取可发布社区，支持**搜索社区**、自定义 `topic_id`。
  - 块式编辑器：文字、小标题、图片；支持上移/下移/删除/更换图片。
  - 图片先上传到小黑盒图床（腾讯云 COS 四步直传）。
  - **图文帖**：文字与图片作为独立块提交；**文章帖**：按块顺序生成 HTML，图片行内混排，支持小标题。
  - 发布前有独立预览与二次确认。
- **界面**：浅色/深色主题自适应、内容区始终横向铺满、窄窗口自动折叠侧栏、统一加载/错误/成功提示。
- **风控**：接口返回 `show_captcha` 时直接弹出风控对话框，引导打开「账号 → 网页登录」获取 `x_xhh_tokenid`。

## 关键技术说明

当前网页端写接口要求 **`_rnd=16`**（AES-256-GCM，附加数据 `heybox-rnd:v16`）签名，只有
`hkey/_time/nonce` 会得到「缺失参数」。插件在 `Services/XhhSigner.cs` 的 `Rnd16` 中实现，
并由 `XhhClient` 对每个请求附带。详见 `API-TEST-REPORT.md`。

## 包信息

| 项 | 值 |
| --- | --- |
| PackageId | `Tubatools.Xiaoheihe` |
| 插件 API | v2 |
| 入口 | `Tubatools.Xiaoheihe.XiaoheihePlugin` |
| 工具 ID | `xiaoheihe`（全局 `plugin:tubatools.xiaoheihe:xiaoheihe`） |
| 分类 | 游戏工具 |
| 依赖 | `QRCoder`（MIT）、`HtmlAgilityPack`（MIT）、`System.Drawing.Common`（MIT，QRCoder 传递依赖） |

## 构建与打包

```powershell
dotnet build plugins/Tubatools.Xiaoheihe/Tubatools.Xiaoheihe.csproj -c Release
dotnet pack  plugins/Tubatools.Xiaoheihe/Tubatools.Xiaoheihe.csproj -c Release -o artifacts/
```

产物：`artifacts/Tubatools.Xiaoheihe.<Version>.nupkg`

包内布局：

```
tubatools.json
assets/icon.svg
plugin/Tubatools.Xiaoheihe.dll
plugin/Tubatools.Xiaoheihe.deps.json
plugin/QRCoder.dll
plugin/System.Drawing.Common.dll
```

修改插件版本只需递增 csproj 的 `<Version>`；保持 PackageId、工具 ID 和分类不变，
已安装用户才能覆盖升级并保留收藏/排序。

## 使用说明

1. 打开「内置工具 → 小黑盒」。
2. 进入「账号」：
   - 推荐先用「网页登录 / 获取风控令牌」：打开官方网页并登录，再点「读取登录状态」。
   - 也可以直接「扫码登录」；写操作依赖 `_rnd` 签名，插件已自动处理。
3. 首页/社区/搜索/游戏可匿名浏览；点赞、收藏、评论、关注、签到、发帖需要登录。

## API 与算法来源

- 签名算法、接口路径与参数约定参考
  [`56dz/xiaoheihe-api`](https://github.com/56dz/xiaoheihe-api)（GPL-3.0）、
  [`luckylca/xhhBackCrack`](https://github.com/luckylca/xhhBackCrack) 与网页 bundle 的当前实现。
- 帖子爬虫数据结构参考
  [`tlkppm/xiaoheihe-crawler-api`](https://github.com/tlkppm/xiaoheihe-crawler-api)（MIT）。
- 本项目未内置任何账号、Cookie 或密钥；登录态只保存在本机插件私有目录。

## 已知限制

- 私信、创作者中心、商城/工坊完整浏览、视频上传等没有内置；请使用官方网页/客户端。
- 签到使用 `POST /task/sign_v3/sign`（`is_cy=0`）并刷新状态；若账号已签到，按钮会正确显示为「已签到」。网页端任务奖励可能要求较新的客户端版本，补签/奖励请以官方 App 为准。
- 帖子详情、用户主页等接口在部分网络/账号状态下需要 `x_xhh_tokenid`；没有令牌时插件会弹风控对话框引导。
- 需要 Google 两步验证的账号请使用网页登录完成验证。
- 小黑盒接口可能随时调整；如果某个页面失效，请更新插件或反馈接口变化。
- 第三方客户端仅供个人学习与交流，请遵守小黑盒服务条款，不要高频请求或批量采集。

## 许可证

GPL-3.0-only。第三方组件：QRCoder（MIT）、System.Drawing.Common（MIT）。

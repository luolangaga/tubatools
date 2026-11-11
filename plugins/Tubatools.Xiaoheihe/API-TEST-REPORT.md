# 小黑盒插件 API 验证报告

- 日期：2026-11-11
- 插件版本：`Tubatools.Xiaoheihe` **1.0.13**
- 测试账号：`小哈基毅`（heybox_id `87472651`）
- 测试方式：使用插件保存的真实登录态直接请求 `api.xiaoheihe.cn`；写入测试全部在本人帖子下完成并清理。

## 关键修复：网页端当前签名

旧版参考实现只计算 `hkey/_time/nonce`。当前网页端写接口已经要求新的 **`_rnd=16`** 签名：

- AES-256-GCM
- 密钥：网页 bundle 内置常量
- 附加数据：`heybox-rnd:v16`
- 明文：`[时间戳, nonce, 规范化路径]`
- 输出：`16:` + base64url(IV + ciphertext + tag)

没有 `_rnd` 时，评论/发帖返回笼统的「缺失参数」；补上后接口正常。插件已在 `XhhSigner.Rnd16` + `XhhClient` 中对所有请求附带该签名。

## 只读接口

已通过：

- 首页推荐、轮播、社区分类、社区帖子/头条
- 搜索首屏/发现/联想；综合/帖子/游戏/用户/话题搜索
- Wiki / 商城搜索
- 帖子详情 `link/tree`、相关推荐
- 用户主页、用户帖子、用户动态、粉丝/关注列表
- 粉丝/关注/收藏/历史/消息/签到/游戏库等用户态接口
- 游戏详情、游戏简介、热门游戏、游戏评价、赛事列表
- 发帖编辑器社区列表（4 条推荐）+ 社区搜索（40 条）
- 签到日历 + `task/list_v2`（签到状态：已完成，连签 104 天）

关键真实返回：

| 接口 | 结果 |
| --- | --- |
| `/bbs/app/profile/user/link/list_v2` | `post_links`，20 条 |
| `/bbs/app/profile/history/visit` | `history_visit[].content`，26 条 |
| `/bbs/app/user/message?list_type=0/1/2` | `messages`，20 条/页 |
| `/bbs/app/profile/fav/folder/v2/links` | 28 条 |
| `/game/owned_game/game_list` | 11 条 |
| `/task/sign_list/` + `/task/list_v2/` | 已签到，连签 104 天 |

## 写入接口

| 操作 | 结果 |
| --- | --- |
| 图片上传（info → token → COS PUT → callback） | 成功，320×200，返回 `imgheybox` URL |
| 评论创建 | 成功（测试评论已删除） |
| 评论删除 | 成功，复查无残留 |
| 评论点赞 `support_type=1` | `up +1`，`is_support=1` |
| 取消评论赞 `support_type=0` | `up 0`，`is_support=0` |
| 帖子点赞 / 取消 | 成功 |
| 图文帖发布（文字块 + 图片块） | 成功，随后删除并复查「该内容已被删除」 |
| 文章帖发布（HTML + 行内图 + 图片引用块） | 成功，随后删除并复查「该内容已被删除」 |
| 关注/取关用户 | 成功并已取关 |
| 关注/取关游戏 | 成功并已取关 |
| 二维码登录 | 取得 pkey / heybox_id；插件会捕获 Set-Cookie 并多处兜底解析 |

发帖频率限制：短时间内连续发帖会返回「发帖频率过快」；等待约 90 秒后文章帖发布成功。这是服务端限制，插件会把原文提示出来。

## 视频与游戏媒体

- 帖子详情视频直链已验证：HTTP `206`，`Content-Type: video/mp4`，支持 Range。
- 插件使用 `MediaPlayerElement` 播放；HLS `.m3u8` 走 `AdaptiveMediaSource`（Steam 预告片已验证 HTTP 200 / `application/vnd.apple.mpegurl`）。
- 游戏详情 `screenshots` 是对象数组：`movie` 的 `url` 是 m3u8、`thumbnail` 才是封面。旧实现把 m3u8 当图片，导致截图区四个灰块；已改为封面海报 + 播放按钮，图片项使用 jpg 版本。
- 游戏截图缩略图 jpg 已验证 HTTP 200 / `image/jpeg`。
- 移动端游戏走独立接口：`GET /game/mobile/get_game_detail/?appid=<id>`。已用「第五人格」（appid `99900359`, `game_type=mobile`）验证返回 `name=第五人格`、简介、`menu_v2`、`game_data`、截图与 m3u8 预告片；PC/主机仍走 `/game/get_game_detail/`。
- 游戏详情修复了简介请求后重建 `XhhGameDetail` 时丢掉 `Media` 的问题；视频现在排在截图最前面，第一段视频静音自动循环预览，点击后打开带声音和进度条的大窗。
- 表情包：接入小黑盒 Cube/盒娘表情映射（`imgheybox.max-c.com/heybox/emoji/*.png`），帖子和评论中 `[cube_xxx]` 内联显示为表情图，评论/发帖编辑器提供表情选择器并发送原始标记。
- 部分视频封面 URL 会 404，属于服务端资源问题；播放和「浏览器打开」不受影响。

## 测试内容清理

- 所有测试帖子已删除并复查。
- 所有测试评论已删除并复查。
- 点赞/关注均已恢复。
- 测试上传的图片会保留在账号图床（没有删除图片的公开接口），不影响正常使用。

## 未覆盖 / 说明

- 私有窗口环境无法启动 GUI 做最终视觉验收；1.0.13 已编译通过，界面需要你在工具箱中实际查看。
- 私信、创作者中心、商城/工坊完整浏览、视频上传等未内置。
- 签到接口已从错误的 GET 改为 `POST /task/sign_v3/sign` + `is_cy=0`；当前账号今日已完成签到（连续 104 天），无法在未签到日复测实际写入，但请求方式已与参考实现和网页端一致。签到后会重新拉取状态；若服务端没有变成已签到，插件会提示改用官方 App，而不是假报成功。
- 需要 `x_xhh_tokenid` 的接口若触发 `show_captcha`，插件会直接弹出风控对话框并引导到「账号 → 网页登录」。

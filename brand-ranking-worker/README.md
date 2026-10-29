# 电脑品牌排行榜 Worker

原生 WinUI 页：内置工具 → 硬件工具 → 电脑品牌排行榜。

## 部署

```powershell
cd brand-ranking-worker
npm install
npx wrangler login
npx wrangler d1 create tuba-brand-ranking
# 把输出的 database_id 写入 wrangler.toml
npx wrangler d1 migrations apply tuba-brand-ranking --remote
npx wrangler secret put ADMIN_TOKEN
npm run deploy
```

将发布得到的 HTTPS URL 填入工具的「服务设置」。不含任何客户端共享密钥。
本地调试先运行 `npx wrangler d1 migrations apply tuba-brand-ranking --local`，再 `npm run dev`，配置 `http://localhost:8787`。

## 接口与规则

- GET /brands：品牌、平均分（1–10）、点评人数。没有评分的品牌排在最后，不伪造种子评分。
- GET /brands/:id/reviews?offset=0：按最新排序，每页 30 条。
- POST /brands/:id/reviews：JSON `{device,nickname,model,content,score}`。device 是客户端生成的随机 GUID，不采集硬件信息。每安装实例每品牌仅计一票，重复提交更新内容。
- DELETE /reviews/:id：需 `Authorization: Bearer ADMIN_TOKEN`，供运营删除违规点评。

服务端绑定 SQL 参数、校验字段长度和整数评分；每 IP 一分钟一次提交（仅保存 IP 的 SHA-256，限频并非身份认证）。匿名标识可以重置，不能宣称实名机主或防刷票；客户端没有上传设备证据，显示「玩家点评」。上线后的公开评论需要运营审核/举报流程，当前提供管理员删除接口。

所有品牌按笔记本厂商品牌聚合，联想包含 ThinkPad，华硕包含 ROG，其他品牌可在迁移中新增。综合分为公开点评简单算术平均；小样本可能波动。

使用 Cloudflare D1 Worker bindings：https://developers.cloudflare.com/d1/worker-api/

## 本地验证

先执行本地迁移，启动 `npx wrangler dev --port 8791`，另一终端执行 `npm run check` 和 `npm test`。测试会写入本地联想测试点评；重复测试前清空本地 throttle 表或等一分钟。


线上服务地址：https://rt.tubawinui3.cn（客户端默认已接入）。

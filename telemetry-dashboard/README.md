# 图吧工具箱遥测看板

软件直接通过 HTTPS 上报 Axiom，Cloudflare Worker 仅查询 Axiom 并展示看板，不接收软件上报。

- 看板：https://stats.tubawinui3.cn/
- Axiom 组织：luolan-vplq；数据集：tuba-telemetry
- 数据区域：US East 1 (AWS)；免费方案保留 30 天
- 上报地址：https://us-east-1.aws.edge.axiom.co/v1/ingest/tuba-telemetry

## 凭据与发布

客户端使用仅限 tuba-telemetry 的 Basic 写入凭据，无读取和管理权限。
该凭据随软件分发，可被提取，不应授予任何其他权限。
本地构建从主项目 Properties/Telemetry.local.props 读取 AxiomIngestToken；示例见同目录的 .example。
真实 .props 已被 Git 忽略。GitHub 发布流程从仓库 secret AXIOM_INGEST_TOKEN 注入，缺失时阻止发布。

Worker Secrets：

- AXIOM_QUERY_TOKEN：仅允许读取 tuba-telemetry 的 Query 权限，不允许写入、删除、管理。
- DASHBOARD_TOKEN：现有管理员口令，迁移时保留。
- GITHUB_TOKEN：读取公开 Release 下载量，可选。

部署：在此目录运行 wrangler deploy；验证：npm test。

## 数据口径与权限

- 在线：最近 15 分钟有 heartbeat 或 app_start 的匿名设备；软件心跳每 10 分钟一次。
- 活跃、新增、累计：当前查询/保留窗口内的设备，不能理解为永久累计。
- 异常在客户端脱敏后发送，保留类型、消息、异常链和解析后的栈帧。
- 工具热度依赖支持此功能的新版客户端：第三方启动器和内置工具统一执行入口上报 tool_launch，脚本/网页卡片也单独接入；仅上传工具目录名/ID，不上传文件路径。旧版历史没有采集工具启动，不能从心跳推算热度。
- 查询按 event_id 去重，避免重试或可恢复导入造成重复统计。
- origin=verification 的接入验证记录不参与使用事件/异常统计。
- 游客仅能看聚合面板；工具热度和异常详情仍需原管理员口令。
- 工具热度将近 7 天全部工具启动统计与官方目录合并，生成热门与低热度各 10 项，可按启动次数或使用设备数排序。全部工具支持按名称或上报 ID 搜索。目录工具没有收到启动记录时显示为 0；查询失败时显示错误。两种指标共用一份查询缓存。
- 按天趋势支持 7 / 14 / 30 天范围，活跃 / 新增支持折线与柱状切换；图表支持悬停和键盘聚焦提示，动画尊重系统减少动态效果设置。可暂停自动刷新，隐藏标签页暂停轮询。
- Worker 按面板时间窗口查询，使用进程缓存、并发合并和边缘 Cache API。
  在线/24 小时异常缓存 30 秒，其余遥测面板缓存 5 分钟；下载量缓存 6 小时。
  游客的 force 参数无效；管理员可强制刷新。

## 代码

- src/axiom.js：APL 查询、列式响应转换、去重与查询窗口。
- src/index.js：两级权限、缓存、下载量查询和页面服务。
- public/index.html：现有主题看板，无外部前端依赖。
- scripts/verify-axiom.mjs：实际查询和只读/只写权限隔离验证。
- scripts/migrate-azure.mjs：Azure 历史备份、分批导入、检查点恢复和精确数量核验。

## 历史迁移

Azure 旧资源的只读密钥仅在迁移期间使用，迁移完成后不再参与看板运行。
临时迁移模块保存在 scripts/azure-export.js，正式 Worker 不挂载该入口，也不依赖 Azure。
再次导出时才临时引入模块并配置 MIGRATION_TOKEN；迁移后移除路由和该 secret。
入口只允许固定的 customEvents / exceptions 表和合法时间范围，不接受任意 KQL。
原始导出（gzip JSON）、检查点与核验报告位于仓库根目录 artifacts/telemetry-migration/，不提交到 Git。
迁移保留原始事件时间、匿名设备标识、事件属性及异常详情，Azure 资源和原始数据不会被删除。
核验按稳定事件 ID 去重，并扣除超出 Axiom 30 天窗口的数据。超期记录单独备份为 expired-before-axiom-window.json.gz，完整原始备份也会保留。

旧版本客户端内置的 Azure 地址不会远程改变；新上报地址随新版软件生效。

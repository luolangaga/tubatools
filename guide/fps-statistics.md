# 帧率与延迟统计口径

2026-11-05 对齐 [FrameScopeMonitor statistics v2](https://github.com/Misakamiro/FrameScopeMonitor/blob/main/src/core/FrameScopeFrameStatistics.cs)。来源 blob：`b242dafb3bb5b78848cef58a325c75bba2084c22`，采用仓库 GPL-3.0 许可。

- 实时 FPS、1% Low、0.1% Low 都使用最近帧时间戳之前的 **1 秒窗口**，左开右闭。停止出帧满 1 秒后不再显示旧读数。
- FPS = `1000 × 帧数 / 帧时间总和(ms)`。
- Low = 最慢 `max(1, floor(帧数 × 比例))` 帧的平均帧时间的倒数，换算为 FPS。不再等待 100/900 帧或 10/30 秒预热。
- FPS 和 Low 使用 `floor(value + 0.5)` 四舍五入。有效帧时间范围为 `(0, 604800000] ms`，保留亚毫秒帧和长卡顿。
- 同进程多个交换链按上游评分和字符串排序决胜，存在多链时优先采用所选链的 Hardware 帧；帧时间和延迟也取该链的同一类帧。
- 帧率分析报告每秒采样上述窗口，末尾不足一秒也计入；使用上游默认的高低 FPS 筛选和逐秒整数均值汇总，不能把整段所有帧直接混在一起算 Low。PresentMon 与 ETW 回退共用这套报告统计。

## 延迟定义

核对的上游 LiveFrameStats 与报告解析代码不计算单独的渲染延迟。图吧的该项使用相同采集源 PresentMon 的 `msUntilRenderComplete`（提交到渲染完成），单位 ms、显示一位小数；不使用 `msUntilDisplayed`（提交到上屏）。没有有效样本显示 `--`。

旧 ETW 回退把提交事件与 Win32k 合成事件按 FIFO 配对，不能证明是同一帧，已移除该估算。ETW 回退仍提供 FPS 和帧时间，但延迟缺失时显示 `--`。采集源不同仍可能产生不同读数，应使用相同游戏进程、交换链、录制区间与帧生成设置比较。

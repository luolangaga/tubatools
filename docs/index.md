---
layout: home

hero:
  name: 图吧工具箱
  text: PC 硬件检测与系统维护工具集
  tagline: 专为 PC 硬件爱好者和系统维护人员打造 · WinUI 3 现代界面 · 82 款专业工具一键启动
  image:
    src: /logo.png
    alt: 图吧工具箱
  actions:
    - theme: brand
      text: 快速开始
      link: /guide/getting-started
    - theme: alt
      text: 下载应用
      link: /download
---

<div class="notice-banner">
  <i class="fa-solid fa-circle-info"></i>
  <span>本项目为社区开发的 <strong>WinUI 3 重构版</strong>，采用全新现代界面、新增内置工具与硬件查询等功能，与原版图吧工具箱无隶属关系。</span>
</div>

<div class="stat-grid">
  <div class="stat-item"><div class="stat-number">82</div><div class="stat-label">收录工具</div></div>
  <div class="stat-item"><div class="stat-number">12</div><div class="stat-label">内置功能</div></div>
  <div class="stat-item"><div class="stat-number">8</div><div class="stat-label">工具分类</div></div>
  <div class="stat-item"><div class="stat-number">0</div><div class="stat-label">数据收集</div></div>
</div>

<div class="features-grid">

<div class="feature-card">
  <div class="feature-icon blue"><i class="fa-solid fa-rocket"></i></div>
  <h3>一键启动工具</h3>
  <p>自动扫描 Tools 文件夹，按分类展示，点一下就能跑。支持实时搜索、收藏夹、管理员运行、创建桌面快捷方式。</p>
</div>

<div class="feature-card">
  <div class="feature-icon green"><i class="fa-solid fa-microchip"></i></div>
  <h3>硬件信息查询</h3>
  <p>通过 WMI 实时读取 CPU、主板、内存、显卡、硬盘、显示器等全面硬件信息，后台线程查询不阻塞 UI。</p>
</div>

<div class="feature-card">
  <div class="feature-icon purple"><i class="fa-solid fa-screwdriver-wrench"></i></div>
  <h3>内置实用工具</h3>
  <p>功耗监控、证书屏蔽、端口查看、Hosts 编辑、键盘测试、垃圾清理、蓝屏分析、WiFi 密码查看、网速测试等 12 款内置工具。</p>
</div>

<div class="feature-card">
  <div class="feature-icon orange"><i class="fa-solid fa-palette"></i></div>
  <h3>WinUI 3 现代界面</h3>
  <p>原生 Windows 11 风格设计，支持亮色/暗色/跟随系统主题切换，毛玻璃 Acrylic 效果，流畅的导航体验。</p>
</div>

<div class="feature-card">
  <div class="feature-icon cyan"><i class="fa-solid fa-shield-halved"></i></div>
  <h3>完全离线运行</h3>
  <p>纯离线应用，不收集任何用户数据，所有操作均在本地完成。零 Cookie、零追踪、零第三方服务。</p>
</div>

<div class="feature-card">
  <div class="feature-icon pink"><i class="fa-solid fa-arrows-rotate"></i></div>
  <h3>自动更新</h3>
  <p>启动时静默检查更新，有新版本自动提醒，始终保持最新状态。无需手动下载安装新版本。</p>
</div>

</div>

## 核心功能展示

<ToolShowcase />

<DiskScan />

<CertBlock />

<HardwareInfo />

## 工具分类一览

<div class="category-grid">

<div class="category-card">
  <div class="category-icon"><i class="fa-solid fa-calculator"></i></div>
  <h4>处理器工具</h4><span class="badge">9 款</span>
</div>

<div class="category-card">
  <div class="category-icon"><i class="fa-solid fa-display"></i></div>
  <h4>显卡工具</h4><span class="badge">11 款</span>
</div>

<div class="category-card">
  <div class="category-icon"><i class="fa-solid fa-hard-drive"></i></div>
  <h4>硬盘工具</h4><span class="badge">20 款</span>
</div>

<div class="category-card">
  <div class="category-icon"><i class="fa-solid fa-memory"></i></div>
  <h4>内存工具</h4><span class="badge">7 款</span>
</div>

<div class="category-card">
  <div class="category-icon"><i class="fa-solid fa-magnifying-glass-chart"></i></div>
  <h4>综合检测</h4><span class="badge">5 款</span>
</div>

<div class="category-card">
  <div class="category-icon"><i class="fa-solid fa-keyboard"></i></div>
  <h4>外设工具</h4><span class="badge">7 款</span>
</div>

<div class="category-card">
  <div class="category-icon"><i class="fa-solid fa-toolbox"></i></div>
  <h4>其他工具</h4><span class="badge">19 款</span>
</div>

</div>

## 技术栈

<div class="tech-row">

<div class="tech-card">
  <div class="tech-icon"><i class="fa-brands fa-microsoft"></i></div>
  <strong>.NET 10</strong>
  <div class="tech-sub">最新框架</div>
</div>

<div class="tech-card">
  <div class="tech-icon"><i class="fa-brands fa-windows"></i></div>
  <strong>WinUI 3</strong>
  <div class="tech-sub">Windows App SDK</div>
</div>

<div class="tech-card">
  <div class="tech-icon"><i class="fa-solid fa-chart-line"></i></div>
  <strong>WMI</strong>
  <div class="tech-sub">硬件信息查询</div>
</div>

<div class="tech-card">
  <div class="tech-icon"><i class="fa-solid fa-layer-group"></i></div>
  <strong>x86 / x64 / ARM64</strong>
  <div class="tech-sub">多架构支持</div>
</div>

</div>

## 系统要求

- Windows 10 1809 (17763) 及以上
- 支持 x86 / x64 / ARM64 架构
- 无需额外安装运行时，应用自带 .NET 运行时

<PageAnimator />
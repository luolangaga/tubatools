// 全站搜索索引：文档（guide/tools/tutorials/dev）+ 站点页面
import type { Locale } from '../components/i18n/index';

// 注意：相对本文件（src/site/）的路径是 ../docs/（即 src/docs/）
const rawDocs = import.meta.glob('../docs/**/*.md', { query: '?raw', import: 'default', eager: true }) as Record<string, string>;

export const getDocRaw = (cat: string, file: string): string => rawDocs[`../docs/${cat}/${file}.md`] ?? '';

interface DocEntry {
  cat: string;
  file: string;
  title: string;
  text: string;
}

const stripFrontmatter = (raw: string): string => {
  const match = /^---\r?\n[\s\S]*?\r?\n---\r?\n?/.exec(raw);
  return match ? raw.slice(match[0].length) : raw;
};

const parseTitle = (raw: string): string => {
  const fm = /^---\r?\n([\s\S]*?)\r?\n---/.exec(raw);
  if (fm) {
    const titleMatch = /^title:\s*(.+)$/m.exec(fm[1]);
    if (titleMatch) {
      const short = titleMatch[1].trim().split('——')[0].trim();
      if (short) return short;
    }
  }
  return '';
};

const toPlainText = (md: string): string => md
  .replace(/```[\s\S]*?```/g, ' ')
  .replace(/`([^`]*)`/g, '$1')
  .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')
  .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
  .replace(/^#{1,6}\s+/gm, '')
  .replace(/[>|\-*_~:]{1,}/g, ' ')
  .replace(/\s+/g, ' ')
  .trim();

const devTitles: Record<string, string> = {
  index: '开发者文档索引',
  'app-entry': '应用入口与启动流程',
  'main-window': '主窗口架构',
  'tool-catalog': '工具目录服务',
  'hardware-info': '硬件信息服务',
  'lite-monitor': '实时监控服务',
  'builtin-tools': '内置工具系统',
  models: '数据模型',
  'services-api': '服务层 API 参考',
  pages: '页面与导航'
};

const docs: DocEntry[] = [];

for (const path in rawDocs) {
  const rel = path.replace('../docs/', '').replace(/\\/g, '/');
  const parts = rel.split('/');
  if (parts.length !== 2 || !parts[1].endsWith('.md')) continue;
  const cat = parts[0];
  const file = parts[1].slice(0, -3);
  const raw = rawDocs[path];
  const fmTitle = parseTitle(raw);
  const title = fmTitle || (cat === 'dev' ? (devTitles[file] ?? file) : file);
  const text = toPlainText(stripFrontmatter(raw));
  docs.push({ cat, file, title, text });
}

export interface SitePageEntry {
  title: string;
  subtitle: string;
  tag: string;
  url: string;
  text: string;
}

const pages: SitePageEntry[] = [
  { title: '首页', subtitle: 'PC 硬件检测利器', tag: 'home', url: '/', text: '图吧工具箱CE 图吧工具箱 硬件检测 CPU-Z GPU-Z CrystalDiskMark WinUI3 免费 开源 下载 工具集' },
  { title: '下载', subtitle: '免费 · 离线运行 · 自动识别架构', tag: 'download', url: '/download', text: '图吧工具箱CE 图吧工具箱 下载 便携版 安装包 微软商店 x64 arm64 系统要求' },
  { title: '功能展示', subtitle: '实机截图与内置工具一览', tag: 'features', url: '/features', text: '功能展示 截图 预览 内置工具 硬件信息 一键验机 烤机 压力测试 游戏监控 游戏联机 流量监控 网速测试 端口占用 垃圾清理 格式转换 时间同步 Windows 镜像 天梯图 跑分 AI 助手 社区工具 设置' },
  { title: '跑分排行', subtitle: '社区性能跑分排行榜', tag: 'ranking', url: '/ranking', text: '跑分 排行榜 排行 性能 游戏性能 办公性能 CPU GPU 硬盘 浏览器 天梯 对比 跑分排行 benchmark leaderboard' },
  { title: '核间延迟', subtitle: 'CPU 核心间通信延迟热力图', tag: 'latency', url: '/latency', text: '核间延迟 核延迟 延迟 热力图 CPU 核心 跨核心 通信延迟 查询 latency heatmap core-to-core' },
  { title: '毒蘑菇测试', subtitle: '在线 GPU 分形压力测试', tag: 'mushroom', url: '/mushroom', text: '毒蘑菇测试 GPU 压力测试 分形 显卡 稳定性 散热 轻松 中等 变态 渲染倍率 实时光线追踪 帧率 fps WebGL mushroom stress test' },
  { title: '关于', subtitle: '关于图吧工具箱CE', tag: 'about', url: '/about', text: '图吧工具箱CE 图吧工具箱 关于 主题 开源协议 GPL-3.0 社区 GitHub GitCode AtomGit 反馈' }
];

const normalize = (value: string) => value.toLowerCase();

export interface SearchHit {
  title: string;
  subtitle: string;
  cat?: string;
  file?: string;
  tag?: string;
  url: string;
  score: number;
  snippet?: string;
  noResults?: boolean;
}

const makeSnippet = (text: string, query: string): string => {
  const index = text.indexOf(query);
  if (index < 0) return '';
  const start = Math.max(0, index - 24);
  const end = Math.min(text.length, index + query.length + 48);
  return (start > 0 ? '…' : '') + text.slice(start, end).trim() + (end < text.length ? '…' : '');
};

const matches = (text: string, tokens: string[]): boolean => tokens.every((token) => text.includes(token));

export function searchAll(query: string, _locale: Locale = 'zh-CN'): SearchHit[] {
  const q = normalize(query.trim());
  if (!q) return [];
  const tokens = q.split(/\s+/).filter(Boolean);

  const hits: SearchHit[] = [];

  for (const doc of docs) {
    const title = normalize(doc.title);
    const text = normalize(doc.text);
    if (!matches(title + ' ' + text, tokens)) continue;
    let score = 0;
    if (tokens.every((token) => title.includes(token))) score += 10;
    score += tokens.reduce((sum, token) => sum + (text.split(token).length - 1), 0);
    hits.push({
      title: doc.title,
      subtitle: `${catLabel(doc.cat)} · ${doc.file}`,
      cat: doc.cat,
      file: doc.file,
      url: `/${doc.cat}/${doc.file}`,
      score,
      snippet: makeSnippet(doc.text, q)
    });
  }

  for (const page of pages) {
    const title = normalize(page.title);
    const text = normalize(page.text);
    if (!matches(title + ' ' + text, tokens)) continue;
    hits.push({ title: page.title, subtitle: page.subtitle, tag: page.tag, url: page.url, score: 5 });
  }

  return hits.sort((a, b) => b.score - a.score);
}

export function suggest(query: string, _locale: Locale = 'zh-CN'): SearchHit[] {
  return searchAll(query, _locale).slice(0, 8);
}

const catLabel = (cat: string): string => ({
  guide: '使用指南',
  tools: '工具说明',
  tutorials: '使用教程',
  dev: '开发文档'
}[cat] ?? cat);

export { rawDocs };

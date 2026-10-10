/**
 * 功能展示页数据。
 * 截图位于 public/screenshots/，元数据按 id 从 i18n 取（features.<id>.title / .desc / .tags）。
 */

export interface FeatureCategory {
  id: string;
  icon: string;
}

export const featureCategories: FeatureCategory[] = [
  { id: 'all', icon: '\uE8FD' },
  { id: 'hardware', icon: '\uE964' },
  { id: 'perf', icon: '\uE9D9' },
  { id: 'maintain', icon: '\uE90F' },
  { id: 'network', icon: '\uE968' },
  { id: 'ai', icon: '\uE99A' },
  { id: 'shell', icon: '\uE790' }
];

export interface FeatureEntry {
  id: string;
  cat: string;
  image: string;
  /** 相关文档路径（可选） */
  doc?: string;
}

export const features: FeatureEntry[] = [
  { id: 'home', cat: 'shell', image: '/screenshots/home.png' },
  { id: 'hardware', cat: 'hardware', image: '/screenshots/hardware.png', doc: '/guide/hardware' },
  { id: 'builtin-tools', cat: 'maintain', image: '/screenshots/builtin-tools.png', doc: '/guide/builtin' },
  { id: 'ai-assistant', cat: 'ai', image: '/screenshots/ai-assistant.png' },
  { id: 'game-monitor', cat: 'perf', image: '/screenshots/game-monitor.png' },
  { id: 'game-tunnel', cat: 'network', image: '/screenshots/game-tunnel.png' },
  { id: 'stress-test', cat: 'perf', image: '/screenshots/stress-test.png' },
  { id: 'gpu-stress-test', cat: 'perf', image: '/screenshots/gpu-stress-test.png' },
  { id: 'benchmark-cloud', cat: 'perf', image: '/screenshots/benchmark-cloud.png', doc: '/ranking' },
  { id: 'cpu-ranking', cat: 'hardware', image: '/screenshots/cpu-ranking.png' },
  { id: 'quick-device-check', cat: 'hardware', image: '/screenshots/quick-device-check.png' },
  { id: 'traffic-monitor', cat: 'network', image: '/screenshots/traffic-monitor.png' },
  { id: 'speed-test', cat: 'network', image: '/screenshots/speed-test.png' },
  { id: 'port-viewer', cat: 'network', image: '/screenshots/port-viewer.png' },
  { id: 'junk-cleaner', cat: 'maintain', image: '/screenshots/junk-cleaner.png' },
  { id: 'rogue-cleaner', cat: 'maintain', image: '/screenshots/rogue-cleaner.png' },
  { id: 'startup-manager', cat: 'maintain', image: '/screenshots/startup-manager.png' },
  { id: 'format-converter', cat: 'maintain', image: '/screenshots/format-converter.png' },
  { id: 'windows-image', cat: 'maintain', image: '/screenshots/windows-image.png' },
  { id: 'time-sync', cat: 'maintain', image: '/screenshots/time-sync.png' },
  { id: 'favorites', cat: 'shell', image: '/screenshots/favorites.png' },
  { id: 'community', cat: 'shell', image: '/screenshots/community.png' },
  { id: 'settings', cat: 'shell', image: '/screenshots/settings.png' }
];

/** 首页「进阶功能」精选（按顺序取 4 个） */
export const homeSpotlightIds = ['ai-assistant', 'game-monitor', 'game-tunnel', 'format-converter'];

export const featureById = (id: string): FeatureEntry | undefined => features.find((item) => item.id === id);

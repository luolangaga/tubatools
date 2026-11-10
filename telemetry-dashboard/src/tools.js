import metadata from '../../TubaWinUi3.WinUI3/Metadata/tools.json' with { type: 'json' };

// 当前界面名称和部分历史挂载名称不同，保留两者作为搜索词。
const displayNames = {
  'battery-analyzer': '电池消耗分析',
  'cert-block': '恶意软件拦截器',
  'cpu-ranking': 'CPU 天梯图',
  'dotnet-completion': '.NET 环境补全',
  'energy-star': '后台节流省电神器',
  'format-converter': '格式转换',
  'gpu-ranking': 'GPU 天梯图',
  'hardware-spoofer': '配置修改器',
  'hosts-editor': 'Hosts 编辑',
  'optimizer-duck': 'OptimizerDuck 优化鸭',
  'performance-benchmark': '性能测试',
  'port-viewer': '端口占用',
  'space-cleanup': '空间清理',
  'speed-test': '网速测试',
  'stress-test': '一键三烤',
  'unigetui': 'UniGetUI 包管理器',
  'wifi-password': 'WiFi 密码',
  'windows-image': 'Windows 镜像',
  'winget-store': '正版软件商店',
};

const key = name => name.trim().toLowerCase();
const catalog = new Map();
const aliases = new Map();

for (const entry of [...metadata.tools, { match: '句柄清理', builtin: 'handle-cleaner' }]) {
  const name = entry.match?.trim();
  if (!name) continue;
  const directory = entry.downloadUrl?.startsWith('gc:Tools/')
    ? entry.downloadUrl.split('/').at(-1)
    : undefined;
  const id = key(entry.builtin || directory || name);
  const displayName = displayNames[entry.builtin] || name;
  if (!catalog.has(id)) catalog.set(id, { name: displayName, ids: new Set() });
  for (const alias of [name, displayName, entry.builtin, directory].filter(Boolean)) {
    catalog.get(id).ids.add(alias);
    if (!aliases.has(key(alias))) aliases.set(key(alias), new Set());
    aliases.get(key(alias)).add(id);
  }
}

export function toolRankings(usage, metric) {
  const observed = new Set();
  const rows = usage.rows.map(([id, uses, devices]) => {
    const normalized = key(id);
    const candidates = aliases.get(normalized);
    const catalogId = catalog.has(normalized) ? normalized
      : candidates?.size === 1 ? candidates.values().next().value : undefined;
    const entry = catalog.get(catalogId);
    if (!entry) return [id, uses, devices, id];
    observed.add(catalogId);
    // 保留各上报 ID 的设备数。相同设备可能使用过多个别名，不能直接相加。
    return [entry.name, uses, devices, [...new Set([id, ...entry.ids])].join(' '), id];
  });
  for (const [id, entry] of catalog) {
    if (!observed.has(id)) rows.push([entry.name, 0, 0, [...entry.ids].join(' ')]);
  }
  const column = metric === 'devices' ? 2 : 1;
  const byName = (a, b) => a[0].localeCompare(b[0], 'zh-CN')
    || (a[4] || '').localeCompare(b[4] || '');
  const descending = rows.sort((a, b) => b[column] - a[column] || byName(a, b));
  const ascending = [...rows].sort((a, b) => a[column] - b[column] || byName(a, b));
  return {
    toolsAll: { rows: descending },
    tools: { rows: descending.slice(0, 10) },
    toolsLeast: { rows: ascending.slice(0, 10) },
  };
}

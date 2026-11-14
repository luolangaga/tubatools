type InvokeArgs = Record<string, unknown> | undefined;
type Pending = { resolve: (value: unknown) => void; reject: (error: Error) => void };

const pending = new Map<number, Pending>();
let sequence = 0;
let listening = false;

function bridge(): any {
  return (window as any).chrome?.webview ?? null;
}

function ensureListener() {
  if (listening) return;
  const webview = bridge();
  if (!webview) return;
  listening = true;
  webview.addEventListener("message", (event: MessageEvent) => {
    const message = event.data;
    if (!message || message.type !== "result") return;
    const item = pending.get(message.id);
    if (!item) return;
    pending.delete(message.id);
    if (message.ok) item.resolve(message.data);
    else item.reject(new Error(message.error || "GameBox command failed"));
  });
}

const ALIASES: Record<string, string> = {
  get_system_username: "host.username",
  get_system_locale: "host.locale",
  get_delta_passwords: "delta.passwords",
  cache_delta_image: "cache_delta_image",
  open_platform_window: "host.openUri",
  open_url_in_system_browser: "host.openUri",
  open_url: "host.openUri",
  minimize_to_tray: "host.noop",
  exit_app: "host.noop",
};

function normalize(cmd: string, args?: InvokeArgs): { cmd: string; args: InvokeArgs } {
  const mapped = ALIASES[cmd] ?? cmd;
  if (cmd === "open_platform_window" || cmd === "open_url_in_system_browser" || cmd === "open_url") {
    const url = (args as any)?.url;
    return { cmd: mapped, args: url ? { url } : {} };
  }
  return { cmd: mapped, args: args ?? {} };
}

const MOCK_TOOLS = [
  { id: "gamebox", name: "游戏盒", category: "游戏工具", description: "NexBox 风格游戏盒：工具、硬件与三角洲聚合", isBuiltin: true },
  { id: "delta-force", name: "三角洲专区", category: "游戏工具", description: "改枪码、主播设置、随机装备与官方入口", isBuiltin: true },
  { id: "delta-roulette", name: "三角洲随机装备", category: "游戏工具", description: "随机生成地图、武器、头盔、护甲与干员", isBuiltin: true },
  { id: "delta-streamers", name: "主播设置", category: "游戏工具", description: "收录主播的实机游戏设置与外设配置", isBuiltin: true },
  { id: "space-cleanup", name: "空间清理", category: "系统工具", description: "扫描磁盘占用、定位大文件并清理垃圾", isBuiltin: true },
  { id: "driver-store", name: "驱动管理", category: "系统工具", description: "浏览 DriverStore、清理旧驱动与备份还原", isBuiltin: true },
  { id: "network-optimize", name: "网络优化", category: "网络工具", description: "DNS、TCP 参数与网络适配器优化", isBuiltin: true },
  { id: "speed-test", name: "网速测试", category: "网络工具", description: "测试带宽、延迟与网络质量", isBuiltin: true },
  { id: "time-sync", name: "时间同步", category: "系统工具", description: "测试 NTP 服务器并校准系统时间", isBuiltin: true },
  { id: "catalog:demo", name: "CPU-Z", category: "处理器工具", description: "外部工具示例（浏览器预览）", isBuiltin: false },
];

const MOCK_HARDWARE = [
  {
    title: "概览",
    items: [
      { label: "处理器", value: "Intel Core Ultra 7 258V" },
      { label: "显卡", value: "Intel Arc 140V GPU" },
      { label: "内存", value: "32 GB LPDDR5X 8533 MT/s" },
    ],
  },
  {
    title: "处理器",
    items: [
      { label: "型号", value: "Intel Core Ultra 7 258V" },
      { label: "核心", value: "8 核 8 线程" },
      { label: "频率", value: "2.20 GHz（睿频 4.80 GHz）" },
    ],
  },
  {
    title: "显卡",
    items: [
      { label: "型号", value: "Intel Arc 140V GPU" },
      { label: "显存", value: "共享 16 GB" },
      { label: "驱动", value: "32.0.101.6449" },
    ],
  },
];

function mockInvoke(cmd: string, args?: InvokeArgs): unknown {
  switch (cmd) {
    case "host.info":
      return { version: "dev", language: "zh", architecture: "X64", isPackaged: false, theme: "dark", mode: "skin" };
    case "host.locale":
      return "zh";
    case "host.username":
      return "dev";
    case "tools.list":
      return MOCK_TOOLS;
    case "tools.icon":
      return null;
    case "tools.run":
      return null;
    case "host.navigate":
      return null;
    case "host.openUri":
      return null;
    case "host.copyText":
      return null;
    case "host.pickFolder":
    case "host.pickFile":
      return null;
    case "diag.hardware":
      return MOCK_HARDWARE;
    case "diag.sensors":
      return {
        cpuLoad: 18,
        cpuTemp: 52,
        cpuClock: 3200,
        cpuPower: 28,
        cpuName: "Intel Core Ultra 7 258V",
        gpuLoad: 34,
        gpuTemp: 58,
        gpuClock: 1800,
        gpuPower: 22,
        gpuVramLoad: 41,
        gpuVramUsedGB: 6.2,
        gpuName: "Intel Arc 140V GPU",
        memLoad: 47,
        memUsedGB: 15.1,
        memTotalGB: 32,
        diskReadMBs: 12.4,
        diskWriteMBs: 3.8,
        diskTemp: 41,
        netUpMBs: 0.02,
        netDownMBs: 0.18,
        fps: 144,
        fpsProcess: "delta_force.exe",
      };
    case "diag.disks":
      return null;
    case "skin.get":
      return { enabled: false };
    case "skin.setEnabled":
      return null;
    case "http.getJson":
      return null;
    case "delta.passwords":
      return [];
    case "cache_delta_image":
      return "";
    case "host.noop":
      return null;
    default:
      return null;
  }
}

export function invoke<T = unknown>(cmd: string, args?: InvokeArgs): Promise<T> {
  ensureListener();
  const webview = bridge();
  const normalized = normalize(cmd, args);
  if (!webview) {
    return new Promise<T>((resolve) => {
      window.setTimeout(() => resolve(mockInvoke(normalized.cmd, normalized.args) as T), 30);
    });
  }
  const id = ++sequence;
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve: resolve as (value: unknown) => void, reject });
    webview.postMessage({ type: "invoke", id, cmd: normalized.cmd, args: normalized.args });
    window.setTimeout(() => {
      if (pending.delete(id)) reject(new Error(`GameBox command timeout: ${normalized.cmd}`));
    }, 30000);
  });
}

export function convertFileSrc(path: string): string {
  return path;
}

export function isTauri(): boolean {
  return true;
}

export const isWindows = true;

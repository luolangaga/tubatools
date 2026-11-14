/**
 * 主播档案数据层
 *
 * 数据来源：gjts.cn「冠军导航」/data/streamer-library.json
 * 由 主播/_build_assets.py 打包到 public/streamer-data/ 下（含头像与外设图，离线可用）。
 *
 * 前端只做只读展示：把 JSON 里的 "/xxx" 资源路径统一加 /streamer-data 前缀即可。
 */

// ── 类型 ──

/** 顶层分区。graphics 在 UI 上并入「游戏设置 → 视频」，不单独出现在导航里。 */
export type StreamerTab =
  | "game"
  | "graphics"
  | "nvidia"
  | "nvidia-app"
  | "steam"
  | "gear"
  | "monitor"
  | "gun-codes"
  | "audio-eq";

/** 「游戏设置」下的二级标签 */
export type GameSubTab = "video" | "sensitivity" | "combat" | "interface" | "audio" | "other";

export interface SettingField {
  id: string;
  label: string;
  /** 主值。可能为空 —— 此时看 secondary（例如未绑定的按键） */
  value?: string;
  note?: string;
  /** 副值：绝大多数是按键的「未绑定」，偶尔是数值（如 DLSS 超分 77） */
  secondary?: string;
  /** 交互方式：按住 / 切换 / 展开 / 默认 / 自定义 / — */
  behavior?: string;
  /** 来源截图编号（1~6），可为空 */
  sources?: number[];
  /** 改枪码示例条目 */
  example?: boolean;
}

export interface SettingGroup {
  id: string;
  title: string;
  tab: StreamerTab;
  fields: SettingField[];
}

export interface Equipment {
  id: string;
  /** mouse / keyboard / headset / mousepad / monitor ... */
  category: string;
  brand: string;
  model: string;
  imageUrl?: string;
  officialUrl?: string;
  dpi?: string;
  pollingRate?: string;
}

export interface StreamerProfile {
  version?: number;
  name: string;
  game: string;
  platform: string;
  tags?: string[];
  homepage?: string;
  liveUrl?: string;
  updated?: string;
  equipment?: Equipment[];
  groups?: SettingGroup[];
}

export interface StreamerEntry {
  id: string;
  streamerId: string;
  name: string;
  game: string;
  title?: string;
  avatar?: string;
  platform: string;
  tags?: string[];
  order?: number;
  revision?: number;
  updated?: string;
  profile?: StreamerProfile;
}

interface LibraryFile {
  entries: StreamerEntry[];
}

// ── 资源路径 ──

const DATA_ROOT = "/streamer-data";

/** 把档案里的资源路径转成可访问 URL；data: / http(s): 原样返回。 */
export function streamerAsset(path?: string): string {
  if (!path) return "";
  if (path.startsWith("data:") || path.startsWith("http")) return path;
  return DATA_ROOT + (path.startsWith("/") ? path : "/" + path);
}

// ── 加载 ──

let libraryPromise: Promise<StreamerEntry[]> | null = null;

/** 加载全部主播档案（进程内缓存，重复调用不会重复请求）。 */
export function loadStreamerLibrary(): Promise<StreamerEntry[]> {
  if (!libraryPromise) {
    libraryPromise = fetch(`${DATA_ROOT}/streamer-library.json`)
      .then((r) => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        return r.json() as Promise<LibraryFile>;
      })
      .then((data) =>
        (data.entries ?? [])
          .filter((e) => e && !(e as unknown as { deleted?: boolean }).deleted)
          .sort((a, b) => (a.order ?? 0) - (b.order ?? 0) || a.id.localeCompare(b.id))
      )
      .catch((err) => {
        libraryPromise = null; // 允许重试
        throw err;
      });
  }
  return libraryPromise;
}

// ── 字段展示辅助 ──

/** 值是否已填写（主值或副值任一非空） */
export function hasValue(f: SettingField): boolean {
  return !!((f.value ?? "").trim() || (f.secondary ?? "").trim());
}

/** 实际展示的值 */
export function displayValue(f: SettingField): string {
  const v = (f.value ?? "").trim();
  if (v) return v;
  return (f.secondary ?? "").trim();
}

/** 只有副值（典型：未绑定的按键） */
export function isSecondaryOnly(f: SettingField): boolean {
  return !(f.value ?? "").trim() && !!(f.secondary ?? "").trim();
}

const SWITCH_VALUES = new Set(["开", "关", "开启", "关闭", "启用", "禁用", "未勾选", "勾选", "已勾选"]);
const NUMERIC_RE = /^-?\d+(?:\.\d+)?\s*(?:%|°|ms|Hz)?$/;
/** 这两项是硬件信息，不是可选项 —— 展示为纯文本框（对齐原站样式） */
const READONLY_IDS = new Set(["display-monitor", "display-adapter"]);

export type FieldKind = "switch" | "slider" | "text";

/** 猜控件形态：开关 / 滑杆 / 文本（文本再按需加下拉箭头） */
export function fieldKind(f: SettingField): FieldKind {
  const v = displayValue(f);
  if (!v) return "text";
  if (SWITCH_VALUES.has(v)) return "switch";
  if (NUMERIC_RE.test(v)) return "slider";
  return "text";
}

/** 是否显示下拉箭头（可选项），硬件信息与改枪码不显示 */
export function showChevron(f: SettingField, group?: SettingGroup): boolean {
  if (f.example) return false;
  if (READONLY_IDS.has(f.id)) return false;
  if (group && (group.id === "equipment" || group.tab === "gun-codes")) return false;
  return fieldKind(f) === "text";
}

/** 滑杆百分比：把数值映射到 0~100 用于画填充条 */
export function sliderPercent(f: SettingField): number {
  const raw = parseFloat(displayValue(f));
  if (!Number.isFinite(raw)) return 0;
  // 带百分号的直接取值；其余按常见量程（0~200）估算
  if (displayValue(f).includes("%")) return Math.max(0, Math.min(100, raw));
  if (raw <= 1) return Math.max(0, Math.min(100, raw * 100));
  return Math.max(0, Math.min(100, (raw / 200) * 100));
}

// ── 分区 / 二级标签归类 ──

/** 不展示的分组：Fxsound 板块已按需求整体移除 */
function groupVisible(g: SettingGroup): boolean {
  return g.id !== "fxsound";
}

/** 顶层导航顺序与图标 key（图标在页面里映射） */
export const TAB_ORDER: StreamerTab[] = [
  "game",
  "nvidia",
  "nvidia-app",
  "monitor",
  "gun-codes",
  "audio-eq",
  "steam",
  "gear",
];

/** 「游戏设置」二级标签顺序 */
export const GAME_SUB_ORDER: GameSubTab[] = [
  "video",
  "sensitivity",
  "combat",
  "interface",
  "audio",
  "other",
];

/** 三角洲行动：分组 id → 二级标签（原站固定分法） */
const DELTA_SUB_MAP: Record<string, GameSubTab> = {
  sensitivity: "sensitivity",
  mouse: "sensitivity",
  movement: "combat",
  combat: "combat",
  interaction: "interface",
  spectator: "interface",
  "audio-global": "audio",
  "audio-volume": "audio",
  "audio-voice": "audio",
};

/** 无畏契约：分组 id → 二级标签 */
const VAL_SUB_MAP: Record<string, GameSubTab> = {
  "val-display": "video",
  "val-quality": "video",
  "val-mouse": "sensitivity",
  "val-control-options": "sensitivity",
  "val-movement": "combat",
  "val-equipment": "combat",
  "val-abilities": "combat",
  "val-user-interface": "interface",
  "val-communication": "interface",
  "val-map": "interface",
  "val-crosshair-primary": "interface",
  "val-crosshair-inner": "interface",
  "val-crosshair-outer": "interface",
  "val-crosshair-sniper": "interface",
  "val-audio": "audio",
  "val-voice": "audio",
};

/** 反恐精英 2：分组 id → 二级标签 */
const CS2_SUB_MAP: Record<string, GameSubTab> = {
  "cs2-video": "video",
  "cs2-advanced-video": "video",
  "cs2-frame-pacing": "video",
  "cs2-preset": "video",
  "cs2-mouse": "sensitivity",
  "cs2-radar": "interface",
  "cs2-viewmodel": "interface",
};

const KEYWORD_RULES: Array<[RegExp, GameSubTab]> = [
  [/音频|声音|音量|语音|音效|EQ/i, "audio"],
  [/鼠标|灵敏度|操作方式|键盘/i, "sensitivity"],
  [/画面|画质|视频|帧|分辨率|显示|图形|超分|DLSS|锐化/i, "video"],
  [/战斗|移动|技能|装备|操控|射击/i, "combat"],
  [/界面|交互|雷达|准星|地图|隐私|网络|交流|用户/i, "interface"],
];

/**
 * 把「游戏设置」下的分组归到二级标签。
 * graphics 分区的分组一律归 video（对齐原站：视频页展示画质/帧率）。
 */
export function gameSubTabOf(group: SettingGroup, game?: string): GameSubTab {
  if (group.tab === "graphics") return "video";

  const g = game ?? "";
  const map = g.includes("三角洲")
    ? DELTA_SUB_MAP
    : g.includes("无畏契约")
      ? VAL_SUB_MAP
      : g.includes("反恐精英")
        ? CS2_SUB_MAP
        : null;

  if (map && map[group.id]) return map[group.id];

  const hay = `${group.id} ${group.title}`;
  for (const [re, sub] of KEYWORD_RULES) {
    if (re.test(hay)) return sub;
  }
  return "other";
}

/** 某位主播实际拥有的顶层分区（去掉空分区，game 永远在前） */
export function tabsOf(entry: StreamerEntry): StreamerTab[] {
  const groups = entry.profile?.groups ?? [];
  const present = new Set<StreamerTab>();
  for (const g of groups) {
    if (!groupVisible(g)) continue;
    if (!g.fields?.some(hasValue)) continue;
    // graphics 并入 game
    present.add(g.tab === "graphics" ? "game" : g.tab);
  }
  return TAB_ORDER.filter((t) => present.has(t));
}

/** 取某个顶层分区下的分组；game 会把 graphics 一起带上 */
export function groupsOf(entry: StreamerEntry, tab: StreamerTab): SettingGroup[] {
  const groups = entry.profile?.groups ?? [];
  return groups.filter((g) => {
    if (!groupVisible(g)) return false;
    if (!g.fields?.some(hasValue)) return false;
    if (tab === "game") return g.tab === "game" || g.tab === "graphics";
    return g.tab === tab;
  });
}

/** 「游戏设置」下实际存在的二级标签（按固定顺序） */
export function gameSubTabsOf(entry: StreamerEntry): GameSubTab[] {
  const subs = new Set<GameSubTab>();
  for (const g of groupsOf(entry, "game")) {
    subs.add(gameSubTabOf(g, entry.game));
  }
  return GAME_SUB_ORDER.filter((s) => subs.has(s));
}

/** 已填写的字段总数（主值或副值任一非空；不计被移除的分组） */
export function filledFieldCount(entry: StreamerEntry): number {
  let n = 0;
  for (const g of entry.profile?.groups ?? []) {
    if (!groupVisible(g)) continue;
    for (const f of g.fields ?? []) if (hasValue(f)) n++;
  }
  return n;
}

/** 总字段数（不计被移除的分组） */
export function totalFieldCount(entry: StreamerEntry): number {
  let n = 0;
  for (const g of entry.profile?.groups ?? []) {
    if (!groupVisible(g)) continue;
    n += g.fields?.length ?? 0;
  }
  return n;
}

/** 外设分类中文名 */
export const EQUIPMENT_CATEGORY_LABEL: Record<string, string> = {
  mouse: "鼠标",
  keyboard: "键盘",
  headset: "耳机",
  mousepad: "鼠标垫",
  monitor: "显示器",
  chair: "座椅",
  controller: "手柄",
  microphone: "麦克风",
  other: "其他",
};

import { invoke } from "@tauri-apps/api/core";

export interface ToolInfo {
  id: string;
  name: string;
  category: string;
  description: string;
  isBuiltin: boolean;
}

export interface HostInfo {
  version: string;
  language: string;
  architecture: string;
  isPackaged: boolean;
  theme: string;
  mode: "skin" | "embedded";
}

export interface SensorSample {
  capturedAt?: string;
  cpuLoad?: number;
  cpuTemp?: number;
  cpuClock?: number;
  cpuPower?: number;
  cpuName?: string;
  gpuLoad?: number;
  gpuTemp?: number;
  gpuClock?: number;
  gpuPower?: number;
  gpuVramLoad?: number;
  gpuVramUsedGB?: number;
  gpuName?: string;
  memLoad?: number;
  memUsedGB?: number;
  memTotalGB?: number;
  diskReadMBs?: number;
  diskWriteMBs?: number;
  diskTemp?: number;
  netUpMBs?: number;
  netDownMBs?: number;
  fps?: number;
}

export interface HardwareItem {
  label: string;
  value: string;
  brandKey?: string;
  isVerified?: boolean;
}

export interface HardwareSection {
  title: string;
  glyph?: string;
  items: HardwareItem[];
}

export interface DisplayInfo {
  index: number;
  name: string;
  device: string;
  width: number;
  height: number;
  primary: boolean;
}

export interface DisplayFilterValues {
  enabled: boolean;
  temperature: number;
  brightness: number;
  contrast: number;
  saturation: number;
  rGamma: number;
  gGamma: number;
  bGamma: number;
  shadow: number;
  mode: number;
}

export interface DisplayFilterSettings extends DisplayFilterValues {
  displayIndex: number;
  device: string;
}

export interface DisplayFilterPreset {
  id: string;
  name: string;
  group: string;
  temperature: number;
  brightness: number;
  contrast: number;
  saturation: number;
  mode: number;
}

export interface DisplayFilterResult {
  ok: boolean;
  error?: string | null;
  settings?: DisplayFilterSettings;
}

export const API_BASE = "https://df.nexbox.top";

export function getMode(): "skin" | "embedded" {
  const params = new URLSearchParams(window.location.search);
  return params.get("mode") === "skin" ? "skin" : "embedded";
}

export const gamebox = {
  info: () => invoke<HostInfo>("host.info"),
  listTools: () => invoke<ToolInfo[]>("tools.list"),
  runTool: (id: string) => invoke<void>("tools.run", { id }),
  icon: (id: string) => invoke<string | null>("tools.icon", { id }),
  navigate: (destination: string) => invoke<void>("host.navigate", { destination }),
  openUri: (url: string) => invoke<void>("host.openUri", { url }),
  copyText: (text: string) => invoke<void>("host.copyText", { text }),
  pickFolder: (title = "选择文件夹") => invoke<string | null>("host.pickFolder", { title }),
  pickFile: (title = "选择文件") => invoke<string | null>("host.pickFile", { title }),
  hardware: () => invoke<HardwareSection[]>("diag.hardware"),
  sensors: () => invoke<SensorSample>("diag.sensors"),
  disks: () => invoke<unknown>("diag.disks"),
  skin: () => invoke<{ enabled: boolean }>("skin.get"),
  setSkin: (enabled: boolean) => invoke<void>("skin.setEnabled", { enabled }),
  getJson: (url: string) => invoke<string | null>("http.getJson", { url }),
  passwords: () => invoke<Array<{ name: string; password: string }>>("delta.passwords"),
  displays: () => invoke<DisplayInfo[]>("filter.displays"),
  filterGet: (displayIndex: number) =>
    invoke<DisplayFilterSettings>("filter.get", { displayIndex }),
  filterSet: (displayIndex: number, values: DisplayFilterValues) =>
    invoke<DisplayFilterResult>("filter.set", { displayIndex, ...values }),
  filterPresets: () => invoke<DisplayFilterPreset[]>("filter.presets"),
  filterApplyPreset: (displayIndex: number, id: string) =>
    invoke<DisplayFilterResult>("filter.applyPreset", { displayIndex, id }),
  filterReset: (displayIndex: number) =>
    invoke<DisplayFilterResult>("filter.reset", { displayIndex }),
  openColorManagement: () => invoke<DisplayFilterResult>("filter.openColorManagement"),
};

export async function apiGet<T>(path: string): Promise<T> {
  const url = `${API_BASE}${path}`;
  try {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    return (await response.json()) as T;
  } catch (primaryError) {
    const raw = await gamebox.getJson(url);
    if (!raw) throw primaryError;
    return JSON.parse(raw) as T;
  }
}

export async function apiPost<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return (await response.json()) as T;
}

export function formatNumber(value: number | undefined | null, digits = 0): string {
  if (value === undefined || value === null || Number.isNaN(value) || value < 0) return "--";
  return value.toFixed(digits);
}

export function formatGb(value: number | undefined | null): string {
  if (value === undefined || value === null || value < 0) return "--";
  return `${value.toFixed(1)} GB`;
}

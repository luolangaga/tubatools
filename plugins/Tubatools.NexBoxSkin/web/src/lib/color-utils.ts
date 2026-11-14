export function hexToRgba(hex: string, alpha: number): string {
  const cleanHex = hex.replace("#", "");
  const r = parseInt(cleanHex.slice(0, 2), 16);
  const g = parseInt(cleanHex.slice(2, 4), 16);
  const b = parseInt(cleanHex.slice(4, 6), 16);
  return `rgba(${r},${g},${b},${alpha})`;
}

export function getContrastColor(hex: string): string {
  const cleanHex = hex.replace("#", "");
  const r = parseInt(cleanHex.slice(0, 2), 16);
  const g = parseInt(cleanHex.slice(2, 4), 16);
  const b = parseInt(cleanHex.slice(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? "#1a1a1a" : "#ffffff";
}

export function isValidHexColor(hex: string): boolean {
  return /^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$/.test(hex);
}

export function normalizeHexColor(hex: string): string {
  if (hex.startsWith("#") && hex.length === 4) {
    const r = hex[1];
    const g = hex[2];
    const b = hex[3];
    return `#${r}${r}${g}${g}${b}${b}`;
  }
  return hex;
}

function parseColorToRgb(color: string): { r: number; g: number; b: number } | null {
  const rgbaMatch = color.match(/^rgba?\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/i);
  if (rgbaMatch) {
    return {
      r: parseInt(rgbaMatch[1], 10) / 255,
      g: parseInt(rgbaMatch[2], 10) / 255,
      b: parseInt(rgbaMatch[3], 10) / 255,
    };
  }
  const hex = color.replace("#", "");
  if (/^[A-Fa-f0-9]{6}$/.test(hex)) {
    return {
      r: parseInt(hex.slice(0, 2), 16) / 255,
      g: parseInt(hex.slice(2, 4), 16) / 255,
      b: parseInt(hex.slice(4, 6), 16) / 255,
    };
  }
  return null;
}

export function hexToHsv(hex: string): { h: number; s: number; v: number } {
  const rgb = parseColorToRgb(hex);
  if (!rgb) {
    return { h: 0, s: 0, v: 0 };
  }
  const { r, g, b } = rgb;
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const d = max - min;
  let h = 0;
  if (d !== 0) {
    switch (max) {
      case r: h = ((g - b) / d + (g < b ? 6 : 0)) / 6; break;
      case g: h = ((b - r) / d + 2) / 6; break;
      case b: h = ((r - g) / d + 4) / 6; break;
    }
  }
  return { h: h * 360, s: max === 0 ? 0 : (d / max) * 100, v: max * 100 };
}

export function hsvToHex(h: number, s: number, v: number): string {
  s /= 100;
  v /= 100;
  const i = Math.floor(h / 60);
  const f = h / 60 - i;
  const p = v * (1 - s);
  const q = v * (1 - s * f);
  const t = v * (1 - s * (1 - f));
  let r = 0, g = 0, b = 0;
  switch (i % 6) {
    case 0: r = v; g = t; b = p; break;
    case 1: r = q; g = v; b = p; break;
    case 2: r = p; g = v; b = t; break;
    case 3: r = p; g = q; b = v; break;
    case 4: r = t; g = p; b = v; break;
    case 5: r = v; g = p; b = q; break;
  }
  const toHex = (n: number) => Math.round(n * 255).toString(16).padStart(2, "0");
  return `#${toHex(r)}${toHex(g)}${toHex(b)}`.toUpperCase();
}

export function colorToHex(color: string): string {
  if (/^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$/.test(color)) {
    return normalizeHexColor(color);
  }
  const rgbaMatch = color.match(/^rgba?\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/i);
  if (rgbaMatch) {
    const toHex = (n: string) => parseInt(n, 10).toString(16).padStart(2, "0");
    return `#${toHex(rgbaMatch[1])}${toHex(rgbaMatch[2])}${toHex(rgbaMatch[3])}`.toUpperCase();
  }
  return "#FFFFFF";
}

export function hexToRgb01(hex: string): [number, number, number] {
  const clean = hex.replace("#", "");
  return [
    parseInt(clean.slice(0, 2), 16) / 255,
    parseInt(clean.slice(2, 4), 16) / 255,
    parseInt(clean.slice(4, 6), 16) / 255,
  ];
}

/** 从主色派生三个霓虹通道色：主色本体 / 更亮的高光 / 色相偏移 +45° 的点缀色 */
export function deriveSplashColors(primaryColor: string): { c1: [number, number, number]; c2: [number, number, number]; c3: [number, number, number] } {
  const fallback = "#98DDD0";
  const base = /^#([0-9a-fA-F]{6})$/.test(primaryColor) ? primaryColor : fallback;
  const { h, s, v } = hexToHsv(base);
  const c1 = base;
  const c2 = hsvToHex(h, Math.max(22, s * 0.62), Math.min(100, v + 30));
  const c3 = hsvToHex((h + 45) % 360, Math.min(100, s + 22), Math.min(100, v + 10));
  return {
    c1: hexToRgb01(c1),
    c2: hexToRgb01(c2),
    c3: hexToRgb01(c3),
  };
}

export const PRESET_COLORS = [
  { name: "cyan", value: "#98DDD0", labelKey: "settings.appearanceSettings.presetColors.cyan" },
  { name: "blue", value: "#60A5FA", labelKey: "settings.appearanceSettings.presetColors.blue" },
  { name: "purple", value: "#A78BFA", labelKey: "settings.appearanceSettings.presetColors.purple" },
  { name: "pink", value: "#F472B6", labelKey: "settings.appearanceSettings.presetColors.pink" },
  { name: "orange", value: "#FB923C", labelKey: "settings.appearanceSettings.presetColors.orange" },
  { name: "green", value: "#4ADE80", labelKey: "settings.appearanceSettings.presetColors.green" },
  { name: "red", value: "#F87171", labelKey: "settings.appearanceSettings.presetColors.red" },
  { name: "yellow", value: "#FBBF24", labelKey: "settings.appearanceSettings.presetColors.yellow" },
];

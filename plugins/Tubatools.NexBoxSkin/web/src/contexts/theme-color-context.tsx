"use client";

import { createContext, useContext, useState, ReactNode, useEffect, useCallback, useMemo } from "react";
import { store } from "@/lib/store";
import { hexToRgba, getContrastColor, isValidHexColor, normalizeHexColor } from "@/lib/color-utils";

export interface ThemeColorConfig {
  primaryColor: string;
  hoverOpacity: number;
  activeOpacity: number;
  borderOpacity: number;
}

export const DEFAULT_THEME_COLOR_CONFIG: ThemeColorConfig = {
  primaryColor: "#98DDD0",
  hoverOpacity: 0.2,
  activeOpacity: 0.3,
  borderOpacity: 0.4,
};

// localStorage 键名（与 store 保持一致，用于启动时同步快速读取，避免闪默认色）
const LS_PRIMARY_COLOR = "theme-primary-color";
const LS_HOVER_OPACITY = "theme-hover-opacity";
const LS_ACTIVE_OPACITY = "theme-active-opacity";
const LS_BORDER_OPACITY = "theme-border-opacity";

/** 从 localStorage 同步读取主题色，启动首帧即可拿到自定义值 */
function loadConfigFromLocalStorage(): ThemeColorConfig {
  const cfg: ThemeColorConfig = { ...DEFAULT_THEME_COLOR_CONFIG };
  try {
    const primaryColor = localStorage.getItem(LS_PRIMARY_COLOR);
    if (primaryColor && isValidHexColor(primaryColor)) {
      cfg.primaryColor = normalizeHexColor(primaryColor);
    }
    const applyOpacity = (key: string, target: () => number, set: (v: number) => void) => {
      const raw = localStorage.getItem(key);
      if (raw !== null) {
        const n = Number(raw);
        if (!Number.isNaN(n)) set(Math.max(0, Math.min(1, n)));
      }
    };
    applyOpacity(LS_HOVER_OPACITY, () => cfg.hoverOpacity, (v) => (cfg.hoverOpacity = v));
    applyOpacity(LS_ACTIVE_OPACITY, () => cfg.activeOpacity, (v) => (cfg.activeOpacity = v));
    applyOpacity(LS_BORDER_OPACITY, () => cfg.borderOpacity, (v) => (cfg.borderOpacity = v));
  } catch {
    // localStorage 不可用时忽略，使用默认值
  }
  return cfg;
}

interface ThemeColorContextType {
  config: ThemeColorConfig;
  setPrimaryColor: (color: string) => void;
  setHoverOpacity: (opacity: number) => void;
  setActiveOpacity: (opacity: number) => void;
  setBorderOpacity: (opacity: number) => void;
  resetToDefault: () => void;
  getHoverColor: (isDark?: boolean) => string;
  getActiveColor: () => string;
  getBorderColor: () => string;
  getContrastTextColor: () => string;
}

const ThemeColorContext = createContext<ThemeColorContextType>({
  config: DEFAULT_THEME_COLOR_CONFIG,
  setPrimaryColor: () => {},
  setHoverOpacity: () => {},
  setActiveOpacity: () => {},
  setBorderOpacity: () => {},
  resetToDefault: () => {},
  getHoverColor: () => "rgba(152,221,208,0.2)",
  getActiveColor: () => "#98DDD0",
  getBorderColor: () => "rgba(152,221,208,0.4)",
  getContrastTextColor: () => "#1a1a1a",
});

export function useThemeColor() {
  return useContext(ThemeColorContext);
}

export function ThemeColorProvider({ children }: { children: ReactNode }) {
  const [config, setConfig] = useState<ThemeColorConfig>(loadConfigFromLocalStorage);
  const [isLoaded, setIsLoaded] = useState(false);

  useEffect(() => {
    async function loadSettings() {
      try {
        const savedPrimaryColor = await store.get<string>("theme-primary-color");
        const savedHoverOpacity = await store.get<number>("theme-hover-opacity");
        const savedActiveOpacity = await store.get<number>("theme-active-opacity");
        const savedBorderOpacity = await store.get<number>("theme-border-opacity");

        if (savedPrimaryColor && isValidHexColor(savedPrimaryColor)) {
          setConfig(prev => ({ ...prev, primaryColor: normalizeHexColor(savedPrimaryColor) }));
        }
        if (savedHoverOpacity !== null && savedHoverOpacity !== undefined) {
          setConfig(prev => ({ ...prev, hoverOpacity: savedHoverOpacity }));
        }
        if (savedActiveOpacity !== null && savedActiveOpacity !== undefined) {
          setConfig(prev => ({ ...prev, activeOpacity: savedActiveOpacity }));
        }
        if (savedBorderOpacity !== null && savedBorderOpacity !== undefined) {
          setConfig(prev => ({ ...prev, borderOpacity: savedBorderOpacity }));
        }

        setIsLoaded(true);
      } catch (error) {
        console.error("Failed to load theme color settings:", error);
        setIsLoaded(true);
      }
    }

    loadSettings();
  }, []);

  useEffect(() => {
    if (!isLoaded) return;

    async function saveSettings() {
      try {
        // 同步写入 localStorage，供下次启动首帧快速读取
        localStorage.setItem(LS_PRIMARY_COLOR, config.primaryColor);
        localStorage.setItem(LS_HOVER_OPACITY, String(config.hoverOpacity));
        localStorage.setItem(LS_ACTIVE_OPACITY, String(config.activeOpacity));
        localStorage.setItem(LS_BORDER_OPACITY, String(config.borderOpacity));

        await store.set("theme-primary-color", config.primaryColor);
        await store.set("theme-hover-opacity", config.hoverOpacity);
        await store.set("theme-active-opacity", config.activeOpacity);
        await store.set("theme-border-opacity", config.borderOpacity);
        await store.save();
      } catch (error) {
        console.error("Failed to save theme color settings:", error);
      }
    }

    saveSettings();
  }, [config, isLoaded]);

  // 暴露主题色 CSS 变量，供全局样式（滚动条等）跟随主题色
  useEffect(() => {
    if (!isLoaded) return;
    const root = document.documentElement;
    root.style.setProperty("--theme-primary", config.primaryColor);
    root.style.setProperty(
      "--theme-primary-hover",
      hexToRgba(config.primaryColor, Math.min(1, config.hoverOpacity + 0.15))
    );
  }, [config, isLoaded]);

  const setPrimaryColor = useCallback((color: string) => {
    if (isValidHexColor(color)) {
      setConfig(prev => ({ ...prev, primaryColor: normalizeHexColor(color) }));
    }
  }, []);

  const setHoverOpacity = useCallback((opacity: number) => {
    setConfig(prev => ({ ...prev, hoverOpacity: Math.max(0, Math.min(1, opacity)) }));
  }, []);

  const setActiveOpacity = useCallback((opacity: number) => {
    setConfig(prev => ({ ...prev, activeOpacity: Math.max(0, Math.min(1, opacity)) }));
  }, []);

  const setBorderOpacity = useCallback((opacity: number) => {
    setConfig(prev => ({ ...prev, borderOpacity: Math.max(0, Math.min(1, opacity)) }));
  }, []);

  const resetToDefault = useCallback(() => {
    setConfig(DEFAULT_THEME_COLOR_CONFIG);
  }, []);

  const getHoverColor = useCallback((isDark: boolean = true) => {
    const opacity = isDark ? config.hoverOpacity + 0.1 : config.hoverOpacity;
    return hexToRgba(config.primaryColor, opacity);
  }, [config.hoverOpacity, config.primaryColor]);

  const getActiveColor = useCallback(() => {
    return config.primaryColor;
  }, [config.primaryColor]);

  const getBorderColor = useCallback(() => {
    return hexToRgba(config.primaryColor, config.borderOpacity);
  }, [config.primaryColor, config.borderOpacity]);

  const getContrastTextColor = useCallback(() => {
    return getContrastColor(config.primaryColor);
  }, [config.primaryColor]);

  // useMemo 稳定 context value，避免每次渲染创建新对象导致所有消费者重渲染
  const value = useMemo(() => ({
    config,
    setPrimaryColor,
    setHoverOpacity,
    setActiveOpacity,
    setBorderOpacity,
    resetToDefault,
    getHoverColor,
    getActiveColor,
    getBorderColor,
    getContrastTextColor,
  }), [config, setPrimaryColor, setHoverOpacity, setActiveOpacity, setBorderOpacity, resetToDefault, getHoverColor, getActiveColor, getBorderColor, getContrastTextColor]);

  return (
    <ThemeColorContext.Provider value={value}>
      {children}
    </ThemeColorContext.Provider>
  );
}

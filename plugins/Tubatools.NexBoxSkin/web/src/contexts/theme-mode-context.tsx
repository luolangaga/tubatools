"use client";

import {
  createContext,
  useContext,
  useState,
  useEffect,
  useRef,
  ReactNode,
  useCallback,
  useMemo,
} from "react";
import { useColorMode } from "@chakra-ui/react";
import { store } from "@/lib/store";
import {
  LS_THEME_MODE,
  type ThemeMode,
  isThemeMode,
  readInitialThemeMode,
  resolveScheme,
} from "@/lib/theme";

interface ThemeModeContextType {
  /** 用户选定的主题模式：跟随系统 / 浅色 / 深色 */
  themeMode: ThemeMode;
  setThemeMode: (mode: ThemeMode) => void;
}

const ThemeModeContext = createContext<ThemeModeContextType>({
  themeMode: "system",
  setThemeMode: () => {},
});

/** 写 localStorage 的浅/深缓存，托盘菜单等窗口依赖该键做跨窗口主题同步 */
function writeLocalScheme(mode: ThemeMode) {
  try {
    localStorage.setItem("chakra-ui-color-mode", resolveScheme(mode));
  } catch {
    // 忽略 localStorage 不可用
  }
}

/**
 * 主题模式三态协调 Provider：
 * - 统一管理 themeMode（system/light/dark）并持久化（localStorage + settings.json）。
 * - 将 themeMode 解析为 Chakra colorMode（light/dark），全项目 useColorModeValue 实时响应。
 * - "system" 模式下监听系统深浅偏好，实时跟随。
 *
 * 权威来源约定：
 * - settings.json 的 `theme-mode` 是**权威来源**（磁盘持久化，不受 WebView 数据清理影响）。
 * - localStorage 只作首帧渲染缓存，启动后会被 settings.json 校验并回填。
 * - 只有在用户显式切换（setThemeMode）时才写盘，避免挂载即写盘与其它 LazyStore 实例
 *   的 `save()` 全量覆盖产生竞态。
 */
export function ThemeModeProvider({ children }: { children: ReactNode }) {
  const { setColorMode } = useColorMode();
  const [themeMode, setThemeModeState] = useState<ThemeMode>(readInitialThemeMode);

  // 用户是否已显式切换过主题：只有显式切换才允许写盘
  const userChangedRef = useRef(false);

  // 启动恢复：以 settings.json 为准，修复 localStorage 缓存丢失/过期导致的回退
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const saved = await store.get<unknown>("theme-mode");
        if (cancelled) return;
        // 仅在用户尚未显式切换时应用已保存值，避免覆盖启动瞬间的用户操作
        if (!userChangedRef.current && isThemeMode(saved)) {
          setThemeModeState((prev) => (prev === saved ? prev : saved));
          try {
            localStorage.setItem(LS_THEME_MODE, saved);
          } catch {
            // 忽略 localStorage 不可用
          }
        }
      } catch (err) {
        console.error("Failed to restore theme mode:", err);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // 应用 themeMode 到 Chakra colorMode
  useEffect(() => {
    const apply = () => {
      setColorMode(resolveScheme(themeMode));
      // 同步 chakra-ui-color-mode，供托盘菜单等独立窗口读取
      writeLocalScheme(themeMode);
    };
    apply();

    if (themeMode !== "system") return;

    // 跟随系统：监听系统深浅偏好变化
    const mql = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => apply();
    mql.addEventListener("change", onChange);
    return () => mql.removeEventListener("change", onChange);
  }, [themeMode, setColorMode]);

  // 持久化：仅在用户显式切换时写盘（启动恢复不回写，消除挂载即写盘的竞态）
  useEffect(() => {
    try {
      localStorage.setItem(LS_THEME_MODE, themeMode);
    } catch {
      // 忽略 localStorage 不可用
    }

    if (!userChangedRef.current) return;

    store
      .set("theme-mode", themeMode)
      .then(() => store.save())
      .catch((err) => console.error("Failed to save theme mode:", err));
  }, [themeMode]);

  const setThemeMode = useCallback((mode: ThemeMode) => {
    userChangedRef.current = true;
    setThemeModeState((prev) => (prev === mode ? prev : mode));
  }, []);

  const value = useMemo(() => ({ themeMode, setThemeMode }), [themeMode, setThemeMode]);

  return <ThemeModeContext.Provider value={value}>{children}</ThemeModeContext.Provider>;
}

export function useThemeMode() {
  return useContext(ThemeModeContext);
}

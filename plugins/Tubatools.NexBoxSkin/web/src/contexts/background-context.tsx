"use client";

import { createContext, useContext, useState, ReactNode, useEffect, useRef, useCallback, useMemo } from "react";
import { useColorMode } from "@chakra-ui/react";
import { store } from "@/lib/store";

type BackgroundMode = "none" | "preset" | "image" | "dynamic" | "mr";

interface PresetBackground {
  id: number;
  name: string;
  path: string;
}

const PRESET_BACKGROUNDS: PresetBackground[] = [
  { id: 1, name: "预设1", path: "/backgrounds/preset1.webp" },
  { id: 2, name: "预设2", path: "/backgrounds/preset2.webp" },
  { id: 3, name: "预设3", path: "/backgrounds/preset3.webp" },
  { id: 4, name: "预设4", path: "/backgrounds/preset4.webp" },
];

/** 灵动岛玻璃样式：real = SVG 折射的真实液态玻璃；frosted = 毛玻璃（模糊磨砂） */
type IslandGlassMode = "real" | "frosted";

type MrColorMode = "theme" | "custom";

interface BackgroundContextType {
  backgroundMode: BackgroundMode;
  customBgImages: string[];
  activeBgIndex: number;
  dynamicBgVideo: string | null;
  liquidGlassEnabled: boolean;
  liquidGlassBlur: number;
  islandLiquidGlassEnabled: boolean;
  islandGlassMode: IslandGlassMode;
  backgroundBlur: number;
  activePresetIndex: number;
  presetBackgrounds: PresetBackground[];
  carouselEnabled: boolean;
  jellyBounceEnabled: boolean;
  mrColorMode: MrColorMode;
  mrCustomColor: string;
  setBackgroundMode: (mode: BackgroundMode) => void;
  setCustomBgImages: (images: string[]) => void;
  addCustomBgImage: (image: string) => boolean;
  removeCustomBgImage: (index: number) => void;
  setActiveBgIndex: (index: number) => void;
  setDynamicBgVideo: (video: string | null) => void;
  setLiquidGlassEnabled: (enabled: boolean) => void;
  setLiquidGlassBlur: (blur: number) => void;
  setIslandLiquidGlassEnabled: (enabled: boolean) => void;
  setIslandGlassMode: (mode: IslandGlassMode) => void;
  setBackgroundBlur: (blur: number) => void;
  setActivePresetIndex: (index: number) => void;
  setCarouselEnabled: (enabled: boolean) => void;
  setJellyBounceEnabled: (enabled: boolean) => void;
  setMrColorMode: (mode: MrColorMode) => void;
  setMrCustomColor: (color: string) => void;
}

const BackgroundContext = createContext<BackgroundContextType>({
  backgroundMode: "none",
  customBgImages: [],
  activeBgIndex: 0,
  dynamicBgVideo: null,
  liquidGlassEnabled: false,
  liquidGlassBlur: 10,
  islandLiquidGlassEnabled: false,
  islandGlassMode: "real",
  backgroundBlur: 0,
  activePresetIndex: 0,
  presetBackgrounds: PRESET_BACKGROUNDS,
  carouselEnabled: false,
  jellyBounceEnabled: false,
  mrColorMode: "theme",
  mrCustomColor: "#FF214F",
  setBackgroundMode: () => {},
  setCustomBgImages: () => {},
  addCustomBgImage: () => false,
  removeCustomBgImage: () => {},
  setActiveBgIndex: () => {},
  setDynamicBgVideo: () => {},
  setLiquidGlassEnabled: () => {},
  setLiquidGlassBlur: () => {},
  setIslandLiquidGlassEnabled: () => {},
  setIslandGlassMode: () => {},
  setBackgroundBlur: () => {},
  setActivePresetIndex: () => {},
  setCarouselEnabled: () => {},
  setJellyBounceEnabled: () => {},
  setMrColorMode: () => {},
  setMrCustomColor: () => {},
});

export function useBackground() {
  return useContext(BackgroundContext);
}


const MAX_BG_IMAGES = 3;

export function BackgroundProvider({ children }: { children: ReactNode }) {
  const [backgroundMode, setBackgroundMode] = useState<BackgroundMode>("none");
  const [customBgImages, setCustomBgImages] = useState<string[]>([]);
  const [activeBgIndex, setActiveBgIndex] = useState(0);
  const [dynamicBgVideo, setDynamicBgVideo] = useState<string | null>(null);
  const [liquidGlassEnabled, setLiquidGlassEnabled] = useState(false);
  const [liquidGlassBlur, setLiquidGlassBlur] = useState(10);
  const [islandLiquidGlassEnabled, setIslandLiquidGlassEnabled] = useState(false);
  const [islandGlassMode, setIslandGlassMode] = useState<IslandGlassMode>("real");
  const [backgroundBlur, setBackgroundBlur] = useState(0);
  const [activePresetIndex, setActivePresetIndex] = useState(0);
  const [carouselEnabled, setCarouselEnabled] = useState(false);
  const [jellyBounceEnabled, setJellyBounceEnabled] = useState(false);
  const [mrColorMode, setMrColorModeState] = useState<MrColorMode>("theme");
  const [mrCustomColor, setMrCustomColorState] = useState<string>("#FF214F");

  const { colorMode } = useColorMode();

  // 同步 Tooltip 样式到 CSS 变量：跟随液态玻璃开关与深浅主题，由 theme.ts 的 Tooltip baseStyle 消费。
  // 液态玻璃开启 → 半透明 + 模糊；关闭 → 实心胶囊（不再是液态玻璃效果）。
  useEffect(() => {
    const root = document.documentElement;
    const glass = liquidGlassEnabled;
    const isDark = colorMode === "dark";
    const bg = glass
      ? isDark ? "rgba(28,28,32,0.66)" : "rgba(255,255,255,0.55)"
      : isDark ? "#262626" : "#ffffff";
    const fg = isDark ? "#f0f0f0" : "#1a1a1a";
    const border = glass
      ? isDark ? "rgba(255,255,255,0.14)" : "rgba(255,255,255,0.5)"
      : isDark ? "rgba(255,255,255,0.12)" : "rgba(0,0,0,0.10)";
    root.style.setProperty("--nexbox-tooltip-bg", bg);
    root.style.setProperty("--nexbox-tooltip-fg", fg);
    root.style.setProperty("--nexbox-tooltip-border", border);
    root.style.setProperty("--nexbox-tooltip-blur", glass ? "blur(12px) saturate(1.4)" : "none");
  }, [liquidGlassEnabled, colorMode]);

  useEffect(() => {
    async function loadSettings() {
      try {
        // 批量读取所有设置，减少 store IO 调用次数
        const [savedMode, savedImages, savedActiveIndex, savedDynamicVideo, savedLiquidGlass, savedLiquidGlassBlur, savedIslandLiquidGlass, savedBgBlur, savedActivePreset, savedCarousel, savedJellyBounce, savedMrColorMode, savedMrCustomColor, hasLaunched, savedIslandGlassMode] =
          await Promise.all([
            store.get<string>("background-mode"),
            store.get<string[]>("custom-bg-images"),
            store.get<number>("active-bg-index"),
            store.get<string>("dynamic-bg-video"),
            store.get<boolean>("liquid-glass-enabled"),
            store.get<number>("liquid-glass-blur"),
            store.get<boolean>("island-liquid-glass-enabled"),
            store.get<number>("background-blur"),
            store.get<number>("active-preset-index"),
            store.get<boolean>("carousel-enabled"),
            store.get<boolean>("jelly-bounce-enabled"),
            store.get<string>("mr-color-mode"),
            store.get<string>("mr-custom-color"),
            store.get<boolean>("has-launched"),
            store.get<string>("island-glass-mode"),
          ]);

        // 透明液态玻璃已移除：旧版模式 / 增强设置一律删除，统一走毛玻璃
        const [savedLiquidGlassMode, savedLiquidGlassEnhanced] = await Promise.all([
          store.get<string>("liquid-glass-mode"),
          store.get<boolean>("liquid-glass-enhanced"),
        ]);
        if (savedLiquidGlassMode !== null && savedLiquidGlassMode !== undefined) {
          persistDelete("liquid-glass-mode");
        }
        if (savedLiquidGlassEnhanced !== null && savedLiquidGlassEnhanced !== undefined) {
          persistDelete("liquid-glass-enhanced");
        }

        // 检测是否首次启动
        if (!hasLaunched) {
          // 首次启动默认启用预设背景和自动轮播，并直接持久化
          const defaultMode: BackgroundMode = "preset";
          const defaultPresetIndex = 0;
          const defaultCarousel = true;
          setBackgroundMode(defaultMode);
          setActivePresetIndex(defaultPresetIndex);
          setCarouselEnabled(defaultCarousel);
          await store.set("background-mode", defaultMode);
          await store.set("active-preset-index", defaultPresetIndex);
          await store.set("carousel-enabled", defaultCarousel);
          await store.set("has-launched", true);
          await store.save();
        } else {
          // 非首次启动，加载保存的设置
          if (savedMode && ["none", "preset", "image", "dynamic", "mr"].includes(savedMode)) {
            setBackgroundMode(savedMode as BackgroundMode);
          }
          if (savedActivePreset !== null && savedActivePreset !== undefined) {
            setActivePresetIndex(savedActivePreset);
          }
          if (savedCarousel !== null && savedCarousel !== undefined) {
            setCarouselEnabled(savedCarousel);
          }
        }

        if (savedImages && Array.isArray(savedImages)) {
          setCustomBgImages(savedImages);
        }
        if (savedActiveIndex !== null && savedActiveIndex !== undefined) {
          setActiveBgIndex(savedActiveIndex);
        }
        if (savedDynamicVideo) {
          setDynamicBgVideo(savedDynamicVideo);
        }
        if (savedLiquidGlass !== null && savedLiquidGlass !== undefined) {
          setLiquidGlassEnabled(savedLiquidGlass);
        }
        if (savedLiquidGlassBlur !== null && savedLiquidGlassBlur !== undefined) {
          setLiquidGlassBlur(savedLiquidGlassBlur);
        }
        if (savedIslandLiquidGlass !== null && savedIslandLiquidGlass !== undefined) {
          setIslandLiquidGlassEnabled(savedIslandLiquidGlass);
        }
        if (savedIslandGlassMode === "real" || savedIslandGlassMode === "frosted") {
          setIslandGlassMode(savedIslandGlassMode);
        }
        if (savedBgBlur !== null && savedBgBlur !== undefined) {
          setBackgroundBlur(savedBgBlur);
        }
        if (savedJellyBounce !== null && savedJellyBounce !== undefined) {
          setJellyBounceEnabled(savedJellyBounce);
        }
        if (savedMrColorMode === "theme" || savedMrColorMode === "custom") {
          setMrColorModeState(savedMrColorMode);
        }
        if (savedMrCustomColor && /^#([0-9a-fA-F]{6})$/.test(savedMrCustomColor)) {
          setMrCustomColorState(savedMrCustomColor);
        }
      } catch (error) {
        console.error("Failed to load background settings:", error);
      }
    }

    loadSettings();
  }, []);

  // ─── 显式持久化 helper ───────────────────────────────────────────────
  // 和"能保存的"项使用完全相同的方式：store.set / store.delete + store.save。
  // 不再依赖 useEffect 批量保存，彻底消除闭包过期 / 竞态导致丢值的问题。
  //
  // 优化：按 key 合并 + 短延迟批量 flush。
  //   - 同一个 key 在 250ms 内连续修改只保留最新值（滑块拖动不会积压上百次 IPC）。
  //   - 多个不同 key 的修改会合并到同一次 store.save() 写盘。
  //   - flush 用串行链保证不会并发写盘互相覆盖。
  const pendingValuesRef = useRef<Record<string, unknown>>({});
  const pendingDeleteKeysRef = useRef<Set<string>>(new Set());
  const flushTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const flushChainRef = useRef<Promise<void>>(Promise.resolve());

  const flush = useCallback(() => {
    const values = pendingValuesRef.current;
    const deletes = pendingDeleteKeysRef.current;
    pendingValuesRef.current = {};
    pendingDeleteKeysRef.current = new Set();
    flushChainRef.current = flushChainRef.current.then(async () => {
      try {
        for (const [key, value] of Object.entries(values)) {
          await store.set(key, value);
        }
        for (const key of deletes) {
          await store.delete(key);
        }
        await store.save();
      } catch (e) {
        console.error("[Persist] flush failed:", e);
      }
    });
  }, []);

  const scheduleFlush = useCallback(() => {
    if (flushTimerRef.current) clearTimeout(flushTimerRef.current);
    flushTimerRef.current = setTimeout(() => {
      flushTimerRef.current = null;
      flush();
    }, 250);
  }, [flush]);

  const persist = useCallback((key: string, value: unknown) => {
    pendingValuesRef.current[key] = value;
    pendingDeleteKeysRef.current.delete(key);
    scheduleFlush();
  }, [scheduleFlush]);

  const persistDelete = useCallback((key: string) => {
    pendingDeleteKeysRef.current.add(key);
    delete pendingValuesRef.current[key];
    scheduleFlush();
  }, [scheduleFlush]);

  // ─── 对外暴露的 setter：更新 state + 立即持久化 ──────────────────────
  const setBackgroundModePersist = useCallback((mode: BackgroundMode) => {
    setBackgroundMode(mode);
    persist("background-mode", mode);
  }, [persist]);

  const setCustomBgImagesPersist = useCallback((images: string[]) => {
    setCustomBgImages(images);
    if (images.length > 0) {
      persist("custom-bg-images", images);
    } else {
      persistDelete("custom-bg-images");
    }
  }, [persist, persistDelete]);

  const setActiveBgIndexPersist = useCallback((index: number) => {
    setActiveBgIndex(index);
    persist("active-bg-index", index);
  }, [persist]);

  const setDynamicBgVideoPersist = useCallback((video: string | null) => {
    setDynamicBgVideo(video);
    if (video) {
      persist("dynamic-bg-video", video);
    } else {
      persistDelete("dynamic-bg-video");
    }
  }, [persist, persistDelete]);

  const setLiquidGlassEnabledPersist = useCallback((enabled: boolean) => {
    setLiquidGlassEnabled(enabled);
    persist("liquid-glass-enabled", enabled);
  }, [persist]);

  const setLiquidGlassBlurPersist = useCallback((blur: number) => {
    setLiquidGlassBlur(blur);
    persist("liquid-glass-blur", blur);
  }, [persist]);

  const setIslandLiquidGlassEnabledPersist = useCallback((enabled: boolean) => {
    setIslandLiquidGlassEnabled(enabled);
    persist("island-liquid-glass-enabled", enabled);
  }, [persist]);

  const setIslandGlassModePersist = useCallback((mode: IslandGlassMode) => {
    setIslandGlassMode(mode);
    persist("island-glass-mode", mode);
  }, [persist]);

  const setBackgroundBlurPersist = useCallback((blur: number) => {
    setBackgroundBlur(blur);
    persist("background-blur", blur);
  }, [persist]);

  const setActivePresetIndexPersist = useCallback((index: number) => {
    setActivePresetIndex(index);
    persist("active-preset-index", index);
  }, [persist]);

  const setCarouselEnabledPersist = useCallback((enabled: boolean) => {
    setCarouselEnabled(enabled);
    persist("carousel-enabled", enabled);
  }, [persist]);

  const setJellyBounceEnabledPersist = useCallback((enabled: boolean) => {
    setJellyBounceEnabled(enabled);
    persist("jelly-bounce-enabled", enabled);
  }, [persist]);

  const setMrColorModePersist = useCallback((mode: MrColorMode) => {
    setMrColorModeState(mode);
    persist("mr-color-mode", mode);
  }, [persist]);

  const setMrCustomColorPersist = useCallback((color: string) => {
    setMrCustomColorState(color);
    persist("mr-custom-color", color);
  }, [persist]);

  // 预设壁纸轮播定时器
  // 轮播是临时展示，用内部 setActivePresetIndex（不持久化），
  // 避免每 10 秒写一次磁盘；用户手动切换预设时才走 setActivePresetIndexPersist 持久化。
  const carouselTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    if (backgroundMode === "preset" && carouselEnabled) {
      carouselTimerRef.current = setInterval(() => {
        // 窗口最小化/隐藏到托盘时暂停轮播，降低后台 CPU 占用
        if (document.hidden) return;
        setActivePresetIndex((prev) => (prev + 1) % PRESET_BACKGROUNDS.length);
      }, 10000);
    } else {
      if (carouselTimerRef.current) {
        clearInterval(carouselTimerRef.current);
        carouselTimerRef.current = null;
      }
    }

    return () => {
      if (carouselTimerRef.current) {
        clearInterval(carouselTimerRef.current);
        carouselTimerRef.current = null;
      }
    };
  }, [backgroundMode, carouselEnabled]);

  const addCustomBgImage = useCallback((image: string): boolean => {
    if (customBgImages.length >= MAX_BG_IMAGES) {
      return false;
    }
    const newImages = [...customBgImages, image];
    setCustomBgImages(newImages);
    persist("custom-bg-images", newImages);
    return true;
  }, [customBgImages, persist]);

  const removeCustomBgImage = useCallback((index: number) => {
    const newImages = customBgImages.filter((_, i) => i !== index);
    setCustomBgImages(newImages);
    if (newImages.length > 0) {
      persist("custom-bg-images", newImages);
    } else {
      persistDelete("custom-bg-images");
    }
    if (activeBgIndex >= newImages.length) {
      const newIdx = Math.max(0, newImages.length - 1);
      setActiveBgIndex(newIdx);
      persist("active-bg-index", newIdx);
    }
  }, [customBgImages, activeBgIndex, persist, persistDelete]);

  // useMemo 稳定 context value，避免每次渲染创建新对象导致所有消费者重渲染
  const value = useMemo(() => ({
    backgroundMode,
    customBgImages,
    activeBgIndex,
    dynamicBgVideo,
    liquidGlassEnabled,
    liquidGlassBlur,
    islandLiquidGlassEnabled,
    islandGlassMode,
    backgroundBlur,
    activePresetIndex,
    presetBackgrounds: PRESET_BACKGROUNDS,
    carouselEnabled,
    jellyBounceEnabled,
    mrColorMode,
    mrCustomColor,
    setBackgroundMode: setBackgroundModePersist,
    setCustomBgImages: setCustomBgImagesPersist,
    addCustomBgImage,
    removeCustomBgImage,
    setActiveBgIndex: setActiveBgIndexPersist,
    setDynamicBgVideo: setDynamicBgVideoPersist,
    setLiquidGlassEnabled: setLiquidGlassEnabledPersist,
    setLiquidGlassBlur: setLiquidGlassBlurPersist,
    setIslandLiquidGlassEnabled: setIslandLiquidGlassEnabledPersist,
    setIslandGlassMode: setIslandGlassModePersist,
    setBackgroundBlur: setBackgroundBlurPersist,
    setActivePresetIndex: setActivePresetIndexPersist,
    setCarouselEnabled: setCarouselEnabledPersist,
    setJellyBounceEnabled: setJellyBounceEnabledPersist,
    setMrColorMode: setMrColorModePersist,
    setMrCustomColor: setMrCustomColorPersist,
  }), [backgroundMode, customBgImages, activeBgIndex, dynamicBgVideo, liquidGlassEnabled, liquidGlassBlur, islandLiquidGlassEnabled, islandGlassMode, backgroundBlur, activePresetIndex, carouselEnabled, jellyBounceEnabled, mrColorMode, mrCustomColor, setBackgroundModePersist, setCustomBgImagesPersist, addCustomBgImage, removeCustomBgImage, setActiveBgIndexPersist, setDynamicBgVideoPersist, setLiquidGlassEnabledPersist, setLiquidGlassBlurPersist, setIslandLiquidGlassEnabledPersist, setIslandGlassModePersist, setBackgroundBlurPersist, setActivePresetIndexPersist, setCarouselEnabledPersist, setJellyBounceEnabledPersist, setMrColorModePersist, setMrCustomColorPersist]);

  return (
    <BackgroundContext.Provider value={value}>
      {children}
    </BackgroundContext.Provider>
  );
}

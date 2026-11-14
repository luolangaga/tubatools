"use client";

import { useEffect, useMemo, useState } from "react";
import { useColorMode } from "@chakra-ui/react";
import { useBackground } from "@/contexts/background-context";

export interface AdaptiveTextColor {
  text: string;
  shadow: string;
}

function luma(r: number, g: number, b: number): number {
  return (0.299 * r + 0.587 * g + 0.114 * b) / 255;
}

/** 从图片「标题所在区域（上方约 65%）」采样平均亮度，返回 [0,1] */
function sampleImageLuma(url: string): Promise<number> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.crossOrigin = "anonymous";
    img.onload = () => {
      try {
        const iw = img.naturalWidth;
        const ih = img.naturalHeight;
        const cw = 64;
        const ch = 40;
        const canvas = document.createElement("canvas");
        canvas.width = cw;
        canvas.height = ch;
        const ctx = canvas.getContext("2d");
        if (!ctx) return reject(new Error("no 2d context"));
        ctx.drawImage(img, 0, 0, iw, ih, 0, 0, cw, ch);
        const data = ctx.getImageData(0, 0, cw, ch).data;
        let sum = 0;
        let n = 0;
        const topRows = Math.floor(ch * 0.65);
        for (let y = 0; y < topRows; y++) {
          for (let x = 0; x < cw; x++) {
            const i = (y * cw + x) * 4;
            sum += luma(data[i], data[i + 1], data[i + 2]);
            n++;
          }
        }
        resolve(sum / (n || 1));
      } catch (e) {
        reject(e);
      }
    };
    img.onerror = () => reject(new Error("background image load failed"));
    img.src = url;
  });
}

/** 从当前播放中的背景视频帧采样平均亮度；失败返回 null */
function sampleVideoLuma(): number | null {
  try {
    const v = document.querySelector<HTMLVideoElement>("video");
    if (!v || v.readyState < 2) return null;
    const cw = 64;
    const ch = 36;
    const canvas = document.createElement("canvas");
    canvas.width = cw;
    canvas.height = ch;
    const ctx = canvas.getContext("2d");
    if (!ctx) return null;
    ctx.drawImage(v, 0, 0, cw, ch);
    const data = ctx.getImageData(0, 0, cw, ch).data;
    let sum = 0;
    for (let i = 0; i < data.length; i += 4) {
      sum += luma(data[i], data[i + 1], data[i + 2]);
    }
    return sum / (cw * ch);
  } catch {
    return null;
  }
}

function pickColor(modeLuma: number | null): AdaptiveTextColor {
  const dark = modeLuma !== null && modeLuma > 0.5;
  return dark
    ? { text: "#141414", shadow: "0 1px 2px rgba(255,255,255,0.22)" }
    : { text: "#ffffff", shadow: "0 2px 6px rgba(0,0,0,0.45)" };
}

/** 无背景图片（none 模式）时，背景为纯主题色 */
function themeBgLuma(colorMode: "light" | "dark"): number {
  return colorMode === "dark" ? 0.02 : 0.97;
}

/**
 * 根据当前实际背景的明暗，自适应返回适合的主页标题文字颜色（黑/白 + 阴影），
 * 不再依赖主题模式切换。兼容预设图片、自定义图片、动态视频、MR 霓虹与纯色背景。
 */
export function useAdaptiveTextColor(): AdaptiveTextColor {
  const {
    backgroundMode,
    customBgImages,
    activeBgIndex,
    dynamicBgVideo,
    presetBackgrounds,
    activePresetIndex,
  } = useBackground();
  const { colorMode } = useColorMode();
  const [imgLuma, setImgLuma] = useState<number | null>(null);
  const [videoLuma, setVideoLuma] = useState<number | null>(null);

  const mode = backgroundMode;

  const imageUrl = useMemo(() => {
    if (mode === "preset") return presetBackgrounds[activePresetIndex]?.path ?? null;
    if (mode === "image") return customBgImages[activeBgIndex] ?? null;
    return null;
  }, [mode, presetBackgrounds, activePresetIndex, customBgImages, activeBgIndex]);

  useEffect(() => {
    if (!imageUrl) {
      setImgLuma(null);
      return;
    }
    let cancelled = false;
    sampleImageLuma(imageUrl)
      .then((l) => {
        if (!cancelled) setImgLuma(l);
      })
      .catch(() => {
        if (!cancelled) setImgLuma(null);
      });
    return () => {
      cancelled = true;
    };
  }, [imageUrl]);

  useEffect(() => {
    if (mode !== "dynamic" || !dynamicBgVideo) {
      setVideoLuma(null);
      return;
    }
    let timer: ReturnType<typeof setInterval> | null = null;
    const sample = () => {
      const l = sampleVideoLuma();
      if (l !== null) setVideoLuma(l);
    };
    sample();
    timer = setInterval(sample, 3000);
    return () => {
      if (timer) clearInterval(timer);
    };
  }, [mode, dynamicBgVideo]);

  return useMemo(() => {
    const themeLuma = themeBgLuma(colorMode);
    switch (mode) {
      case "mr":
        // MR 为深色霓虹背景，固定用亮色文字
        return pickColor(0.05);
      case "dynamic":
        return pickColor(videoLuma ?? themeLuma);
      case "preset":
      case "image":
        // 采样失败时回退到主题亮度，避免出现反差不足
        return pickColor(imgLuma ?? themeLuma);
      default:
        return pickColor(themeLuma);
    }
  }, [mode, videoLuma, imgLuma, colorMode]);
}
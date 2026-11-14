"use client";

import { Box, useColorModeValue } from "@chakra-ui/react";
import { useCallback, useEffect, useRef, useState } from "react";
import { useThemeColor } from "@/contexts/theme-color-context";

interface FloatingScrollbarProps {
  /** 目标滚动容器元素 id（默认主内容滚动区） */
  targetId?: string;
}

/** 滑块最小高度（px） */
const MIN_THUMB = 14;
/** 指示器距容器底部的留白（px） */
const BOTTOM_INSET = 24;
/** 轨道整体高度：占可用区域比例与上下限（保证「短」而非贯穿整页） */
const TRACK_FACTOR = 0.26;
const TRACK_MIN = 76;
const TRACK_MAX = 120;
/** 滑块最长不超过轨道的该比例，且不大于下面的绝对上限 */
const THUMB_CAP = 0.45;
const THUMB_MAX = 46;
/** 轨道 / 滑块宽度（px） */
const TRACK_W = 8;
const THUMB_W = 6;

interface BarGeom {
  /** 是否可见（内容不足一屏时隐藏） */
  visible: boolean;
  /** 轨道 fixed 元素距视口顶部的偏移（px） */
  trackTop: number;
  /** 轨道高度（px） */
  trackHeight: number;
  /** 滑块在轨道内的位移（px） */
  thumbTop: number;
  /** 滑块高度（px） */
  thumbHeight: number;
}

const HIDDEN: BarGeom = { visible: false, trackTop: 0, trackHeight: 0, thumbTop: 0, thumbHeight: 0 };

/**
 * 自定义页面悬浮滚动条：
 * - 非原生（不依赖 ::-webkit-scrollbar），全局样式已把原生滚动条隐藏
 * - 右侧一条「短」圆角半透明轨道作为包裹，滑块在其中上下移动，整体不贯穿整页
 * - 固定在主内容区右侧留白内浮动，实时反映滚动位置，支持按住拖拽滚动（不改变手势光标）
 */
export function FloatingScrollbar({ targetId = "app-main-scroll" }: FloatingScrollbarProps) {
  const { config } = useThemeColor();
  const [geom, setGeom] = useState<BarGeom>(HIDDEN);
  const [hover, setHover] = useState(false);
  const [dragging, setDragging] = useState(false);
  const elRef = useRef<HTMLElement | null>(null);
  const thumbRef = useRef<HTMLDivElement | null>(null);
  const rafRef = useRef(0);
  const dragRef = useRef<{ startY: number; startScrollTop: number; max: number; travel: number } | null>(null);

  // 重新计算轨道与滑块几何：轨道长度适中，滑块长度按可见比例、位移按滚动进度。
  // 滑块位移直接写 DOM（滚动热路径不进 React）；轨道几何量只有真正变化才 setState，
  // 配合 rAF 合帧，滚动时每帧最多一次计算、零重渲染。
  const update = useCallback(() => {
    const el = elRef.current;
    if (!el) return;
    const { scrollTop, scrollHeight, clientHeight } = el;
    const max = scrollHeight - clientHeight;
    if (max <= 1) {
      setGeom((prev) => (prev.visible ? HIDDEN : prev));
      return;
    }
    const cs = getComputedStyle(el);
    const padTop = parseFloat(cs.paddingTop) || 0;
    const regionH = Math.max(clientHeight - padTop - BOTTOM_INSET, TRACK_MIN);
    const trackHeight = Math.min(Math.max(regionH * TRACK_FACTOR, TRACK_MIN), TRACK_MAX);
    const trackTop = padTop + (regionH - trackHeight) / 2;
    const ratio = clientHeight / scrollHeight;
    const thumbHeight = Math.min(Math.max(ratio * trackHeight, MIN_THUMB), trackHeight * THUMB_CAP, THUMB_MAX);
    const travel = Math.max(trackHeight - thumbHeight, 0);
    const thumbTop = (scrollTop / max) * travel;
    setGeom((prev) => {
      const same =
        prev.visible &&
        prev.trackTop === trackTop &&
        prev.trackHeight === trackHeight &&
        prev.thumbHeight === thumbHeight;
      // 纯滚动：只有位移变化，跳过重渲染
      if (same) return prev;
      return { visible: true, trackTop, trackHeight, thumbTop, thumbHeight };
    });
    const thumb = thumbRef.current;
    if (thumb) thumb.style.transform = `translate(-50%, ${thumbTop}px)`;
  }, []);

  // 滚动事件按 rAF 合帧：一帧内多次 scroll 事件只算一次
  const scheduleUpdate = useCallback(() => {
    if (rafRef.current) return;
    rafRef.current = requestAnimationFrame(() => {
      rafRef.current = 0;
      update();
    });
  }, [update]);

  // 绑定滚动容器与事件监听；容器尚未挂载时用 rAF 重试
  useEffect(() => {
    let disposed = false;
    let ro: ResizeObserver | undefined;
    let rafId = 0;

    const attach = () => {
      if (disposed) return;
      const el = document.getElementById(targetId);
      if (!el) {
        rafId = requestAnimationFrame(attach);
        return;
      }
      elRef.current = el;
      update();
      el.addEventListener("scroll", scheduleUpdate, { passive: true });
      if (typeof ResizeObserver !== "undefined") {
        ro = new ResizeObserver(scheduleUpdate);
        ro.observe(el);
        if (el.firstElementChild) ro.observe(el.firstElementChild);
      }
      window.addEventListener("resize", scheduleUpdate);
    };

    attach();

    return () => {
      disposed = true;
      cancelAnimationFrame(rafId);
      cancelAnimationFrame(rafRef.current);
      rafRef.current = 0;
      const el = elRef.current;
      el?.removeEventListener("scroll", scheduleUpdate);
      ro?.disconnect();
      window.removeEventListener("resize", scheduleUpdate);
      elRef.current = null;
    };
  }, [targetId, update, scheduleUpdate]);

  // 路由切换后新内容挂载，重算一次
  useEffect(() => {
    update();
  }, [update]);

  const onPointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    const el = elRef.current;
    if (!el) return;
    const max = el.scrollHeight - el.clientHeight;
    const travel = Math.max(geom.trackHeight - geom.thumbHeight, 0);
    dragRef.current = { startY: e.clientY, startScrollTop: el.scrollTop, max, travel };
    setDragging(true);
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    e.preventDefault();
  };

  const onPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current;
    const el = elRef.current;
    if (!drag || !el || drag.travel <= 0) return;
    const deltaY = e.clientY - drag.startY;
    const next = drag.startScrollTop + (deltaY / drag.travel) * drag.max;
    el.scrollTop = Math.max(0, Math.min(drag.max, next));
  };

  const endDrag = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!dragRef.current) return;
    dragRef.current = null;
    setDragging(false);
    try {
      (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId);
    } catch {
      // pointer 已释放，忽略
    }
  };

  const active = hover || dragging;
  // 轨道背景随明暗模式取中性黑白（浅色模式黑、深色模式白），滑块仍用主题色
  const trackBg = useColorModeValue("rgba(0, 0, 0, 0.16)", "rgba(255, 255, 255, 0.18)");

  return (
    <Box
      position="fixed"
      right="16px"
      top={`${geom.trackTop}px`}
      width={`${TRACK_W}px`}
      height={`${geom.trackHeight}px`}
      borderRadius="999px"
      background={trackBg}
      zIndex={10}
      pointerEvents="none"
      aria-hidden
      display={geom.visible ? "block" : "none"}
    >
      <Box
        as="div"
        ref={thumbRef}
        position="absolute"
        top={0}
        left="50%"
        width={`${THUMB_W}px`}
        height={`${geom.thumbHeight}px`}
        transform={`translate(-50%, ${geom.thumbTop}px)`}
        background={config.primaryColor}
        opacity={active ? 0.95 : 0.7}
        borderRadius="999px"
        pointerEvents="auto"
        cursor="default"
        onPointerEnter={() => setHover(true)}
        onPointerLeave={() => setHover(false)}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
        sx={{
          willChange: "transform, height",
          transition: "opacity 0.15s ease, height 0.15s ease",
          touchAction: "none",
          userSelect: "none",
        }}
      />
    </Box>
  );
}

"use client";
import { Box, BoxProps, useColorModeValue } from "@chakra-ui/react";
import { useBackground } from "@/contexts/background-context";
import { getBorderGlowStyle } from "@/hooks/use-glow-effect";
import { useMemo } from "react";

interface LiquidGlassCardProps extends BoxProps {
  children: React.ReactNode;
  className?: string;
  isDashed?: boolean;
  /** 强制启用液态玻璃效果（忽略全局开关） */
  forceGlass?: boolean;
  /** 是否显示玻璃边框辉光（白边），默认 true */
  glow?: boolean;
}

export function LiquidGlassCard({
  children,
  className,
  isDashed = false,
  forceGlass = false,
  glow = true,
  ...props
}: LiquidGlassCardProps) {
  const { liquidGlassEnabled, liquidGlassBlur } = useBackground();
  const glassOn = forceGlass || liquidGlassEnabled;

  // 强制玻璃时使用明显的模糊值，避免全局关闭时看起来像没有效果
  const effectiveGlassBlur = forceGlass && !liquidGlassEnabled ? 16 : liquidGlassBlur;

  // 强制玻璃时背景稍微加一点不透明度，保证内容可读、不会透到看不清
  const glassBgColor = useColorModeValue(
    forceGlass ? "rgba(255,255,255,0.4)" : "rgba(255,255,255,0.25)",
    forceGlass ? "rgba(0,0,0,0.45)" : "rgba(0,0,0,0.25)"
  );
  const glassBorderColor = useColorModeValue(
    forceGlass ? "rgba(255,255,255,0.35)" : "rgba(255,255,255,0.2)",
    forceGlass ? "rgba(255,255,255,0.16)" : "rgba(255,255,255,0.1)"
  );
  const glowColor = useColorModeValue("rgba(255,255,255,0.8)", "rgba(255,255,255,0.5)");
  const defaultBg = useColorModeValue("white", "#111111");
  const defaultBorder = useColorModeValue("gray.200", "#333333");

  // 模糊立即生效
  const effectiveBlur = glassOn ? effectiveGlassBlur : 0;

  // 卡片主体：零滤镜
  const backdropFilter = useMemo(() => {
    if (!glassOn) return "none";
    return `blur(${effectiveBlur}px) saturate(1.3)`;
  }, [glassOn, effectiveBlur]);

  const cardStyles = useMemo(() => {
    const base: any = {
      bg: glassOn ? glassBgColor : defaultBg,
      borderRadius: "3xl" as const,
      border: isDashed ? "1px dashed" : "1px solid",
      borderColor: glassOn ? glassBorderColor : defaultBorder,
      backdropFilter,
      WebkitBackdropFilter: backdropFilter,
      transition: "background 0.45s, border-color 0.45s, backdrop-filter 0.45s",
    };
    return base;
  }, [backdropFilter, glassOn, glassBgColor, glassBorderColor, defaultBg, defaultBorder, isDashed]);

  const borderGlowStyle = useMemo(() => getBorderGlowStyle(glowColor), [glowColor]);

  return (
    <Box
      className={`jelly-bounce-card${className ? ` ${className}` : ""}`}
      {...cardStyles}
      position="relative"
      overflow="visible"
      {...props}
    >
      {glow && (
        <Box
          style={borderGlowStyle}
          opacity={glassOn ? 1 : 0}
          transition="opacity 0.45s cubic-bezier(0.4, 0, 0.2, 1)"
        />
      )}
      {children}
    </Box>
  );
}

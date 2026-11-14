"use client";

import { Box, useColorModeValue } from "@chakra-ui/react";
import { useCallback } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { ArrowLeft } from "lucide-react";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useBackground } from "@/contexts/background-context";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { resolveParent } from "@/hooks/use-esc-back";

export interface PageBackButtonProps {
  /** 目标路径；不传且未传 onClick 时，用 resolveParent(location.pathname) 自动推断父级 */
  to?: string;
  /** 自定义行为（页内视图返回），优先级高于 to */
  onClick?: () => void;
  /** 文案，默认 t("common.back") */
  label?: string;
  size?: "sm" | "md";
  className?: string;
  style?: React.CSSProperties;
}

const SIZES = {
  sm: { h: "32px", px: "8px", gap: "4px", icon: 15, fontSize: "xs" },
  md: { h: "36px", px: "10px", gap: "6px", icon: 17, fontSize: "sm" },
} as const;

/**
 * 全站统一的「返回」按钮：返回图标 + 返回字样 + 玻璃卡片包裹。
 *
 * 玻璃相关约束（勿改）：
 * - hover/active 只通过「染色层 opacity + 边框色」表达，不加 bg / boxShadow / filter / opacity。
 * - transition 必须把 LiquidGlassCard 原有的三个过渡一起写上，否则会被整体覆盖。
 */
export function PageBackButton({
  to,
  onClick,
  label,
  size = "md",
  className,
  style,
}: PageBackButtonProps) {
  const navigate = useNavigate();
  const location = useLocation();
  const { t } = useTranslation();
  const { liquidGlassEnabled } = useBackground();
  const adaptive = useAdaptiveTextColor();

  const text = label ?? t("common.back");
  const s = SIZES[size];
  const glassOn = liquidGlassEnabled;

  // 玻璃关闭时卡片是白/深色实心，必须用主题色文字；玻璃开启时文字浮在壁纸模糊层上，跟随背景自适应
  const solidText = useColorModeValue("gray.900", "#ffffff");
  const color = glassOn ? adaptive.text : solidText;
  const textShadow = glassOn ? adaptive.shadow : undefined;

  const handleClick = useCallback(() => {
    if (onClick) {
      onClick();
      return;
    }
    const target = to ?? resolveParent(location.pathname);
    if (target) {
      navigate(target);
      return;
    }
    if (import.meta.env.DEV) {
      console.warn(`[PageBackButton] 未找到 ${location.pathname} 的父级路由，回退到历史返回`);
    }
    navigate(-1);
  }, [onClick, to, location.pathname, navigate]);

  // BoxProps 不含 type，字面量写法会触发多余属性检查，用变量展开绕过
  const nativeAttrs = { type: "button" } as any;

  return (
    <LiquidGlassCard
      as="button"
      {...nativeAttrs}
      onClick={handleClick}
      display="inline-flex"
      alignItems="center"
      justifyContent="center"
      gap={s.gap}
      h={s.h}
      minH={s.h}
      px={s.px}
      flexShrink={0}
      cursor="pointer"
      userSelect="none"
      borderRadius="2xl"
      color={color}
      textShadow={textShadow}
      fontSize={s.fontSize}
      fontWeight="500"
      lineHeight="1"
      whiteSpace="nowrap"
      className={`no-bounce${className ? ` ${className}` : ""}`}
      _focusVisible={{
        outline: "2px solid",
        outlineColor: "currentColor",
        outlineOffset: "2px",
      }}
      transition="background 0.45s, border-color 0.45s, backdrop-filter 0.45s"
      sx={{
        "&:active .pbb-tint": { opacity: 0.14 },
      }}
      style={style}
    >
      {/* 按下时的轻微染色：只叠一层 currentColor，不碰 bg / box-shadow / filter */}
      <Box
        className="pbb-tint"
        position="absolute"
        inset={0}
        borderRadius="inherit"
        bg="currentColor"
        opacity={0}
        transition="opacity 0.18s ease"
        pointerEvents="none"
        zIndex={1}
      />
      <ArrowLeft size={s.icon} strokeWidth={2.2} style={{ position: "relative", zIndex: 2 }} />
      <Box as="span" position="relative" zIndex={2}>
        {text}
      </Box>
    </LiquidGlassCard>
  );
}

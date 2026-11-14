"use client";

import { Box, useColorModeValue, type BoxProps } from "@chakra-ui/react";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useBackground } from "@/contexts/background-context";

/**
 * 三角洲专区通用容器：开启液态玻璃时用 LiquidGlassCard，否则用普通卡片。
 * 保持与项目其它卡片一致的底色 / 边框。
 *
 * 固定带 `no-bounce` —— LiquidGlassCard 默认挂 `jelly-bounce-card`，
 * 路由切换时会播放果冻弹跳动画（项目全局「果冻弹跳」设置），本专区不需要。
 */
export function Surface({
  glass,
  children,
  className,
  ...rest
}: { glass?: boolean; children: React.ReactNode } & BoxProps) {
  const { liquidGlassEnabled } = useBackground();
  const on = glass ?? liquidGlassEnabled;
  const bg = useColorModeValue("white", "#111111");
  const border = useColorModeValue("gray.200", "#333333");
  const cls = className ? `no-bounce ${className}` : "no-bounce";

  if (on) {
    return (
      <LiquidGlassCard className={cls} {...rest}>
        {children}
      </LiquidGlassCard>
    );
  }
  return (
    <Box
      className={cls}
      bg={bg}
      borderRadius="3xl"
      border="1px solid"
      borderColor={border}
      {...rest}
    >
      {children}
    </Box>
  );
}

"use client";

import { Box, HStack, Heading } from "@chakra-ui/react";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { PageBackButton } from "@/components/special/page-back-button";

export interface PageHeaderProps {
  title: React.ReactNode;
  /** 右侧操作区（按钮、徽标等） */
  children?: React.ReactNode;
  /** 底部间距，默认 6 */
  mb?: number | string;
  /** 是否显示返回按钮，默认 true */
  showBack?: boolean;
}

/**
 * 全站统一的页头：返回按钮固定在左侧，标题在页面正中，右侧放操作区。
 * 标题用绝对定位居中，因此左右两侧宽度不一致也不会让标题偏移。
 */
export function PageHeader({
  title,
  children,
  mb = 6,
  showBack = true,
}: PageHeaderProps) {
  const adaptive = useAdaptiveTextColor();

  return (
    <HStack position="relative" w="full" justify="center" align="center" mb={mb} minH="36px">
      {showBack && (
        <Box position="absolute" left={0} top="50%" transform="translateY(-50%)" zIndex={1}>
          <PageBackButton />
        </Box>
      )}

      <Box minW={0} maxW="72%" textAlign="center">
        <Heading
          size="lg"
          color={adaptive.text}
          textShadow={adaptive.shadow}
          fontWeight="700"
        >
          {title}
        </Heading>
      </Box>

      {children && (
        <Box position="absolute" right={0} top="50%" transform="translateY(-50%)" zIndex={1}>
          <HStack spacing={2}>{children}</HStack>
        </Box>
      )}
    </HStack>
  );
}

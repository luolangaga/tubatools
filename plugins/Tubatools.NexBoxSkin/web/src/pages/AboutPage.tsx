import {
  Box,
  Button,
  HStack,
  Heading,
  Text,
  VStack,
  useColorModeValue,
} from "@chakra-ui/react";
import { ExternalLink, Heart, Info } from "lucide-react";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { gamebox } from "@/lib/gamebox";

export default function AboutPage() {
  const adaptive = useAdaptiveTextColor();
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  const border = useColorModeValue("gray.200", "#333333");

  return (
    <Box pt={6} pb={10} px={{ base: 2, md: 4 }} maxW="880px">
      <Heading size="lg" mb={1} color={adaptive.text} textShadow={adaptive.shadow}>
        关于游戏盒
      </Heading>
      <Text fontSize="sm" color={adaptive.text} opacity={0.78} mb={4} textShadow={adaptive.shadow}>
        NexBox 风格界面 + 图吧工具箱
      </Text>

      <VStack align="stretch" spacing={4}>
        <LiquidGlassCard p={5}>
          <HStack align="flex-start" spacing={3}>
            <Info size={20} />
            <VStack align="start" spacing={2}>
              <Text fontWeight="semibold">NexBox 风格复刻</Text>
              <Text fontSize="sm" color={sub}>
                本插件的界面与部分功能逻辑裁剪移植自 NexBox（GPL-3.0）。原项目：
                https://github.com/MuLiuSaMa/NexBox
              </Text>
              <Button
                size="xs"
                variant="outline"
                rightIcon={<ExternalLink size={12} />}
                onClick={() => void gamebox.openUri("https://github.com/MuLiuSaMa/NexBox")}
              >
                打开 NexBox 仓库
              </Button>
            </VStack>
          </HStack>
        </LiquidGlassCard>

        <LiquidGlassCard p={5}>
          <VStack align="stretch" spacing={3}>
            <Text fontWeight="semibold">图吧工具箱</Text>
            <Text fontSize="sm" color={sub}>
              工具启动、硬件诊断、系统信息等能力由图吧工具箱宿主提供。本插件不读取游戏内存、不注入游戏进程。
            </Text>
            <HStack>
              <Button
                size="xs"
                variant="outline"
                rightIcon={<ExternalLink size={12} />}
                onClick={() => void gamebox.openUri("https://github.com/luolangaga/tubawinui3")}
              >
                图吧工具箱仓库
              </Button>
              <Button size="xs" variant="ghost" onClick={() => void gamebox.navigate("settings")}>
                打开宿主设置
              </Button>
            </HStack>
          </VStack>
        </LiquidGlassCard>

        <LiquidGlassCard p={5}>
          <VStack align="stretch" spacing={2} pt={0}>
            <HStack>
              <Heart size={16} />
              <Text fontWeight="semibold">第三方组件</Text>
            </HStack>
            <Text fontSize="sm" color={sub}>
              React、Chakra UI、Emotion、Framer Motion、lucide-react、React Router、i18next、react-i18next、
              dnd-kit（上游依赖）。完整许可见插件包内 THIRD-PARTY-NOTICES.md。
            </Text>
          </VStack>
        </LiquidGlassCard>

        <LiquidGlassCard p={5}>
          <VStack align="stretch" spacing={2}>
            <Text fontWeight="semibold">免责声明</Text>
            <Text fontSize="sm" color={sub}>
              三角洲改枪码、主播设置与随机装备数据来自公开来源；每日密码与战绩分析需要 NexBox 的游戏数据后端，
              当前版本未迁移。请遵守游戏与平台的使用条款。
            </Text>
          </VStack>
        </LiquidGlassCard>
      </VStack>
    </Box>
  );
}

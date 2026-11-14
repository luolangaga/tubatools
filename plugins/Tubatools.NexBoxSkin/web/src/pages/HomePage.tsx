import {
  Badge,
  Box,
  Button,
  HStack,
  Heading,
  SimpleGrid,
  Spinner,
  Text,
  VStack,
  useColorModeValue,
} from "@chakra-ui/react";
import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import {
  ChevronRight,
  Cpu,
  Gamepad2,
  HardDrive,
  MemoryStick,
  Sparkles,
} from "lucide-react";
import { ToolIcon } from "@/components/ToolIcon";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { getGreeting } from "@/lib/greetings";
import {
  formatGb,
  formatNumber,
  gamebox,
  type HardwareSection,
  type SensorSample,
  type ToolInfo,
} from "@/lib/gamebox";

const QUICK_TOOL_IDS = [
  "space-cleanup",
  "driver-store",
  "game-tunnel",
  "ai-assistant",
  "time-sync",
  "format-converter",
  "log-viewer",
  "network-optimize",
];

function MetricCard({
  icon,
  title,
  value,
  detail,
  color,
}: {
  icon: React.ReactNode;
  title: string;
  value: string;
  detail: string;
  color: string;
}) {
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  return (
    <LiquidGlassCard p={4}>
      <VStack align="stretch" spacing={1}>
        <HStack justify="space-between">
          <Text fontSize="sm" color={sub} fontWeight="medium">
            {title}
          </Text>
          <Box color={color}>{icon}</Box>
        </HStack>
        <Text fontSize="xl" fontWeight="bold">
          {value}
        </Text>
        <Text fontSize="xs" color={sub} noOfLines={1}>
          {detail}
        </Text>
      </VStack>
    </LiquidGlassCard>
  );
}

function QuickTool({ tool }: { tool: ToolInfo }) {
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  return (
    <LiquidGlassCard
      p={4}
      cursor="pointer"
      _hover={{ transform: "translateY(-2px)" }}
      transition="transform 0.18s"
      onClick={() => void gamebox.runTool(tool.id)}
    >
      <VStack align="start" spacing={1}>
        <ToolIcon tool={tool} size={24} />
        <Text fontWeight="semibold" fontSize="sm" noOfLines={1}>
          {tool.name}
        </Text>
        <Text fontSize="xs" color={sub} noOfLines={2}>
          {tool.description || tool.category}
        </Text>
      </VStack>
    </LiquidGlassCard>
  );
}

export default function HomePage() {
  const adaptive = useAdaptiveTextColor();
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  const primary = "#98DDD0";
  const [sensors, setSensors] = useState<SensorSample | null>(null);
  const [hardware, setHardware] = useState<HardwareSection[]>([]);
  const [tools, setTools] = useState<ToolInfo[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let alive = true;
    let timer = 0;
    const tick = async () => {
      if (document.hidden) return;
      try {
        const sample = await gamebox.sensors();
        if (alive) setSensors(sample);
      } catch {
        /* sensors may be unavailable on some machines */
      }
    };
    void tick();
    timer = window.setInterval(tick, 2000);
    return () => {
      alive = false;
      window.clearInterval(timer);
    };
  }, []);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const [hardwareRaw, toolList] = await Promise.all([gamebox.hardware(), gamebox.listTools()]);
        if (!alive) return;
        const sections = Array.isArray(hardwareRaw)
          ? hardwareRaw
          : ((hardwareRaw as any)?.data as HardwareSection[]) ?? [];
        setHardware(sections);
        setTools(toolList ?? []);
      } catch {
        /* keep empty state */
      } finally {
        if (alive) setLoading(false);
      }
    })();
    return () => {
      alive = false;
    };
  }, []);

  const greeting = useMemo(() => getGreeting(new Date(), "").text, []);
  const items = useMemo(() => hardware.flatMap((section) => section.items ?? []), [hardware]);
  const cpu = items.find((item) => item.label === "处理器")?.value ?? sensors?.cpuName ?? "正在读取";
  const gpu = items.find((item) => item.label === "显卡")?.value ?? sensors?.gpuName ?? "正在读取";
  const memory = items.find((item) => item.label === "内存")?.value ?? "";
  const quickTools = useMemo(() => {
    const byId = new Map(tools.map((tool) => [tool.id, tool]));
    return QUICK_TOOL_IDS.map((id) => byId.get(id)).filter(Boolean).slice(0, 8) as ToolInfo[];
  }, [tools]);

  return (
    <Box pt={6} pb={10} px={{ base: 2, md: 4 }}>
      <HStack justify="space-between" align="flex-start" mb={6} wrap="wrap" spacing={4}>
        <Box>
          <Text
            fontSize={{ base: "2xl", md: "3xl" }}
            fontWeight="bold"
            lineHeight="1.35"
            color={adaptive.text}
            textShadow={adaptive.shadow}
          >
            {greeting}
          </Text>
          <Text mt={2} color={adaptive.text} opacity={0.78} fontSize="sm" textShadow={adaptive.shadow}>
            图吧工具箱 · NexBox 风格游戏盒
          </Text>
        </Box>
        <HStack spacing={2}>
          <Badge colorScheme="teal" variant="subtle" borderRadius="full" px={3} py={1}>
            <HStack spacing={1}>
              <Sparkles size={12} />
              <Text>Delta Force</Text>
            </HStack>
          </Badge>
        </HStack>
      </HStack>

      <SimpleGrid columns={{ base: 1, md: 3 }} spacing={4} mb={6}>
        <MetricCard
          icon={<Cpu size={18} />}
          title="CPU"
          value={`${formatNumber(sensors?.cpuLoad, 0)}%`}
          detail={`${formatNumber(sensors?.cpuTemp, 1)}°C · ${formatNumber(sensors?.cpuClock, 0)} MHz · ${formatNumber(sensors?.cpuPower, 1)} W`}
          color={primary}
        />
        <MetricCard
          icon={<Gamepad2 size={18} />}
          title="GPU"
          value={`${formatNumber(sensors?.gpuLoad, 0)}%`}
          detail={`${formatNumber(sensors?.gpuTemp, 1)}°C · ${formatNumber(sensors?.gpuClock, 0)} MHz · ${formatNumber(sensors?.gpuPower, 1)} W`}
          color="#7C9EFF"
        />
        <MetricCard
          icon={<MemoryStick size={18} />}
          title="内存"
          value={formatGb(sensors?.memUsedGB)}
          detail={`共 ${formatGb(sensors?.memTotalGB)} · ${formatNumber(sensors?.memLoad, 0)}%`}
          color="#FFB86B"
        />
      </SimpleGrid>

      <SimpleGrid columns={{ base: 1, lg: 2 }} spacing={4} mb={6}>
        <LiquidGlassCard p={5}>
          <VStack align="stretch" spacing={3}>
            <HStack justify="space-between">
              <Heading size="sm">本机硬件</Heading>
              <Button
                as={Link}
                to="/hardware"
                size="xs"
                variant="ghost"
                rightIcon={<ChevronRight size={14} />}
              >
                完整信息
              </Button>
            </HStack>
            <VStack align="stretch" spacing={2} fontSize="sm">
              <HStack justify="space-between" spacing={4}>
                <Text color={sub} flexShrink={0}>
                  处理器
                </Text>
                <Text noOfLines={1} textAlign="right">
                  {cpu}
                </Text>
              </HStack>
              <HStack justify="space-between" spacing={4}>
                <Text color={sub} flexShrink={0}>
                  显卡
                </Text>
                <Text noOfLines={1} textAlign="right">
                  {gpu}
                </Text>
              </HStack>
              <HStack justify="space-between" spacing={4}>
                <Text color={sub} flexShrink={0}>
                  内存
                </Text>
                <Text noOfLines={1} textAlign="right">
                  {memory || formatGb(sensors?.memTotalGB)}
                </Text>
              </HStack>
            </VStack>
          </VStack>
        </LiquidGlassCard>

        <LiquidGlassCard
          p={5}
          bgGradient="linear(135deg, rgba(152,221,208,0.22), rgba(124,158,255,0.18))"
        >
          <VStack align="stretch" spacing={3}>
            <Badge alignSelf="flex-start" colorScheme="teal" borderRadius="full">
              三角洲行动
            </Badge>
            <Heading size="md">改枪码 · 主播设置 · 随机装备</Heading>
            <Text fontSize="sm" color={sub}>
              浏览社区改枪码、查看主播同款设置，或者让随机装备帮你决定下一局带什么。
            </Text>
            <HStack>
              <Button
                as={Link}
                to="/delta-force"
                size="sm"
                colorScheme="teal"
                rightIcon={<ChevronRight size={14} />}
              >
                进入专区
              </Button>
              <Button
                as={Link}
                to="/delta-force/random-equipment"
                size="sm"
                variant="outline"
              >
                随机装备
              </Button>
            </HStack>
          </VStack>
        </LiquidGlassCard>
      </SimpleGrid>

      <HStack justify="space-between" mb={3}>
        <Heading size="sm" color={adaptive.text} textShadow={adaptive.shadow}>
          快捷工具
        </Heading>
        <Button
          as={Link}
          to="/tools"
          size="xs"
          variant="ghost"
          color={adaptive.text}
          textShadow={adaptive.shadow}
          rightIcon={<ChevronRight size={14} />}
        >
          全部工具
        </Button>
      </HStack>

      {loading ? (
        <HStack justify="center" py={8}>
          <Spinner size="sm" />
          <Text fontSize="sm" color={sub}>
            正在读取工具列表…
          </Text>
        </HStack>
      ) : (
        <SimpleGrid columns={{ base: 2, md: 4 }} spacing={3}>
          {quickTools.map((tool) => (
            <QuickTool key={tool.id} tool={tool} />
          ))}
        </SimpleGrid>
      )}

      {!loading && quickTools.length === 0 && (
        <LiquidGlassCard p={5}>
          <HStack spacing={3}>
            <HardDrive size={18} />
            <Text fontSize="sm" color={sub}>
              没有读取到内置工具，请确认插件已启用。
            </Text>
          </HStack>
        </LiquidGlassCard>
      )}
    </Box>
  );
}

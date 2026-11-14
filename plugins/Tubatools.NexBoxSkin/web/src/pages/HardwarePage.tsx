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
import {
  Activity,
  Cpu,
  Gauge,
  Gamepad2,
  HardDrive,
  MemoryStick,
  Monitor,
  Network,
  RefreshCw,
} from "lucide-react";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import {
  formatGb,
  formatNumber,
  gamebox,
  type HardwareSection,
  type SensorSample,
} from "@/lib/gamebox";

function Metric({
  label,
  value,
  detail,
  icon,
  color,
}: {
  label: string;
  value: string;
  detail: string;
  icon: React.ReactNode;
  color: string;
}) {
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  return (
    <LiquidGlassCard p={4}>
      <HStack justify="space-between" align="flex-start">
        <VStack align="start" spacing={1}>
          <Text fontSize="sm" color={sub}>
            {label}
          </Text>
          <Text fontSize="xl" fontWeight="bold">
            {value}
          </Text>
          <Text fontSize="xs" color={sub}>
            {detail}
          </Text>
        </VStack>
        <Box color={color}>{icon}</Box>
      </HStack>
    </LiquidGlassCard>
  );
}

export default function HardwarePage() {
  const adaptive = useAdaptiveTextColor();
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  const border = useColorModeValue("gray.200", "#333333");
  const [sensors, setSensors] = useState<SensorSample | null>(null);
  const [sections, setSections] = useState<HardwareSection[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  useEffect(() => {
    let alive = true;
    let timer = 0;
    const tick = async () => {
      if (document.hidden) return;
      try {
        const sample = await gamebox.sensors();
        if (alive) setSensors(sample);
      } catch {
        /* ignore */
      }
    };
    void tick();
    timer = window.setInterval(tick, 2000);
    return () => {
      alive = false;
      window.clearInterval(timer);
    };
  }, []);

  const loadHardware = async () => {
    setRefreshing(true);
    try {
      const raw = await gamebox.hardware();
      const list = Array.isArray(raw) ? raw : ((raw as any)?.data as HardwareSection[]) ?? [];
      setSections(list);
    } finally {
      setRefreshing(false);
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadHardware();
  }, []);

  const detailSections = useMemo(
    () => sections.filter((section) => section.title !== "概览" && (section.items?.length ?? 0) > 0),
    [sections],
  );

  return (
    <Box pt={6} pb={10} px={{ base: 2, md: 4 }}>
      <HStack justify="space-between" align="flex-end" mb={4} wrap="wrap">
        <Box>
          <Heading size="lg" color={adaptive.text} textShadow={adaptive.shadow}>
            硬件监控
          </Heading>
          <Text mt={1} fontSize="sm" color={adaptive.text} opacity={0.78} textShadow={adaptive.shadow}>
            实时传感器与完整设备信息
          </Text>
        </Box>
        <HStack>
          <Button
            size="sm"
            variant="outline"
            color="white"
            bg="rgba(0,0,0,0.38)"
            borderColor="whiteAlpha.300"
            backdropFilter="blur(10px)"
            leftIcon={<RefreshCw size={14} />}
            isLoading={refreshing}
            onClick={() => void loadHardware()}
          >
            刷新
          </Button>
        </HStack>
      </HStack>

      <SimpleGrid columns={{ base: 1, md: 2, xl: 3 }} spacing={3} mb={6}>
        <Metric
          label="CPU"
          value={`${formatNumber(sensors?.cpuLoad, 0)}%`}
          detail={`${formatNumber(sensors?.cpuTemp, 1)}°C · ${formatNumber(sensors?.cpuClock, 0)} MHz · ${formatNumber(sensors?.cpuPower, 1)} W`}
          icon={<Cpu size={22} />}
          color="#98DDD0"
        />
        <Metric
          label="GPU"
          value={`${formatNumber(sensors?.gpuLoad, 0)}%`}
          detail={`${formatNumber(sensors?.gpuTemp, 1)}°C · ${formatNumber(sensors?.gpuClock, 0)} MHz · ${formatNumber(sensors?.gpuPower, 1)} W`}
          icon={<Gamepad2 size={22} />}
          color="#7C9EFF"
        />
        <Metric
          label="显存"
          value={formatGb(sensors?.gpuVramUsedGB)}
          detail={`${formatNumber(sensors?.gpuVramLoad, 0)}% · ${sensors?.gpuName ?? ""}`}
          icon={<Monitor size={22} />}
          color="#A78BFA"
        />
        <Metric
          label="内存"
          value={formatGb(sensors?.memUsedGB)}
          detail={`共 ${formatGb(sensors?.memTotalGB)} · ${formatNumber(sensors?.memLoad, 0)}%`}
          icon={<MemoryStick size={22} />}
          color="#FFB86B"
        />
        <Metric
          label="磁盘"
          value={`${formatNumber(sensors?.diskReadMBs, 1)} / ${formatNumber(sensors?.diskWriteMBs, 1)} MB/s`}
          detail={`温度 ${formatNumber(sensors?.diskTemp, 1)}°C`}
          icon={<HardDrive size={22} />}
          color="#4ADE80"
        />
        <Metric
          label="网络 / FPS"
          value={`${formatNumber(sensors?.netDownMBs, 2)} / ${formatNumber(sensors?.netUpMBs, 2)} MB/s`}
          detail={`FPS ${formatNumber(sensors?.fps, 0)} · ${sensors?.fpsProcess ?? ""}`}
          icon={<Network size={22} />}
          color="#38BDF8"
        />
      </SimpleGrid>

      <HStack justify="space-between" mb={3}>
        <Heading size="sm" color={adaptive.text} textShadow={adaptive.shadow}>
          设备信息
        </Heading>
        <Badge colorScheme="teal" variant="subtle" borderRadius="full">
          {detailSections.length} 个分区
        </Badge>
      </HStack>

      {loading ? (
        <HStack justify="center" py={12}>
          <Spinner size="md" />
          <Text fontSize="sm" color={sub}>
            正在读取硬件信息…
          </Text>
        </HStack>
      ) : detailSections.length === 0 ? (
        <LiquidGlassCard p={8}>
          <HStack spacing={3}>
            <Activity size={18} />
            <Text fontSize="sm" color={sub}>
              没有读取到硬件分区，请确认宿主硬件服务可用。
            </Text>
          </HStack>
        </LiquidGlassCard>
      ) : (
        <SimpleGrid minChildWidth="300px" spacing={3} alignItems="start">
          {detailSections.map((section) => (
            <LiquidGlassCard key={section.title} p={4}>
              <VStack align="stretch" spacing={2}>
                <HStack justify="space-between">
                  <Text fontWeight="semibold">{section.title}</Text>
                  <Gauge size={16} />
                </HStack>
                <VStack align="stretch" spacing={1.5} pt={1} borderTop="1px solid" borderColor={border}>
                  {section.items.map((item, index) => (
                    <HStack key={`${section.title}-${item.label}-${index}`} spacing={3} align="flex-start">
                      <Text fontSize="xs" color={sub} minW="72px" flexShrink={0}>
                        {item.label}
                      </Text>
                      <Text fontSize="sm" flex={1} wordBreak="break-word">
                        {item.value}
                      </Text>
                    </HStack>
                  ))}
                </VStack>
              </VStack>
            </LiquidGlassCard>
          ))}
        </SimpleGrid>
      )}
    </Box>
  );
}

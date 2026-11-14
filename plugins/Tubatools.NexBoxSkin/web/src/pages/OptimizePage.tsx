import {
  Box,
  Button,
  HStack,
  Heading,
  SimpleGrid,
  Spinner,
  Text,
  VStack,
  useColorModeValue,
  useToast,
} from "@chakra-ui/react";
import { useEffect, useMemo, useState } from "react";
import {
  Cpu,
  Download,
  Gauge,
  HardDrive,
  List,
  MemoryStick,
  Network,
  Play,
  Settings,
  Shield,
  Trash2,
  Zap,
} from "lucide-react";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { gamebox, type ToolInfo } from "@/lib/gamebox";

interface OptimizeItem {
  id: string;
  title: string;
  description: string;
  color: string;
  icon: React.ComponentType<{ size?: number }>;
}

const ITEMS: OptimizeItem[] = [
  { id: "space-cleanup", title: "空间清理", description: "扫描磁盘占用、定位大文件并清理垃圾", color: "#38A169", icon: HardDrive },
  { id: "memory-manager", title: "内存管理", description: "查看内存占用、释放工作集与整理内存", color: "#FF6B9D", icon: MemoryStick },
  { id: "startup-manager", title: "启动项管理", description: "管理开机自启项，减少后台占用", color: "#805AD5", icon: List },
  { id: "context-menu-mgr", title: "右键菜单管理", description: "清理和整理资源管理器右键菜单", color: "#3182CE", icon: Settings },
  { id: "driver-store", title: "驱动管理", description: "浏览 DriverStore、清理旧驱动与备份还原", color: "#DD6B20", icon: Cpu },
  { id: "runtime-repair", title: "运行库修复", description: "修复 VC++、DirectX 等游戏运行环境", color: "#E53E3E", icon: Download },
  { id: "dotnet-completion", title: ".NET 环境补全", description: "检测并补全 .NET 运行环境", color: "#8B5CF6", icon: Download },
  { id: "network-optimize", title: "网络优化", description: "DNS、TCP 参数与网络适配器优化", color: "#38A169", icon: Network },
  { id: "speed-test", title: "网速测试", description: "测试带宽、延迟与网络质量", color: "#0EA5E9", icon: Gauge },
  { id: "traffic-monitor", title: "流量监控", description: "实时查看各程序网络流量", color: "#06B6D4", icon: Network },
  { id: "hosts-editor", title: "Hosts 编辑", description: "编辑和备份 hosts 规则", color: "#64748B", icon: Shield },
  { id: "windows-feature", title: "Windows 隐藏功能", description: "启用系统隐藏设置与实用功能", color: "#F6AD55", icon: Zap },
  { id: "time-sync", title: "时间同步", description: "测试 NTP 服务器并校准系统时间", color: "#38B2AC", icon: Settings },
  { id: "energy-star", title: "后台节流省电", description: "降低后台程序资源占用", color: "#48BB78", icon: Zap },
  { id: "disk-health", title: "磁盘健康", description: "查看 SMART、温度与健康状态", color: "#E53E3E", icon: HardDrive },
  { id: "quick-device-check", title: "快速验机", description: "快速核对 CPU、显卡、内存等配置", color: "#ED8936", icon: Shield },
  { id: "stress-test", title: "CPU / GPU 烤机", description: "压力测试稳定性与散热表现", color: "#9F7AEA", icon: Gauge },
  { id: "performance-benchmark", title: "性能测试", description: "跑分并对比硬件性能", color: "#4299E1", icon: Gauge },
];

export default function OptimizePage() {
  const toast = useToast();
  const adaptive = useAdaptiveTextColor();
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  const [tools, setTools] = useState<ToolInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [runningId, setRunningId] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const list = await gamebox.listTools();
        if (alive) setTools(list ?? []);
      } finally {
        if (alive) setLoading(false);
      }
    })();
    return () => {
      alive = false;
    };
  }, []);

  const available = useMemo(() => {
    const ids = new Set(tools.map((tool) => tool.id));
    return ITEMS.filter((item) => ids.has(item.id));
  }, [tools]);

  const run = async (item: OptimizeItem) => {
    if (runningId) return;
    setRunningId(item.id);
    try {
      await gamebox.runTool(item.id);
      toast({ title: `已启动：${item.title}`, status: "success", duration: 1800 });
    } catch (error) {
      toast({
        title: `${item.title} 启动失败`,
        description: String(error),
        status: "error",
        duration: 4000,
        isClosable: true,
      });
    } finally {
      setRunningId(null);
    }
  };

  return (
    <Box pt={6} pb={10} px={{ base: 2, md: 4 }}>
      <HStack justify="space-between" align="flex-end" mb={4} wrap="wrap">
        <Box>
          <Heading size="lg" color={adaptive.text} textShadow={adaptive.shadow}>
            优化
          </Heading>
          <Text mt={1} fontSize="sm" color={adaptive.text} opacity={0.78} textShadow={adaptive.shadow}>
            常用系统优化与维护工具，点击即可由宿主启动
          </Text>
        </Box>
        <Button
          size="sm"
          variant="outline"
          color="white"
          bg="rgba(0,0,0,0.38)"
          borderColor="whiteAlpha.300"
          backdropFilter="blur(10px)"
          leftIcon={<Trash2 size={14} />}
          onClick={() => void run(ITEMS[0])}
        >
          一键打开空间清理
        </Button>
      </HStack>

      {loading ? (
        <HStack justify="center" py={16}>
          <Spinner size="md" />
          <Text fontSize="sm" color={sub}>
            正在读取工具…
          </Text>
        </HStack>
      ) : (
        <SimpleGrid minChildWidth="260px" spacing={3}>
          {available.map((item) => {
            const Icon = item.icon;
            return (
              <LiquidGlassCard key={item.id} p={4} h="100%">
                <VStack align="stretch" spacing={3} h="100%">
                  <HStack justify="space-between">
                    <Box color={item.color}>
                      <Icon size={24} />
                    </Box>
                    <Button
                      size="xs"
                      colorScheme="teal"
                      variant="ghost"
                      leftIcon={<Play size={12} />}
                      isLoading={runningId === item.id}
                      onClick={() => void run(item)}
                    >
                      打开
                    </Button>
                  </HStack>
                  <Box>
                    <Text fontWeight="semibold">{item.title}</Text>
                    <Text mt={1} fontSize="sm" color={sub} noOfLines={2}>
                      {item.description}
                    </Text>
                  </Box>
                </VStack>
              </LiquidGlassCard>
            );
          })}
        </SimpleGrid>
      )}
    </Box>
  );
}

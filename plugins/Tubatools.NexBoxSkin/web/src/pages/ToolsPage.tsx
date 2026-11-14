import {
  Badge,
  Box,
  Button,
  HStack,
  Heading,
  IconButton,
  Input,
  InputGroup,
  InputLeftElement,
  SimpleGrid,
  Spinner,
  Text,
  VStack,
  useColorModeValue,
  useToast,
} from "@chakra-ui/react";
import { useEffect, useMemo, useState } from "react";
import { Play, Search, Star } from "lucide-react";
import { ToolIcon } from "@/components/ToolIcon";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { gamebox, type ToolInfo } from "@/lib/gamebox";

type Tab = "all" | "builtin" | "external" | "favorite";
const FAVORITE_KEY = "gamebox.favorite.tools";

function readFavorites(): string[] {
  try {
    const raw = localStorage.getItem(FAVORITE_KEY);
    const parsed = raw ? (JSON.parse(raw) as unknown) : [];
    return Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === "string") : [];
  } catch {
    return [];
  }
}

function ToolCard({
  tool,
  favorite,
  running,
  onRun,
  onToggleFavorite,
}: {
  tool: ToolInfo;
  favorite: boolean;
  running: boolean;
  onRun: (tool: ToolInfo) => void;
  onToggleFavorite: (tool: ToolInfo) => void;
}) {
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  const border = useColorModeValue("gray.200", "#333333");
  return (
    <LiquidGlassCard p={4} h="100%">
      <VStack align="stretch" spacing={3} h="100%">
        <HStack justify="space-between" align="flex-start">
          <HStack spacing={3} minW={0}>
            <Box flexShrink={0}>
              <ToolIcon tool={tool} />
            </Box>
            <VStack align="start" spacing={0} minW={0}>
              <Text fontWeight="semibold" noOfLines={1}>
                {tool.name}
              </Text>
              <Badge fontSize="10px" variant="subtle" borderRadius="full" mt={1}>
                {tool.category}
              </Badge>
            </VStack>
          </HStack>
          <IconButton
            aria-label={favorite ? "取消收藏" : "收藏"}
            size="xs"
            variant="ghost"
            color={favorite ? "yellow.400" : sub}
            icon={<Star size={15} fill={favorite ? "currentColor" : "none"} />}
            onClick={(event) => {
              event.stopPropagation();
              onToggleFavorite(tool);
            }}
          />
        </HStack>

        <Text fontSize="sm" color={sub} noOfLines={3} flex={1}>
          {tool.description || "点击启动此工具。"}
        </Text>

        <HStack justify="space-between" pt={1} borderTop="1px solid" borderColor={border}>
          <Text fontSize="xs" color={sub}>
            {tool.isBuiltin ? "内置" : "外部"}
          </Text>
          <Button
            size="xs"
            colorScheme="teal"
            variant="solid"
            leftIcon={<Play size={12} />}
            isLoading={running}
            onClick={() => onRun(tool)}
          >
            启动
          </Button>
        </HStack>
      </VStack>
    </LiquidGlassCard>
  );
}

export default function ToolsPage() {
  const toast = useToast();
  const adaptive = useAdaptiveTextColor();
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  const [tools, setTools] = useState<ToolInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [tab, setTab] = useState<Tab>("all");
  const [category, setCategory] = useState("全部");
  const [query, setQuery] = useState("");
  const [favorites, setFavorites] = useState<string[]>(readFavorites);
  const [runningId, setRunningId] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const list = await gamebox.listTools();
        if (alive) setTools(list ?? []);
      } catch (error) {
        if (alive) {
          toast({
            title: "工具列表加载失败",
            description: String(error),
            status: "error",
            duration: 3000,
          });
        }
      } finally {
        if (alive) setLoading(false);
      }
    })();
    return () => {
      alive = false;
    };
  }, [toast]);

  const categories = useMemo(() => {
    const set = new Set<string>();
    tools.forEach((tool) => set.add(tool.category));
    return ["全部", ...Array.from(set).sort((a, b) => a.localeCompare(b, "zh-CN"))];
  }, [tools]);

  const filtered = useMemo(() => {
    const keyword = query.trim().toLowerCase();
    return tools.filter((tool) => {
      if (tab === "builtin" && !tool.isBuiltin) return false;
      if (tab === "external" && tool.isBuiltin) return false;
      if (tab === "favorite" && !favorites.includes(tool.id)) return false;
      if (category !== "全部" && tool.category !== category) return false;
      if (!keyword) return true;
      return `${tool.name} ${tool.description} ${tool.category}`.toLowerCase().includes(keyword);
    });
  }, [tools, tab, category, query, favorites]);

  const toggleFavorite = (tool: ToolInfo) => {
    setFavorites((previous) => {
      const next = previous.includes(tool.id)
        ? previous.filter((id) => id !== tool.id)
        : [...previous, tool.id];
      try {
        localStorage.setItem(FAVORITE_KEY, JSON.stringify(next));
      } catch {
        /* ignore */
      }
      return next;
    });
  };

  const runTool = async (tool: ToolInfo) => {
    if (runningId) return;
    setRunningId(tool.id);
    try {
      await gamebox.runTool(tool.id);
      toast({ title: `已启动：${tool.name}`, status: "success", duration: 1800 });
    } catch (error) {
      toast({
        title: `${tool.name} 启动失败`,
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
      <HStack justify="space-between" align="flex-end" mb={4} wrap="wrap" spacing={3}>
        <Box>
          <Heading size="lg" color={adaptive.text} textShadow={adaptive.shadow}>
            工具
          </Heading>
          <Text mt={1} fontSize="sm" color={adaptive.text} opacity={0.78} textShadow={adaptive.shadow}>
            图吧工具箱的全部内置工具与外部工具
          </Text>
        </Box>
        <InputGroup maxW="320px" size="sm">
          <InputLeftElement pointerEvents="none">
            <Search size={14} />
          </InputLeftElement>
          <Input
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="搜索工具、分类或说明"
            borderRadius="full"
            bg="rgba(0,0,0,0.42)"
            color="white"
            borderColor="whiteAlpha.300"
            backdropFilter="blur(10px)"
            _placeholder={{ color: "whiteAlpha.600" }}
            _hover={{ borderColor: "whiteAlpha.500" }}
            _focus={{ borderColor: "teal.300", boxShadow: "none" }}
          />
        </InputGroup>
      </HStack>

      <HStack spacing={2} mb={3} wrap="wrap">
        {(
          [
            ["all", "全部"],
            ["builtin", "内置"],
            ["external", "外部"],
            ["favorite", "收藏"],
          ] as Array<[Tab, string]>
        ).map(([value, label]) => (
          <Button
            key={value}
            size="sm"
            borderRadius="full"
            variant={tab === value ? "solid" : "outline"}
            colorScheme={tab === value ? "teal" : undefined}
            color={tab === value ? "white" : adaptive.text}
            borderColor={tab === value ? undefined : "whiteAlpha.400"}
            onClick={() => setTab(value)}
          >
            {label}
          </Button>
        ))}
      </HStack>

      <HStack spacing={2} mb={4} wrap="wrap">
        {categories.map((item) => (
          <Button
            key={item}
            size="xs"
            borderRadius="full"
            variant={category === item ? "subtle" : "ghost"}
            colorScheme={category === item ? "teal" : undefined}
            color={category === item ? "white" : adaptive.text}
            textShadow={category === item ? undefined : adaptive.shadow}
            onClick={() => setCategory(item)}
          >
            {item}
          </Button>
        ))}
      </HStack>

      {loading ? (
        <HStack justify="center" py={16}>
          <Spinner size="md" />
          <Text fontSize="sm" color={sub}>
            正在读取工具列表…
          </Text>
        </HStack>
      ) : filtered.length === 0 ? (
        <LiquidGlassCard p={10}>
          <VStack spacing={2}>
            <Text fontWeight="semibold">没有匹配的工具</Text>
            <Text fontSize="sm" color={sub}>
              换个关键词或分类试试。
            </Text>
          </VStack>
        </LiquidGlassCard>
      ) : (
        <SimpleGrid minChildWidth="260px" spacing={3}>
          {filtered.map((tool) => (
            <ToolCard
              key={tool.id}
              tool={tool}
              favorite={favorites.includes(tool.id)}
              running={runningId === tool.id}
              onRun={runTool}
              onToggleFavorite={toggleFavorite}
            />
          ))}
        </SimpleGrid>
      )}
    </Box>
  );
}

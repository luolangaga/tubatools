import { useEffect, useMemo, useState } from "react";
import {
  Box,
  Text,
  VStack,
  HStack,
  SimpleGrid,
  useColorModeValue,
  Button,
  Spinner,
  Badge,
} from "@chakra-ui/react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { RefreshCw, ChevronRight, Gamepad2 } from "lucide-react";
import { PageBackButton } from "@/components/special/page-back-button";
import { PageHeader } from "@/components/special/page-header";
import { Surface } from "@/components/df/surface";
import { useBackground } from "@/contexts/background-context";
import { useThemeColor } from "@/contexts/theme-color-context";
import {
  loadStreamerLibrary,
  streamerAsset,
  filledFieldCount,
  totalFieldCount,
  type StreamerEntry,
} from "@/lib/streamer-data";
import championLogo from "@/assets/champion.webp";

function StreamerCard({
  entry,
  order,
  onClick,
}: {
  entry: StreamerEntry;
  order: number;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  const { liquidGlassEnabled } = useBackground();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const textColor = useColorModeValue("#000000", "#ffffff");
  const subTextColor = useColorModeValue("gray.600", "#a0a0a0");
  const hoverBg = useColorModeValue("gray.100", "#1c1c1c");
  const avatarBg = useColorModeValue("gray.100", "#1f1f1f");

  const filled = filledFieldCount(entry);
  const total = totalFieldCount(entry);
  const tags = (entry.tags ?? []).slice(0, 2);
  const meta = [entry.game, ...tags].filter(Boolean).join(" · ");

  return (
    <Surface
      glass={liquidGlassEnabled}
      px={4}
      py={4}
      cursor="pointer"
      transition="background-color 0.2s"
      _hover={liquidGlassEnabled ? undefined : { bg: hoverBg }}
      onClick={onClick}
    >
      <HStack spacing={3} align="center">
        <Box position="relative" flexShrink={0}>
          <Box
            boxSize="50px"
            borderRadius="full"
            overflow="hidden"
            bg={avatarBg}
            border="2px solid"
            borderColor={`${primaryColor}55`}
          >
            {entry.avatar ? (
              <img
                src={streamerAsset(entry.avatar)}
                alt={entry.name}
                style={{ width: "100%", height: "100%", objectFit: "cover" }}
              />
            ) : (
              <Box display="flex" alignItems="center" justifyContent="center" h="100%">
                <Gamepad2 size={22} color={primaryColor} />
              </Box>
            )}
          </Box>
          <Text
            position="absolute"
            bottom="-4px"
            right="-5px"
            fontSize="10px"
            fontWeight="700"
            px={1.5}
            borderRadius="4px"
            bg={primaryColor}
            color="#000"
            lineHeight="16px"
          >
            {String(order).padStart(2, "0")}
          </Text>
        </Box>

        <VStack align="start" spacing={1} flex={1} minW={0}>
          <HStack spacing={2} maxW="100%">
            <Text fontWeight="700" fontSize="md" color={textColor} noOfLines={1}>
              {entry.name}
            </Text>
            <Badge
              fontSize="10px"
              px={1.5}
              py={0}
              borderRadius="full"
              bg={`${primaryColor}22`}
              color={textColor}
              fontWeight="500"
              flexShrink={0}
            >
              {entry.platform}
            </Badge>
          </HStack>
          <Text fontSize="sm" color={subTextColor} noOfLines={1}>
            {meta}
          </Text>
        </VStack>

        <VStack align="end" spacing={0} flexShrink={0}>
          <Text fontSize="sm" fontWeight="600" color={textColor} lineHeight="1.3">
            {filled}
            <Text as="span" fontSize="xs" color={subTextColor} fontWeight="400">
              {" "}
              / {total}
            </Text>
          </Text>
          <Text fontSize="xs" color={subTextColor} lineHeight="1.3">
            {t("streamerSettings.settingsUnit", "项设置")}
          </Text>
        </VStack>

        <ChevronRight size={18} color={subTextColor} style={{ flexShrink: 0 }} />
      </HStack>
    </Surface>
  );
}

export default function StreamerDirectoryPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { liquidGlassEnabled } = useBackground();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const subTextColor = useColorModeValue("gray.600", "#a0a0a0");

  const [entries, setEntries] = useState<StreamerEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let alive = true;
    setError(null);
    setEntries(null);
    loadStreamerLibrary()
      .then((list) => {
        if (alive) setEntries(list);
      })
      .catch((e) => {
        if (alive) setError(String(e?.message ?? e));
      });
    return () => {
      alive = false;
    };
  }, [reloadKey]);

  /** id → 原始序号（01~16），列表被筛选后仍显示真实编号 */
  const orderOf = useMemo(() => {
    const m = new Map<string, number>();
    (entries ?? []).forEach((e, i) => m.set(e.id, i + 1));
    return m;
  }, [entries]);

  return (
    <Box pt={8} pb={8}>
      <PageHeader title={t("streamerSettings.title", "主播设置")} mb={2} />

        <HStack mb={6} align="flex-end" spacing={4}>
        <Text fontSize="sm" color={subTextColor} ml={{ base: 0, md: "92px" }}>
          {t("streamerSettings.directoryDesc", "收录 {{count}} 位主播的实机游戏设置与电脑外设配置", {
            count: entries?.length ?? 16,
          })}
        </Text>
        <Box flex={1} />
        <HStack spacing={1.5} flexShrink={0}>
          <img
            src={championLogo}
            alt=""
            style={{ width: "16px", height: "16px", objectFit: "contain" }}
          />
          <Text fontSize="xs" color={subTextColor}>
            {t("streamerSettings.dataSource", "数据来源：冠军调试")}
          </Text>
        </HStack>
      </HStack>

      {/* 列表：按最小宽度自动铺列，卡片不会被拉宽，列数随窗口自适应 */}
      {error ? (
        <Surface glass={liquidGlassEnabled} p={8} textAlign="center">
          <VStack spacing={3}>
            <Text color={subTextColor} fontSize="sm">
              {t("streamerSettings.loadFailed", "主播数据加载失败")}：{error}
            </Text>
            <Button
              size="sm"
              variant="outline"
              color={primaryColor}
              borderColor={primaryColor}
              leftIcon={<RefreshCw size={14} />}
              onClick={() => setReloadKey((k) => k + 1)}
            >
              {t("streamerSettings.retry", "重试")}
            </Button>
          </VStack>
        </Surface>
      ) : !entries ? (
        <Box py={16} textAlign="center">
          <Spinner color={primaryColor} size="lg" />
          <Text mt={3} fontSize="sm" color={subTextColor}>
            {t("deltaForce.loading", "加载中…")}
          </Text>
        </Box>
      ) : entries.length === 0 ? (
        <Surface glass={liquidGlassEnabled} p={10} textAlign="center">
          <Text fontSize="sm" color={subTextColor}>
            {t("streamerSettings.empty", "没有匹配的主播")}
          </Text>
        </Surface>
      ) : (
        <SimpleGrid minChildWidth="260px" spacing={3}>
          {entries.map((entry) => (
            <StreamerCard
              key={entry.id}
              entry={entry}
              order={orderOf.get(entry.id) ?? 0}
              onClick={() => navigate(`/delta-force/streamers/${entry.id}`)}
            />
          ))}
        </SimpleGrid>
      )}
    </Box>
  );
}

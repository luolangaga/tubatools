"use client";

import { useMemo, useState } from "react";
import {
  Box,
  Text,
  HStack,
  VStack,
  Badge,
  Wrap,
  WrapItem,
  useColorModeValue,
  IconButton,
  SimpleGrid,
} from "@chakra-ui/react";
import { useTranslation } from "react-i18next";
import { invoke } from "@tauri-apps/api/core";
import {
  ChevronLeft,
  ChevronRight,
  Gamepad2,
} from "lucide-react";
import { Surface } from "@/components/df/surface";
import { useThemeColor } from "@/contexts/theme-color-context";
import { useDynamicIsland } from "@/components/ui/dynamic-island";
import {
  streamerAsset,
  EQUIPMENT_CATEGORY_LABEL,
  type Equipment,
  type StreamerEntry,
} from "@/lib/streamer-data";

/** 平台角标：抖音用图标，其它用首字母 */
function PlatformMark({ platform }: { platform: string }) {
  const isDouyin = platform.includes("抖音");
  return (
    <Box
      boxSize="22px"
      borderRadius="full"
      bg="#000"
      border="2px solid"
      borderColor="white"
      display="flex"
      alignItems="center"
      justifyContent="center"
      overflow="hidden"
    >
      {isDouyin ? (
        <img src="/icons/douyin.webp" alt="抖音" style={{ width: "13px", height: "13px" }} />
      ) : (
        <Text fontSize="10px" fontWeight="700" color="#fff" lineHeight="1">
          {platform.slice(0, 1)}
        </Text>
      )}
    </Box>
  );
}

/** 打开外部链接（系统默认浏览器） */
function useOpenExternal() {
  const toast = useDynamicIsland("target");
  const { t } = useTranslation();
  return async (url: string) => {
    try {
      await invoke("open_url_in_system_browser", { url });
    } catch {
      toast({
        title: t("deltaForce.openFailed", "打开失败"),
        status: "error",
        duration: 2000,
        isClosable: true,
      });
    }
  };
}

/** 单个外设卡片 */
function EquipmentTile({ item, compact }: { item: Equipment; compact?: boolean }) {
  const textColor = useColorModeValue("#000000", "#ffffff");
  const subTextColor = useColorModeValue("gray.600", "#a0a0a0");
  const tileBg = useColorModeValue("gray.50", "#181818");
  const borderColor = useColorModeValue("gray.200", "#2c2c2c");
  const openExternal = useOpenExternal();
  const { t } = useTranslation();

  const body = (
    <VStack align="stretch" spacing={2} h="100%">
      <Box
        h={compact ? "72px" : "104px"}
        display="flex"
        alignItems="center"
        justifyContent="center"
        overflow="hidden"
      >
        {item.imageUrl ? (
          <img
            src={streamerAsset(item.imageUrl)}
            alt={item.model}
            style={{ maxWidth: "100%", maxHeight: "100%", objectFit: "contain" }}
          />
        ) : (
          <Gamepad2 size={28} color={subTextColor} />
        )}
      </Box>
      <Box>
        <Text fontSize="xs" color={subTextColor} noOfLines={1}>
          {item.brand}
        </Text>
        <Text fontSize="sm" fontWeight="600" color={textColor} noOfLines={2} lineHeight="1.3">
          {item.model}
        </Text>
      </Box>
      {(item.dpi || item.pollingRate) && (
        <HStack spacing={3} flexWrap="wrap">
          {item.dpi && (
            <Text fontSize="xs" color={textColor}>
              <Text as="span" color={subTextColor}>
                DPI{" "}
              </Text>
              {item.dpi}
            </Text>
          )}
          {item.pollingRate && (
            <Text fontSize="xs" color={textColor}>
              <Text as="span" color={subTextColor}>
                {t("streamerSettings.pollingRate", "回报率")}{" "}
              </Text>
              {item.pollingRate}
            </Text>
          )}
        </HStack>
      )}
    </VStack>
  );

  const box = (
    <Box
      bg={tileBg}
      border="1px solid"
      borderColor={borderColor}
      borderRadius="lg"
      p={3}
      h="100%"
      cursor={item.officialUrl ? "pointer" : "default"}
      transition="border-color 0.2s"
      _hover={item.officialUrl ? { borderColor: "#98DDD0" } : undefined}
      onClick={
        item.officialUrl
          ? () => openExternal(item.officialUrl!)
          : undefined
      }
    >
      {body}
    </Box>
  );

  return box;
}

/** 左侧：主播档案 + 外设 */
export function StreamerProfileCard({
  entry,
  order,
}: {
  entry: StreamerEntry;
  order: number;
}) {
  const { t } = useTranslation();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const textColor = useColorModeValue("#000000", "#ffffff");
  const subTextColor = useColorModeValue("gray.600", "#a0a0a0");
  const labelColor = useColorModeValue("gray.500", "#8a8a8a");
  const borderColor = useColorModeValue("gray.200", "#2c2c2c");

  const profile = entry.profile;
  const equipment = profile?.equipment ?? [];

  // 首个外设（通常是鼠标）单独大卡展示，其余按 2 列分页
  const featured = equipment[0];
  const rest = useMemo(() => equipment.slice(1), [equipment]);
  const PAGE_SIZE = 4;
  const pageCount = Math.max(1, Math.ceil(rest.length / PAGE_SIZE));
  const [page, setPage] = useState(0);
  const pageItems = rest.slice(page * PAGE_SIZE, page * PAGE_SIZE + PAGE_SIZE);

  return (
    <VStack align="stretch" spacing={4}>
      {/* ── 档案卡 ── */}
      <Surface p={5}>
        <VStack align="stretch" spacing={4}>
          <Text fontSize="11px" letterSpacing="0.08em" color={labelColor}>
            {String(order).padStart(2, "0")} / {t("streamerSettings.archive", "主播档案")}
          </Text>

          <VStack spacing={3}>
            <Box position="relative">
              <Box
                boxSize="96px"
                borderRadius="full"
                overflow="hidden"
                border="3px solid"
                borderColor={`${primaryColor}66`}
                bg={useColorModeValue("gray.100", "#1f1f1f")}
              >
                {entry.avatar ? (
                  <img
                    src={streamerAsset(entry.avatar)}
                    alt={entry.name}
                    style={{ width: "100%", height: "100%", objectFit: "cover" }}
                  />
                ) : (
                  <Box display="flex" alignItems="center" justifyContent="center" h="100%">
                    <Gamepad2 size={36} color={primaryColor} />
                  </Box>
                )}
              </Box>
              <Box position="absolute" bottom="2px" right="2px">
                <PlatformMark platform={entry.platform} />
              </Box>
            </Box>

            <VStack spacing={1}>
              <Text fontSize="2xl" fontWeight="800" color={textColor} lineHeight="1.2">
                {entry.name}
              </Text>
              <Text fontSize="sm" color={subTextColor}>
                {entry.game}
              </Text>
            </VStack>

            <Wrap spacing={2} justify="center">
              <WrapItem>
                <Badge
                  fontSize="10px"
                  px={2.5}
                  py={0.5}
                  borderRadius="full"
                  bg={useColorModeValue("gray.100", "#242424")}
                  color={subTextColor}
                  fontWeight="500"
                >
                  {entry.platform}
                </Badge>
              </WrapItem>
              {(entry.tags ?? []).map((tag) => (
                <WrapItem key={tag}>
                  <Badge
                    fontSize="10px"
                    px={2.5}
                    py={0.5}
                    borderRadius="full"
                    bg={useColorModeValue("gray.100", "#242424")}
                    color={subTextColor}
                    fontWeight="500"
                  >
                    {tag}
                  </Badge>
                </WrapItem>
              ))}
            </Wrap>
          </VStack>

          {profile?.updated && (
            <Text fontSize="10px" color={labelColor} textAlign="center">
              {t("streamerSettings.updatedAt", "档案更新于 {{date}}", { date: profile.updated })}
            </Text>
          )}
        </VStack>
      </Surface>

      {/* ── 外设 ── */}
      {equipment.length > 0 && (
        <Surface p={4}>
          <VStack align="stretch" spacing={3}>
            <HStack justify="space-between">
              <Text fontSize="xs" color={labelColor} letterSpacing="0.05em">
                {t("streamerSettings.gear", "外设装备")}
              </Text>
              <Text fontSize="10px" color={labelColor}>
                {equipment.length} {t("streamerSettings.items", "件")}
              </Text>
            </HStack>

            {featured && (
              <Box
                bg={useColorModeValue("gray.50", "#181818")}
                border="1px solid"
                borderColor={borderColor}
                borderRadius="lg"
                p={3}
              >
                <HStack spacing={3} align="center">
                  <Box
                    boxSize="72px"
                    flexShrink={0}
                    display="flex"
                    alignItems="center"
                    justifyContent="center"
                    overflow="hidden"
                  >
                    {featured.imageUrl && (
                      <img
                        src={streamerAsset(featured.imageUrl)}
                        alt={featured.model}
                        style={{ maxWidth: "100%", maxHeight: "100%", objectFit: "contain" }}
                      />
                    )}
                  </Box>
                  <Box flex={1} minW={0}>
                    <Text fontSize="xs" color={subTextColor}>
                      {featured.brand}
                    </Text>
                    <Text fontSize="sm" fontWeight="700" color={textColor} noOfLines={2}>
                      {featured.model}
                    </Text>
                    <HStack spacing={3} mt={1} flexWrap="wrap">
                      {featured.dpi && (
                        <Text fontSize="xs" color={textColor}>
                          <Text as="span" color={subTextColor}>
                            DPI{" "}
                          </Text>
                          {featured.dpi}
                        </Text>
                      )}
                      {featured.pollingRate && (
                        <Text fontSize="xs" color={textColor}>
                          <Text as="span" color={subTextColor}>
                            {t("streamerSettings.pollingRate", "回报率")}{" "}
                          </Text>
                          {featured.pollingRate}
                        </Text>
                      )}
                    </HStack>
                  </Box>
                </HStack>
              </Box>
            )}

            {rest.length > 0 && (
              <SimpleGrid columns={2} spacing={2}>
                {pageItems.map((item) => (
                  <EquipmentTile key={`${item.id}-${item.model}`} item={item} compact />
                ))}
              </SimpleGrid>
            )}

            {pageCount > 1 && (
              <HStack justify="center" spacing={2}>
                <IconButton
                  aria-label="prev"
                  size="xs"
                  variant="ghost"
                  color={subTextColor}
                  icon={<ChevronLeft size={15} />}
                  isDisabled={page === 0}
                  onClick={() => setPage((p) => Math.max(0, p - 1))}
                />
                <Text fontSize="xs" color={subTextColor}>
                  {page + 1} / {pageCount}
                </Text>
                <IconButton
                  aria-label="next"
                  size="xs"
                  variant="ghost"
                  color={subTextColor}
                  icon={<ChevronRight size={15} />}
                  isDisabled={page >= pageCount - 1}
                  onClick={() => setPage((p) => Math.min(pageCount - 1, p + 1))}
                />
              </HStack>
            )}
          </VStack>
        </Surface>
      )}
    </VStack>
  );
}

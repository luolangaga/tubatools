import { useEffect, useMemo, useState } from "react";
import {
  Box,
  Text,
  VStack,
  HStack,
  Flex,
  IconButton,
  Spinner,
  useColorModeValue,
  Tooltip,
} from "@chakra-ui/react";
import { useTranslation } from "react-i18next";
import { useParams } from "react-router-dom";
import {
  ChevronDown,
  Gamepad2,
  SlidersHorizontal,
  AppWindow,
  Monitor,
  Crosshair,
  Headphones,
  Terminal,
  Cpu,
  SquarePlay,
  MousePointer2,
  Hand,
  AudioWaveform,
  Copy,
  Check,
  CircleDot,
} from "lucide-react";
import { PageBackButton } from "@/components/special/page-back-button";
import { PageHeader } from "@/components/special/page-header";
import { Surface } from "@/components/df/surface";
import { StreamerProfileCard } from "@/components/df/StreamerProfileCard";
import { useThemeColor } from "@/contexts/theme-color-context";
import {
  loadStreamerLibrary,
  displayValue,
  fieldKind,
  showChevron,
  sliderPercent,
  isSecondaryOnly,
  hasValue,
  gameSubTabOf,
  groupsOf,
  tabsOf,
  gameSubTabsOf,
  type GameSubTab,
  type SettingField,
  type SettingGroup,
  type StreamerEntry,
  type StreamerTab,
} from "@/lib/streamer-data";

// ── 图标 ──

const TAB_ICON: Record<StreamerTab, React.ReactNode> = {
  game: <Gamepad2 size={15} />,
  graphics: <SquarePlay size={15} />,
  nvidia: <SlidersHorizontal size={15} />,
  "nvidia-app": <AppWindow size={15} />,
  monitor: <Monitor size={15} />,
  "gun-codes": <Crosshair size={15} />,
  "audio-eq": <Headphones size={15} />,
  steam: <Terminal size={15} />,
  gear: <Cpu size={15} />,
};

const TAB_LABEL_KEY: Record<StreamerTab, string> = {
  game: "游戏设置",
  graphics: "视频",
  nvidia: "显卡控制面板设置",
  "nvidia-app": "N卡APP设置",
  monitor: "显示器设置",
  "gun-codes": "改枪码",
  "audio-eq": "音频设置",
  steam: "Steam 启动项设置",
  gear: "电脑与外设",
};

const SUB_ICON: Record<GameSubTab, React.ReactNode> = {
  video: <SquarePlay size={15} />,
  sensitivity: <MousePointer2 size={15} />,
  combat: <Crosshair size={15} />,
  interface: <Hand size={15} />,
  audio: <AudioWaveform size={15} />,
  other: <CircleDot size={15} />,
};

const SUB_LABEL_KEY: Record<GameSubTab, string> = {
  video: "视频",
  sensitivity: "鼠标灵敏度",
  combat: "操控&战斗",
  interface: "界面&交互",
  audio: "音频",
  other: "其他",
};

// ── 值控件 ──

function ValueControl({ field, group }: { field: SettingField; group: SettingGroup }) {
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const textColor = useColorModeValue("#000000", "#ffffff");
  const mutedColor = useColorModeValue("gray.500", "#8a8a8a");
  const boxBg = useColorModeValue("gray.50", "#161616");
  const boxBorder = useColorModeValue("gray.200", "#333333");
  const trackBg = useColorModeValue("gray.200", "#3a3a3a");

  const kind = fieldKind(field);
  const value = displayValue(field);
  const muted = isSecondaryOnly(field); // 典型：未绑定的按键

  if (kind === "switch") {
    const on = ["开", "开启", "启用", "勾选", "已勾选"].includes(value);
    return (
      <HStack justify="flex-end" spacing={2}>
        <Text fontSize="xs" color={mutedColor}>
          {value}
        </Text>
        <Box
          w="34px"
          h="18px"
          borderRadius="full"
          bg={on ? primaryColor : trackBg}
          position="relative"
          transition="background-color 0.2s"
          flexShrink={0}
        >
          <Box
            position="absolute"
            top="2px"
            left={on ? "18px" : "2px"}
            boxSize="14px"
            borderRadius="full"
            bg={on ? "#000" : "#ffffff"}
            transition="left 0.2s"
            boxShadow="0 1px 2px rgba(0,0,0,0.35)"
          />
        </Box>
      </HStack>
    );
  }

  if (kind === "slider") {
    const pct = sliderPercent(field);
    return (
      <HStack spacing={3} justify="flex-end">
        <Box flex={1} h="5px" borderRadius="full" bg={trackBg} position="relative" overflow="hidden">
          <Box
            position="absolute"
            left={0}
            top={0}
            h="100%"
            w={`${pct}%`}
            bg={primaryColor}
            borderRadius="full"
          />
        </Box>
        <Text fontSize="xs" fontWeight="600" color={textColor} minW="52px" textAlign="right">
          {value}
        </Text>
      </HStack>
    );
  }

  return (
    <Box
      border="1px solid"
      borderColor={boxBorder}
      borderRadius="md"
      bg={boxBg}
      px={3}
      py={1.5}
      display="flex"
      alignItems="center"
      justifyContent="space-between"
      gap={2}
      minH="30px"
    >
      <Text
        fontSize="sm"
        color={muted ? mutedColor : textColor}
        fontStyle={muted ? "italic" : "normal"}
        noOfLines={1}
        wordBreak="break-all"
      >
        {value || "—"}
      </Text>
      {showChevron(field, group) && !muted && (
        <ChevronDown size={14} color={mutedColor} style={{ flexShrink: 0 }} />
      )}
    </Box>
  );
}

// ── 设置行 ──

function SettingRow({ field, group }: { field: SettingField; group: SettingGroup }) {
  const textColor = useColorModeValue("#000000", "#ffffff");
  const subTextColor = useColorModeValue("gray.500", "#8a8a8a");
  const rowBorder = useColorModeValue("gray.100", "#242424");
  const rowHover = useColorModeValue("gray.50", "#161616");
  const chipBg = useColorModeValue("gray.100", "#262626");

  return (
    <HStack
      px={{ base: 3, md: 4 }}
      py={2.5}
      spacing={4}
      align="center"
      borderBottom="1px solid"
      borderColor={rowBorder}
      _hover={{ bg: rowHover }}
    >
      <Box flex="1" minW={0}>
        <HStack spacing={2} align="center">
          <Text fontSize="sm" color={textColor} noOfLines={2}>
            {field.label}
          </Text>
          {field.behavior && field.behavior !== "—" && (
            <Text
              fontSize="10px"
              px={1.5}
              py="1px"
              borderRadius="4px"
              bg={chipBg}
              color={subTextColor}
              flexShrink={0}
            >
              {field.behavior}
            </Text>
          )}
        </HStack>
        {field.note && (
          <Text fontSize="xs" color={subTextColor} mt={0.5} noOfLines={2}>
            {field.note}
          </Text>
        )}
      </Box>
      <Box w={{ base: "46%", md: "340px" }} flexShrink={0}>
        <ValueControl field={field} group={group} />
      </Box>
    </HStack>
  );
}

// ── 分组卡 ──

function GroupCard({ group, fields }: { group: SettingGroup; fields: SettingField[] }) {
  const textColor = useColorModeValue("#000000", "#ffffff");
  const countColor = useColorModeValue("gray.500", "#8a8a8a");
  const cardBorder = useColorModeValue("gray.200", "#2c2c2c");

  if (fields.length === 0) return null;

  // 标题栏与内容区同底：不加独立背景/色条，玻璃感由外层面板统一提供
  return (
    <Box border="1px solid" borderColor={cardBorder} borderRadius="3xl" overflow="hidden">
      <HStack px={{ base: 3, md: 4 }} py={2.5} spacing={2}>
        <Text fontSize="sm" fontWeight="700" color={textColor}>
          {group.title}
        </Text>
        <Text fontSize="xs" color={countColor}>
          {fields.filter(hasValue).length}
        </Text>
      </HStack>
      <Box>
        {fields.map((f) => (
          <SettingRow key={f.id} field={f} group={group} />
        ))}
      </Box>
    </Box>
  );
}

// ── 改枪码卡 ──

function GunCodeCard({ field }: { field: SettingField }) {
  const { t } = useTranslation();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const textColor = useColorModeValue("#000000", "#ffffff");
  const subTextColor = useColorModeValue("gray.500", "#8a8a8a");
  const boxBg = useColorModeValue("gray.50", "#161616");
  const boxBorder = useColorModeValue("gray.200", "#2c2c2c");
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(field.value ?? "");
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      /* ignore */
    }
  };

  return (
    <Box border="1px solid" borderColor={boxBorder} borderRadius="lg" p={3} bg={boxBg}>
      <HStack justify="space-between" align="flex-start" spacing={3}>
        <Box flex={1} minW={0}>
          <Text fontSize="sm" fontWeight="600" color={textColor}>
            {field.label}
          </Text>
          {field.note && (
            <Text fontSize="xs" color={subTextColor} mt={0.5}>
              {field.note}
            </Text>
          )}
          <Text
            fontSize="xs"
            mt={1.5}
            color={primaryColor}
            wordBreak="break-all"
            userSelect="all"
          >
            {field.value}
          </Text>
        </Box>
        <Tooltip label={t("deltaForce.copy", "复制")} hasArrow>
          <IconButton
            aria-label="copy"
            size="sm"
            variant="ghost"
            color={copied ? primaryColor : subTextColor}
            icon={copied ? <Check size={15} /> : <Copy size={15} />}
            onClick={copy}
          />
        </Tooltip>
      </HStack>
    </Box>
  );
}

// ── 标签按钮 ──

function TabButton({
  active,
  icon,
  label,
  onClick,
}: {
  active: boolean;
  icon: React.ReactNode;
  label: string;
  onClick: () => void;
}) {
  const { getActiveColor, getContrastTextColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const contrastText = getContrastTextColor();
  const textColor = useColorModeValue("#000000", "#ffffff");
  const idleColor = useColorModeValue("gray.500", "#9a9a9a");

  return (
    <HStack
      as="button"
      spacing={2}
      px={3}
      py={2}
      borderRadius="lg"
      bg={active ? primaryColor : "transparent"}
      color={active ? contrastText : idleColor}
      _hover={active ? { filter: "brightness(0.88)" } : { color: textColor }}
      transition="background-color 0.15s, color 0.15s, filter 0.15s"
      onClick={onClick}
      whiteSpace="nowrap"
      flexShrink={0}
    >
      {icon}
      <Text fontSize="sm" fontWeight={active ? "700" : "500"}>
        {label}
      </Text>
    </HStack>
  );
}

/** 主题色开关（显示未填写项） */
function TogglePill({
  checked,
  onChange,
  label,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  label: string;
}) {
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const subTextColor = useColorModeValue("gray.500", "#8a8a8a");
  const trackBg = useColorModeValue("gray.200", "#3a3a3a");

  return (
    <HStack
      as="button"
      spacing={2}
      cursor="pointer"
      onClick={() => onChange(!checked)}
      title={label}
    >
      <Text fontSize="xs" color={subTextColor}>
        {label}
      </Text>
      <Box
        w="34px"
        h="18px"
        borderRadius="full"
        bg={checked ? primaryColor : trackBg}
        position="relative"
        transition="background-color 0.2s"
        flexShrink={0}
      >
        <Box
          position="absolute"
          top="2px"
          left={checked ? "18px" : "2px"}
          boxSize="14px"
          borderRadius="full"
          bg={checked ? "#000" : "#ffffff"}
          transition="left 0.2s"
          boxShadow="0 1px 2px rgba(0,0,0,0.35)"
        />
      </Box>
    </HStack>
  );
}

// ── 主页面 ──

export default function StreamerProfilePage() {
  const { t } = useTranslation();
  const { id } = useParams<{ id: string }>();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const subTextColor = useColorModeValue("gray.500", "#8a8a8a");
  const borderColor = useColorModeValue("gray.200", "#2c2c2c");

  const [entries, setEntries] = useState<StreamerEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<StreamerTab | null>(null);
  const [activeSub, setActiveSub] = useState<GameSubTab | null>(null);
  const [showEmpty, setShowEmpty] = useState(false);

  useEffect(() => {
    let alive = true;
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
  }, []);

  const index = useMemo(
    () => (entries ?? []).findIndex((e) => e.id === id),
    [entries, id]
  );
  const entry = index >= 0 ? (entries ?? [])[index] : null;

  const tabs = useMemo(() => (entry ? tabsOf(entry) : []), [entry]);
  const subTabs = useMemo(() => (entry ? gameSubTabsOf(entry) : []), [entry]);

  // 默认选中第一个分区 / 第一个二级标签
  useEffect(() => {
    if (!tabs.length) return;
    if (!activeTab || !tabs.includes(activeTab)) setActiveTab(tabs[0]);
  }, [tabs, activeTab]);

  useEffect(() => {
    if (!subTabs.length) return;
    if (!activeSub || !subTabs.includes(activeSub)) setActiveSub(subTabs[0]);
  }, [subTabs, activeSub]);

  /** 当前分区下的分组（已按二级标签过滤） */
  const visibleGroups = useMemo<{ group: SettingGroup; fields: SettingField[] }[]>(() => {
    if (!entry || !activeTab) return [];
    let groups = groupsOf(entry, activeTab);

    if (activeTab === "game" && activeSub) {
      groups = groups.filter((g) => gameSubTabOf(g, entry.game) === activeSub);
    }

    return groups
      .map((g) => ({
        group: g,
        fields: g.fields.filter((f) => showEmpty || hasValue(f)),
      }))
      .filter((x) => x.fields.length > 0);
  }, [entry, activeTab, activeSub, showEmpty]);

  const totalVisible = visibleGroups.reduce((n, x) => n + x.fields.length, 0);

  // ── 渲染 ──

  if (error) {
    return (
      <Box pt={8} pb={8}>
        <PageBackButton size="sm" />
        <Surface p={8} mt={4} textAlign="center">
          <Text fontSize="sm" color={subTextColor}>
            {t("streamerSettings.loadFailed", "主播数据加载失败")}：{error}
          </Text>
        </Surface>
      </Box>
    );
  }

  if (!entries) {
    return (
      <Box pt={16} pb={16} textAlign="center">
        <Spinner color={primaryColor} size="lg" />
      </Box>
    );
  }

  if (!entry) {
    return (
      <Box pt={8} pb={8}>
        <PageBackButton size="sm" />
        <Surface p={10} mt={4} textAlign="center">
          <Text fontSize="sm" color={subTextColor}>
            {t("streamerSettings.notFound", "没有找到这位主播")}
          </Text>
        </Surface>
      </Box>
    );
  }

  const isGunCodes = activeTab === "gun-codes";

  return (
    <Box pt={8} pb={8}>
      <PageHeader
        title={
          <HStack spacing={2} justify="center">
            <Text as="span">{entry.name}</Text>
            <Text as="span" fontSize="sm" fontWeight="400" opacity={0.65}>
              {entry.game}
            </Text>
          </HStack>
        }
        mb={6}
      />

      {/* 左右排版：左=主播个人信息 / 外设，右=设置面板。
          断点用 md 而不是 xl —— 主窗口默认 1230px，用 xl(1280) 会退化成上下堆叠。 */}
      <Flex direction={{ base: "column", md: "row" }} gap={6} align="flex-start">
        {/* 左：主播档案。宽度断点必须与上面 Flex 的 direction 断点一致（都用 md），
            否则小窗口下这里会退化成 100% 宽，把右侧设置面板挤没 */}
        <Box w={{ base: "100%", md: "300px" }} flexShrink={0}>
          <StreamerProfileCard entry={entry} order={index + 1} />
        </Box>

        {/* 右：设置面板（flex=1 吃满剩余宽度，不要写 w="100%" 否则 row 下会撑爆） */}
        <Box flex={1} minW={0} w={{ base: "100%", md: "auto" }}>
          <Surface p={0} overflow="hidden" data-backdrop-filter="">
            {/* 顶层分区 */}
            <HStack
              px={3}
              pt={2}
              pb={0}
              spacing={1}
              overflowX="auto"
              borderBottom="1px solid"
              borderColor={borderColor}
              sx={{
                "&::-webkit-scrollbar": { height: "4px" },
                "&::-webkit-scrollbar-thumb": { background: "#555", borderRadius: "2px" },
              }}
            >
              {tabs.map((tb) => (
                <TabButton
                  key={tb}
                  active={activeTab === tb}
                  icon={TAB_ICON[tb]}
                  label={t(`streamerSettings.tabs.${tb}`, TAB_LABEL_KEY[tb])}
                  onClick={() => {
                    setActiveTab(tb);
                  }}
                />
              ))}
            </HStack>

            {/* 搜索框已按需求移除；二级标签（仅游戏设置） */}
            {activeTab === "game" && subTabs.length > 1 && (
              <HStack
                px={4}
                pt={3}
                spacing={1}
                overflowX="auto"
                sx={{
                  "&::-webkit-scrollbar": { height: "4px" },
                  "&::-webkit-scrollbar-thumb": { background: "#555", borderRadius: "2px" },
                }}
              >
                {subTabs.map((sb) => (
                  <TabButton
                    key={sb}
                    active={activeSub === sb}
                    icon={SUB_ICON[sb]}
                    label={t(`streamerSettings.subTabs.${sb}`, SUB_LABEL_KEY[sb])}
                    onClick={() => setActiveSub(sb)}
                  />
                ))}
              </HStack>
            )}

            {/* 工具条：计数 / 显示未填写项 */}
            <HStack px={4} pt={3} pb={1} justify="space-between" spacing={4}>
              <Text fontSize="xs" color={subTextColor}>
                {t("streamerSettings.rowCount", "{{count}} 项", { count: totalVisible })}
              </Text>
              <TogglePill
                checked={showEmpty}
                onChange={setShowEmpty}
                label={t("streamerSettings.showEmpty", "显示未填写项")}
              />
            </HStack>

            {/* 内容 */}
            <Box p={4} pt={3}>
              {visibleGroups.length === 0 ? (
                <Text fontSize="sm" color={subTextColor} textAlign="center" py={8}>
                  {t("streamerSettings.noData", "这位主播暂未填写该分类的设置")}
                </Text>
              ) : isGunCodes ? (
                <VStack align="stretch" spacing={4}>
                  {visibleGroups.map(({ group, fields }) => {
                    // 「码」类分组（改枪码 / 准星代码）做成可复制卡片，
                    // 其余（如 CS2 的「样式设置」）仍是普通表格
                    const asCodeCard =
                      fields.some((f) => f.example) || /码|代码/.test(group.title);
                    if (!asCodeCard) {
                      return <GroupCard key={group.id} group={group} fields={fields} />;
                    }
                    return (
                      <VStack key={group.id} align="stretch" spacing={3}>
                        <Text fontSize="xs" color={subTextColor} letterSpacing="0.05em" px={1}>
                          {group.title}
                        </Text>
                        {fields.map((f) => (
                          <GunCodeCard key={f.id} field={f} />
                        ))}
                      </VStack>
                    );
                  })}
                </VStack>
              ) : (
                <VStack align="stretch" spacing={4}>
                  {visibleGroups.map(({ group, fields }) => (
                    <GroupCard key={group.id} group={group} fields={fields} />
                  ))}
                </VStack>
              )}
            </Box>
          </Surface>
        </Box>
      </Flex>
    </Box>
  );
}

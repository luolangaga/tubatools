import {
  Box,
  Text,
  Heading,
  VStack,
  HStack,
  useColorModeValue,
  Badge,
  Spinner,
  SimpleGrid,
  Input,
  InputGroup,
  InputLeftElement,
  Button,
  Modal,
  ModalOverlay,
  ModalContent,
  ModalHeader,
  ModalBody,
  ModalFooter,
  ModalCloseButton,
  Textarea,
  FormControl,
  FormLabel,
  Wrap,
  WrapItem,
  Divider,
  IconButton,
} from "@chakra-ui/react";
import { useDynamicIsland } from "@/components/ui/dynamic-island";
import { useState, useEffect, useRef, useCallback, useMemo, memo } from "react";
import { invoke } from "@tauri-apps/api/core";
import { useTranslation } from "react-i18next";
import {
  Search,
  Heart,
  Copy,
  Check,
  Plus,
  ChevronLeft,
  ChevronRight,
  Globe,
  Flag,
  Map,
  Image,
  Dices,
  Users,
} from "lucide-react";
import { Link } from "react-router-dom";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { LiquidGlassButton } from "@/components/special/liquid-glass-button";
import { CustomSelect } from "@/components/special/custom-select";
import { useBackground } from "@/contexts/background-context";
import { useThemeColor } from "@/contexts/theme-color-context";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { API_BASE, apiGet, apiPost } from "@/lib/gamebox";

// ── Types ──
interface Category {
  id: number;
  name: string;
  icon: string;
  sort_order: number;
  loadout_count: number;
}

interface WeaponItem {
  weapon_name: string;
  count: number;
}

interface LoadoutItem {
  id: number;
  category_id: number;
  weapon_name: string;
  code: string;
  description: string;
  cost: number;
  author: string;
  likes: number;
  status: string;
  created_at: string;
  category_name: string;
}

interface LoadoutsResponse {
  data: LoadoutItem[];
  total: number;
  page: number;
  totalPages: number;
}

// ── MoreGunCodesCard ──
function MoreGunCodesCard() {
  const { t } = useTranslation();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const subTextColor = useColorModeValue("#000000", "#ffffff");
  const cardBg = useColorModeValue("white", "#111111");
  const cardHoverBg = useColorModeValue("gray.100", "#222222");
  const borderColor = useColorModeValue("gray.200", "#333333");
  const { liquidGlassEnabled } = useBackground();
  const textColor = useColorModeValue("#000000", "#ffffff");

  const content = (
    <VStack align="center" spacing={3} py={2} justify="center" h="100%">
      <Globe size={28} color={primaryColor} />
      <Text fontWeight="semibold" fontSize="sm" color={textColor} textAlign="center">
        {t("deltaForce.moreGunCodes", "更多改枪码")}
      </Text>
      <Text color={subTextColor} fontSize="xs" textAlign="center">
        {t("deltaForce.moreGunCodesDesc", "探索更多改枪码平台")}
      </Text>
    </VStack>
  );

  if (liquidGlassEnabled) {
    return (
      <Box flex={1} as={Link} to="/delta-force/other-platforms" style={{ textDecoration: "none" }}>
        <LiquidGlassCard p={4} h="100%" cursor="pointer">
          {content}
        </LiquidGlassCard>
      </Box>
    );
  }

  return (
    <Box
      flex={1}
      bg={cardBg}
      borderRadius="3xl"
      p={4}
      border="1px solid"
      borderColor={borderColor}
      cursor="pointer"
      _hover={{ bg: cardHoverBg }}
      transition="background-color 0.2s"
      as={Link}
      to="/delta-force/other-platforms"
      style={{ textDecoration: "none" }}
    >
      {content}
    </Box>
  );
}

// ── RouletteCard ──
function RouletteCard() {
  const { t } = useTranslation();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const subTextColor = useColorModeValue("#000000", "#ffffff");
  const cardBg = useColorModeValue("white", "#111111");
  const cardHoverBg = useColorModeValue("gray.100", "#222222");
  const borderColor = useColorModeValue("gray.200", "#333333");
  const { liquidGlassEnabled } = useBackground();
  const textColor = useColorModeValue("#000000", "#ffffff");

  const content = (
    <VStack align="center" spacing={3} py={2} justify="center" h="100%">
      <Dices size={28} color={primaryColor} />
      <Text fontWeight="semibold" fontSize="sm" color={textColor} textAlign="center">
        {t("deltaForce.roulette.title", "随机装备")}
      </Text>
      <Text color={subTextColor} fontSize="xs" textAlign="center">
        {t("deltaForce.roulette.desc", "为下次任务随机生成完整装备配置")}
      </Text>
    </VStack>
  );

  if (liquidGlassEnabled) {
    return (
      <Box flex={1} as={Link} to="/delta-force/random-equipment" style={{ textDecoration: "none" }}>
        <LiquidGlassCard p={4} h="100%" cursor="pointer">
          {content}
        </LiquidGlassCard>
      </Box>
    );
  }

  return (
    <Box
      flex={1}
      bg={cardBg}
      borderRadius="3xl"
      p={4}
      border="1px solid"
      borderColor={borderColor}
      cursor="pointer"
      _hover={{ bg: cardHoverBg }}
      transition="background-color 0.2s"
      as={Link}
      to="/delta-force/random-equipment"
      style={{ textDecoration: "none" }}
    >
      {content}
    </Box>
  );
}

// ── OfficialMapCard ──
function OfficialMapCard() {
  const { t } = useTranslation();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const subTextColor = useColorModeValue("#000000", "#ffffff");
  const cardBg = useColorModeValue("white", "#111111");
  const cardHoverBg = useColorModeValue("gray.100", "#222222");
  const borderColor = useColorModeValue("gray.200", "#333333");
  const { liquidGlassEnabled } = useBackground();
  const textColor = useColorModeValue("#000000", "#ffffff");
  const [isOpening, setIsOpening] = useState(false);
  const toast = useDynamicIsland("target");

  const handleOpen = async () => {
    if (isOpening) return;
    setIsOpening(true);
    try {
      await invoke("open_platform_window", {
        url: "https://df.qq.com/cp/a20240729directory/",
        title: t("deltaForce.officialMapToolTitle", "三角洲行动 - 官方地图工具"),
        label: `official-map-${Date.now()}`,
      });
    } catch (error) {
      toast({
        title: t("deltaForce.openFailed", "打开失败"),
        status: "error",
        duration: 2000,
        isClosable: true,
      });
    } finally {
      setIsOpening(false);
    }
  };

  const content = (
    <VStack align="center" spacing={3} py={2} justify="center" h="100%">
      <Map size={28} color={primaryColor} />
      <Text fontWeight="semibold" fontSize="sm" color={textColor} textAlign="center">
        {t("deltaForce.officialMaps", "官方地图")}
      </Text>
      <Text color={subTextColor} fontSize="xs" textAlign="center">
        {t("deltaForce.mapToolDesc", "物资点、出生点、撤离点及首领坐标")}
      </Text>
      {isOpening && <Spinner size="xs" color={primaryColor} />}
    </VStack>
  );

  if (liquidGlassEnabled) {
    return (
      <Box flex={1}>
        <LiquidGlassCard p={4} h="100%" cursor="pointer" onClick={handleOpen}>
          {content}
        </LiquidGlassCard>
      </Box>
    );
  }

  return (
    <Box
      flex={1}
      bg={cardBg}
      borderRadius="3xl"
      p={4}
      border="1px solid"
      borderColor={borderColor}
      cursor="pointer"
      _hover={{ bg: cardHoverBg }}
      transition="background-color 0.2s"
      onClick={handleOpen}
    >
      {content}
    </Box>
  );
}

// ── OfficialWallpaperCard ──
function OfficialWallpaperCard() {
  const { t } = useTranslation();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const subTextColor = useColorModeValue("#000000", "#ffffff");
  const cardBg = useColorModeValue("white", "#111111");
  const cardHoverBg = useColorModeValue("gray.100", "#222222");
  const borderColor = useColorModeValue("gray.200", "#333333");
  const { liquidGlassEnabled } = useBackground();
  const textColor = useColorModeValue("#000000", "#ffffff");
  const [isOpening, setIsOpening] = useState(false);
  const toast = useDynamicIsland("target");

  const handleOpen = async () => {
    if (isOpening) return;
    setIsOpening(true);
    try {
      const { open } = await import("@tauri-apps/plugin-shell");
      await open("https://df.qq.com/cp/community1031/wallpaper.html#/");
    } catch (error) {
      toast({
        title: t("deltaForce.openFailed", "打开失败"),
        status: "error",
        duration: 2000,
        isClosable: true,
      });
    } finally {
      setIsOpening(false);
    }
  };

  const content = (
    <VStack align="center" spacing={3} py={2} justify="center" h="100%">
      <Image size={28} color={primaryColor} />
      <Text fontWeight="semibold" fontSize="sm" color={textColor} textAlign="center">
        {t("deltaForce.officialWallpaper", "官方壁纸")}
      </Text>
      <Text color={subTextColor} fontSize="xs" textAlign="center">
        {t("deltaForce.wallpaperDesc", "官方高清壁纸下载")}
      </Text>
      {isOpening && <Spinner size="xs" color={primaryColor} />}
    </VStack>
  );

  if (liquidGlassEnabled) {
    return (
      <Box flex={1}>
        <LiquidGlassCard p={4} h="100%" cursor="pointer" onClick={handleOpen}>
          {content}
        </LiquidGlassCard>
      </Box>
    );
  }

  return (
    <Box
      flex={1}
      bg={cardBg}
      borderRadius="3xl"
      p={4}
      border="1px solid"
      borderColor={borderColor}
      cursor="pointer"
      _hover={{ bg: cardHoverBg }}
      transition="background-color 0.2s"
      onClick={handleOpen}
    >
      {content}
    </Box>
  );
}

// ── Memoized LoadoutCard ──
const LoadoutCard = memo(function LoadoutCard({
  item,
  isCopied,
  isLiked,
  isReported,
  onCopy,
  onLike,
  onReport,
  textColor,
  subTextColor,
  borderColor,
  cardBg,
  cardHoverBg,
  liquidGlassEnabled,
  primaryColor,
}: {
  item: LoadoutItem;
  isCopied: boolean;
  isLiked: boolean;
  isReported: boolean;
  onCopy: (id: number, code: string) => void;
  onLike: (id: number) => void;
  onReport: (id: number) => void;
  textColor: string;
  subTextColor: string;
  borderColor: string;
  cardBg: string;
  cardHoverBg: string;
  liquidGlassEnabled: boolean;
  primaryColor: string;
}) {
  const { t } = useTranslation();
  const code = (
    <VStack align="stretch" spacing={3}>
      <HStack justify="space-between" align="flex-start">
        <HStack spacing={2} minW={0} flex={1}>
          <Text fontWeight="bold" fontSize="md" color={textColor} noOfLines={1}>
            {item.weapon_name}
          </Text>
          <Badge
            variant="subtle"
            bg={`${primaryColor}20`}
            color={primaryColor}
            fontSize="xs"
            flexShrink={0}
          >
            {item.category_name}
          </Badge>
        </HStack>
        <Text fontSize="sm" color={subTextColor} flexShrink={0} whiteSpace="nowrap">
          💰 {Number(item.cost).toLocaleString()}
        </Text>
      </HStack>

      <Box position="relative">
        <Text
          fontSize="sm"
          fontFamily="monospace"
          bg={useColorModeValue("gray.100", "#2a2a2a")}
          p={3}
          borderRadius="lg"
          wordBreak="break-all"
          lineHeight="1.6"
          color={textColor}
          pr="70px"
        >
          {item.code}
        </Text>
        <Button
          size="xs"
          position="absolute"
          right={2}
          top={2}
          variant={isCopied ? "solid" : "outline"}
          bg={isCopied ? "#48BB78" : "transparent"}
          color={isCopied ? "white" : primaryColor}
          borderColor={isCopied ? undefined : primaryColor}
          _hover={isCopied ? {} : { bg: `${primaryColor}15` }}
          onClick={() => onCopy(item.id, item.code)}
          leftIcon={isCopied ? <Check size={12} /> : <Copy size={12} />}
        >
          {isCopied ? "已复制" : "复制"}
        </Button>
      </Box>

      {item.description && (
        <Text fontSize="sm" color={subTextColor}>
          {item.description}
        </Text>
      )}

      <HStack justify="space-between" pt={1} borderTop="1px solid" borderColor={borderColor}>
        <Text fontSize="sm" color={subTextColor}>
          {item.author || "匿名"}
        </Text>
        <HStack spacing={1}>
          <Button
            size="xs"
            variant="ghost"
            color={isReported ? "green.400" : subTextColor}
            onClick={() => onReport(item.id)}
            leftIcon={<Flag size={12} />}
            _hover={isReported ? {} : { color: "red.400" }}
            isDisabled={isReported}
          >
            {isReported ? "已报告" : t("deltaForce.report", "无法使用？")}
          </Button>
          <IconButton
            aria-label="Like"
            size="sm"
            variant="ghost"
            color={isLiked ? "red.400" : subTextColor}
            onClick={() => onLike(item.id)}
            icon={<Heart size={14} fill={isLiked ? "currentColor" : "none"} />}
          />
          <Text fontSize="sm" color={subTextColor} minW="20px">
            {item.likes}
          </Text>
        </HStack>
      </HStack>
    </VStack>
  );

  if (liquidGlassEnabled) {
    return <LiquidGlassCard p={4}>{code}</LiquidGlassCard>;
  }
  return (
    <Box
      bg={cardBg}
      borderRadius="3xl"
      p={4}
      border="1px solid"
      borderColor={borderColor}
      _hover={{ bg: cardHoverBg }}
      transition="background-color 0.15s"
    >
      {code}
    </Box>
  );
});

// ── GunLoadoutBrowser ──
const GunLoadoutBrowser = memo(function GunLoadoutBrowser() {
  const { t } = useTranslation();
  const toast = useDynamicIsland("target");
  const { getActiveColor } = useThemeColor();
  const { liquidGlassEnabled, liquidGlassBlur } = useBackground();
  // 模糊立即生效
  const effectiveBlur = liquidGlassEnabled ? liquidGlassBlur : 0;

  const primaryColor = getActiveColor();
  const textColor = useColorModeValue("#000000", "#ffffff");
  const subTextColor = useColorModeValue("#000000", "#ffffff");
  const cardBg = useColorModeValue("white", "#111111");
  const cardHoverBg = useColorModeValue("gray.100", "#222222");
  const borderColor = useColorModeValue("gray.200", "#333333");

  const [categories, setCategories] = useState<Category[]>([]);
  const [weaponsMap, setWeaponsMap] = useState<Record<number, WeaponItem[]>>({});
  const [selectedCategoryId, setSelectedCategoryId] = useState<number | "all">("all");
  const [selectedWeapon, setSelectedWeapon] = useState("");
  const [loadouts, setLoadouts] = useState<LoadoutItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [searchQuery, setSearchQuery] = useState("");
  const [activeSearch, setActiveSearch] = useState("");
  const [sortBy, setSortBy] = useState<"likes" | "latest">("likes");
  const [isInitialLoading, setIsInitialLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [copiedId, setCopiedId] = useState<number | null>(null);
  const [likedIds, setLikedIds] = useState<Set<number>>(new Set());
  const [reportedIds, setReportedIds] = useState<Set<number>>(new Set());
  const searchTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  // ── Load categories ──
  useEffect(() => {
    (async () => {
      try {
        const cats = await apiGet<Category[]>("/api/categories");
        setCategories(cats);
      } catch (err) {
        console.error("Failed to load categories:", err);
      }
    })();
  }, []);

  // ── Load loadouts on filter change ──
  useEffect(() => {
    loadLoadouts();
  }, [selectedCategoryId, selectedWeapon, page, sortBy, activeSearch]);

  const loadLoadouts = useCallback(async () => {
    if (abortRef.current) abortRef.current.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setIsRefreshing(true);
    try {
      const params = new URLSearchParams();
      if (selectedCategoryId !== "all") params.set("category_id", String(selectedCategoryId));
      if (selectedWeapon) params.set("weapon_name", selectedWeapon);
      if (activeSearch.trim()) params.set("search", activeSearch.trim());
      params.set("page", String(page));

      const res = await fetch(`${API_BASE}/api/loadouts?${params}`, { signal: controller.signal });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text.slice(0, 100) || `HTTP ${res.status}`);
      }
      const result: LoadoutsResponse = await res.json();
      if (controller.signal.aborted) return;
      setLoadouts(result.data);
      setTotal(result.total);
      setTotalPages(result.totalPages);
      setIsInitialLoading(false);
    } catch (err: unknown) {
      if (err instanceof Error && err.name === "AbortError") return;
      toast({
        title: "加载失败",
        description: String(err),
        status: "error",
        duration: 3000,
        isClosable: true,
      });
      setLoadouts([]);
      setIsInitialLoading(false);
    } finally {
      setIsRefreshing(false);
    }
  }, [selectedCategoryId, selectedWeapon, page, sortBy, activeSearch, toast]);

  // ── Load weapons when category selected ──
  useEffect(() => {
    if (selectedCategoryId === "all") {
      setSelectedWeapon("");
      return;
    }
    if (!weaponsMap[selectedCategoryId]) {
      (async () => {
        try {
          const weapons = await apiGet<WeaponItem[]>(`/api/weapons/${selectedCategoryId}`);
          setWeaponsMap((prev) => ({ ...prev, [selectedCategoryId]: weapons }));
        } catch (err) {
          console.error("Failed to load weapons:", err);
        }
      })();
    }
    setSelectedWeapon("");
  }, [selectedCategoryId]);

  // ── Search debounce ──
  const handleSearchChange: React.ChangeEventHandler<HTMLInputElement> = useCallback((e) => {
    const val = e.target.value;
    setSearchQuery(val);
    if (searchTimer.current) clearTimeout(searchTimer.current);
    searchTimer.current = setTimeout(() => {
      setActiveSearch(val);
      setPage(1);
    }, 300);
  }, []);

  // ── Copy code ──
  const handleCopy = useCallback(async (id: number, code: string) => {
    try {
      await navigator.clipboard.writeText(code);
      setCopiedId(id);
      setTimeout(() => setCopiedId(null), 1500);
    } catch {
      // silent
    }
  }, []);

  // ── Like ──
  const handleLike = useCallback(async (id: number) => {
    try {
      const result = await apiPost<{ id: number; likes: number }>(`/api/loadouts/${id}/like`);
      setLikedIds((prev) => new Set(prev).add(id));
      setLoadouts((prev) => prev.map((l) => (l.id === id ? { ...l, likes: result.likes } : l)));
    } catch {
      // silent
    }
  }, []);

  // ── Report ──
  const handleReport = useCallback(
    async (id: number) => {
      if (reportedIds.has(id)) return;
      try {
        await apiPost(`/api/loadouts/${id}/report`);
        setReportedIds((prev) => new Set(prev).add(id));
        toast({
          title: t("deltaForce.reported", "已报告管理员"),
          status: "success",
          duration: 2000,
          isClosable: true,
        });
      } catch {
        // silent
      }
    },
    [reportedIds, toast, t],
  );

  // ── Upload state ──
  const [isUploadOpen, setIsUploadOpen] = useState(false);
  const [uploadCategory, setUploadCategory] = useState("");
  const [uploadWeapon, setUploadWeapon] = useState("");
  const [uploadCode, setUploadCode] = useState("");
  const [uploadDesc, setUploadDesc] = useState("");
  const [uploadCost, setUploadCost] = useState("");
  const [uploadAuthor, setUploadAuthor] = useState("");
  const [uploadWeapons, setUploadWeapons] = useState<WeaponItem[]>([]);
  const [isUploading, setIsUploading] = useState(false);
  const [uploadError, setUploadError] = useState("");

  // load weapons when upload modal opens or category changes
  const loadUploadWeapons = useCallback(async (catId: string) => {
    try {
      const weapons = await apiGet<WeaponItem[]>(`/api/weapons/${catId}`);
      setUploadWeapons(weapons);
    } catch {
      setUploadWeapons([]);
    }
  }, []);

  const openUpload = useCallback(() => {
    setUploadError("");
    setUploadCode("");
    setUploadDesc("");
    setUploadCost("");
    setUploadAuthor("");
    if (categories.length > 0) {
      const firstId = String(categories[0].id);
      setUploadCategory(firstId);
      setUploadWeapon("");
      setUploadWeapons([]);
      loadUploadWeapons(firstId);
    }
    setIsUploadOpen(true);
  }, [categories, loadUploadWeapons]);

  const handleUploadCategoryChange = useCallback(
    (catId: string) => {
      setUploadCategory(catId);
      setUploadWeapon("");
      loadUploadWeapons(catId);
    },
    [loadUploadWeapons],
  );

  const handleUploadSubmit = useCallback(async () => {
    setUploadError("");
    if (!uploadCategory) {
      setUploadError("请选择分类");
      return;
    }
    if (!uploadWeapon) {
      setUploadError("请选择武器");
      return;
    }
    if (!uploadCode.trim()) {
      setUploadError("请输入改枪码");
      return;
    }
    const cost = parseInt(uploadCost);
    if (!cost || cost <= 0) {
      setUploadError("请输入有效的改枪费用");
      return;
    }

    setIsUploading(true);
    try {
      await apiPost("/api/loadouts", {
        category_id: Number(uploadCategory),
        weapon_name: uploadWeapon,
        code: uploadCode.trim(),
        cost: cost,
        description: uploadDesc.trim(),
        author: uploadAuthor.trim() || "匿名",
      });
      toast({
        title: t("deltaForce.uploadSuccess"),
        description: t("deltaForce.uploadQueueHint"),
        status: "success",
        duration: 3000,
        isClosable: true,
      });
      setIsUploadOpen(false);
      setPage(1);
    } catch (err) {
      setUploadError(`${t("deltaForce.uploadFailed")}: ${err}`);
    } finally {
      setIsUploading(false);
    }
  }, [uploadCategory, uploadWeapon, uploadCode, uploadCost, uploadDesc, uploadAuthor, toast, t]);

  // ── Memoized category pills ──
  const categoryPills = useMemo(
    () => (
      <Wrap spacing={2} mb={1}>
        <WrapItem>
          <Box
            as="button"
            px={4}
            py={2}
            borderRadius="full"
            fontSize="sm"
            fontWeight="medium"
            bg={selectedCategoryId === "all" ? primaryColor : "transparent"}
            color={selectedCategoryId === "all" ? "white" : subTextColor}
            border="1px solid"
            borderColor={selectedCategoryId === "all" ? primaryColor : borderColor}
            _hover={{ bg: selectedCategoryId === "all" ? primaryColor : cardHoverBg }}
            transition="all 0.15s"
            onClick={() => {
              setSelectedCategoryId("all");
              setPage(1);
            }}
          >
            {t("deltaForce.allCategories")}
          </Box>
        </WrapItem>
        {categories.map((cat) => (
          <WrapItem key={cat.id}>
            <Box
              as="button"
              px={4}
              py={2}
              borderRadius="full"
              fontSize="sm"
              fontWeight="medium"
              bg={
                selectedCategoryId === cat.id && selectedWeapon === ""
                  ? primaryColor
                  : "transparent"
              }
              color={
                selectedCategoryId === cat.id && selectedWeapon === "" ? "white" : subTextColor
              }
              border="1px solid"
              borderColor={
                selectedCategoryId === cat.id && selectedWeapon === "" ? primaryColor : borderColor
              }
              _hover={{
                bg:
                  selectedCategoryId === cat.id && selectedWeapon === ""
                    ? primaryColor
                    : cardHoverBg,
              }}
              transition="all 0.15s"
              onClick={() => {
                setSelectedCategoryId(cat.id);
                setPage(1);
              }}
            >
              {cat.name}
              <Text as="span" ml={1} fontSize="xs" opacity={0.7}>
                {cat.loadout_count}
              </Text>
            </Box>
          </WrapItem>
        ))}
      </Wrap>
    ),
    [
      categories,
      selectedCategoryId,
      selectedWeapon,
      primaryColor,
      subTextColor,
      borderColor,
      cardHoverBg,
      t,
    ],
  );

  const showEmpty = !isInitialLoading && loadouts.length === 0;
  const showCards = loadouts.length > 0;

  const weaponCodesContent = (
    <>
      <HStack justify="space-between" align="center" mb={1}>
        <HStack spacing={3}>
          <Heading size="md" color={textColor}>
            {t("deltaForce.weaponCodes")}
          </Heading>
          <Text fontSize="sm" color={subTextColor}>
            {total} 条
          </Text>
        </HStack>
        <LiquidGlassButton size="sm" leftIcon={<Plus size={16} />} onClick={openUpload}>
          {t("deltaForce.upload")}
        </LiquidGlassButton>
      </HStack>

      {categoryPills}

      <HStack spacing={3} wrap="wrap">
        <InputGroup maxW="280px" size="sm">
          <InputLeftElement>
            <Search size={14} />
          </InputLeftElement>
          <Input
            placeholder={t("deltaForce.searchPlaceholder")}
            value={searchQuery}
            onChange={handleSearchChange}
            borderRadius="full"
          />
        </InputGroup>

        {selectedCategoryId !== "all" && weaponsMap[selectedCategoryId]?.length > 0 && (
          <CustomSelect
            value={selectedWeapon}
            onChange={(val) => {
              setSelectedWeapon(val);
              setPage(1);
            }}
            options={[
              { value: "", label: t("deltaForce.allWeapons") },
              ...weaponsMap[selectedCategoryId].map((w) => ({
                value: w.weapon_name,
                label: `${w.weapon_name} (${w.count})`,
              })),
            ]}
            width="180px"
            placeholder={t("deltaForce.allWeapons")}
          />
        )}
      </HStack>

      <Divider my={2} />

      {isInitialLoading ? (
        <HStack justify="center" py={10}>
          <Spinner size="md" color={primaryColor} />
        </HStack>
      ) : showEmpty ? (
        <VStack py={10} spacing={2}>
          <Text color={subTextColor} fontSize="sm">
            {t("deltaForce.noLoadouts")}
          </Text>
          <Text color={subTextColor} fontSize="xs">
            {t("deltaForce.noLoadoutsHint")}
          </Text>
        </VStack>
      ) : null}

      {showCards && (
        <SimpleGrid columns={{ base: 1, md: 2 }} spacing={4}>
          {loadouts.map((item) => (
            <LoadoutCard
              key={item.id}
              item={item}
              isCopied={copiedId === item.id}
              isLiked={likedIds.has(item.id)}
              isReported={reportedIds.has(item.id)}
              onCopy={handleCopy}
              onLike={handleLike}
              onReport={handleReport}
              textColor={textColor}
              subTextColor={subTextColor}
              borderColor={borderColor}
              cardBg={cardBg}
              cardHoverBg={cardHoverBg}
              liquidGlassEnabled={liquidGlassEnabled}
              primaryColor={primaryColor}
            />
          ))}
        </SimpleGrid>
      )}

      {showCards && totalPages > 1 && (
        <HStack justify="center" spacing={4} pt={4}>
          <Button
            size="sm"
            variant="outline"
            leftIcon={<ChevronLeft size={14} />}
            isDisabled={page <= 1}
            onClick={() => setPage((p) => Math.max(1, p - 1))}
          >
            {t("deltaForce.previousPage")}
          </Button>
          <Text fontSize="sm" color={subTextColor}>
            {t("deltaForce.pageInfo", { page, totalPages })}
          </Text>
          <Button
            size="sm"
            variant="outline"
            rightIcon={<ChevronRight size={14} />}
            isDisabled={page >= totalPages}
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
          >
            {t("deltaForce.nextPage")}
          </Button>
        </HStack>
      )}

      <Modal isOpen={isUploadOpen} onClose={() => setIsUploadOpen(false)} isCentered size="md">
        <ModalOverlay
          backdropFilter={liquidGlassEnabled ? `blur(${effectiveBlur}px)` : "blur(4px)"}
        />
        <ModalContent
          bg={
            liquidGlassEnabled
              ? useColorModeValue("rgba(255,255,255,0.2)", "rgba(0,0,0,0.25)")
              : useColorModeValue("white", "#1a1a1a")
          }
          backdropFilter={liquidGlassEnabled ? `blur(${effectiveBlur}px)` : undefined}
          borderColor={
            liquidGlassEnabled
              ? useColorModeValue("rgba(255,255,255,0.2)", "rgba(255,255,255,0.08)")
              : undefined
          }
          color={useColorModeValue("#000", "#fff")}
        >
          <ModalHeader>{t("deltaForce.upload")}</ModalHeader>
          <ModalCloseButton />
          <ModalBody>
            <VStack spacing={4} align="stretch">
              <FormControl>
                <FormLabel fontSize="sm">{t("deltaForce.category")}</FormLabel>
                <CustomSelect
                  value={uploadCategory}
                  onChange={handleUploadCategoryChange}
                  options={categories.map((cat) => ({ value: String(cat.id), label: cat.name }))}
                  width="100%"
                />
              </FormControl>
              <FormControl>
                <FormLabel fontSize="sm">{t("deltaForce.weaponName")}</FormLabel>
                <CustomSelect
                  value={uploadWeapon}
                  onChange={setUploadWeapon}
                  options={[
                    { value: "", label: t("deltaForce.selectWeapon") },
                    ...uploadWeapons.map((w) => ({
                      value: w.weapon_name,
                      label: `${w.weapon_name} (${w.count})`,
                    })),
                  ]}
                  width="100%"
                  placeholder={t("deltaForce.selectWeapon")}
                />
              </FormControl>
              <FormControl>
                <FormLabel fontSize="sm">{t("deltaForce.code")}</FormLabel>
                <Textarea
                  value={uploadCode}
                  onChange={(e) => setUploadCode(e.target.value)}
                  placeholder={t("deltaForce.codePlaceholder")}
                  rows={3}
                  maxLength={500}
                  _placeholder={{ color: useColorModeValue("#000", "#ccc") }}
                />
              </FormControl>
              <FormControl>
                <FormLabel fontSize="sm">{t("deltaForce.description")}</FormLabel>
                <Input
                  value={uploadDesc}
                  onChange={(e) => setUploadDesc(e.target.value)}
                  placeholder={t("deltaForce.descriptionPlaceholder")}
                  maxLength={200}
                  _placeholder={{ color: useColorModeValue("#000", "#ccc") }}
                />
              </FormControl>
              <FormControl>
                <FormLabel fontSize="sm">{t("deltaForce.cost")}</FormLabel>
                <Input
                  type="number"
                  value={uploadCost}
                  onChange={(e) => setUploadCost(e.target.value)}
                  placeholder="0"
                  min={0}
                  _placeholder={{ color: useColorModeValue("#000", "#ccc") }}
                />
              </FormControl>
              <FormControl>
                <FormLabel fontSize="sm">{t("deltaForce.author")}</FormLabel>
                <Input
                  value={uploadAuthor}
                  onChange={(e) => setUploadAuthor(e.target.value)}
                  placeholder={t("deltaForce.nicknamePlaceholder")}
                  maxLength={20}
                  _placeholder={{ color: useColorModeValue("#000", "#ccc") }}
                />
              </FormControl>
              {uploadError && (
                <Text fontSize="sm" color="red.400">
                  {uploadError}
                </Text>
              )}
            </VStack>
          </ModalBody>
          <ModalFooter gap={3}>
            <Button variant="ghost" onClick={() => setIsUploadOpen(false)}>
              {t("deltaForce.cancel")}
            </Button>
            <LiquidGlassButton
              onClick={handleUploadSubmit}
              isLoading={isUploading}
              loadingText="提交中..."
            >
              {t("deltaForce.submit")}
            </LiquidGlassButton>
          </ModalFooter>
        </ModalContent>
      </Modal>
    </>
  );

  const codesWrapper = liquidGlassEnabled ? (
    <LiquidGlassCard p={5}>{weaponCodesContent}</LiquidGlassCard>
  ) : (
    <Box
      bg={useColorModeValue("white", "#111111")}
      borderRadius="xl"
      p={5}
      border="1px solid"
      borderColor={useColorModeValue("gray.200", "#333333")}
    >
      {weaponCodesContent}
    </Box>
  );

  return codesWrapper;
});

// ── StreamerSettingsCard ──
function StreamerSettingsCard() {
  const { t } = useTranslation();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  const subTextColor = useColorModeValue("#000000", "#ffffff");
  const cardBg = useColorModeValue("white", "#111111");
  const cardHoverBg = useColorModeValue("gray.100", "#222222");
  const borderColor = useColorModeValue("gray.200", "#333333");
  const { liquidGlassEnabled } = useBackground();
  const textColor = useColorModeValue("#000000", "#ffffff");

  const content = (
    <VStack align="center" spacing={3} py={2} justify="center" h="100%">
      <Users size={28} color={primaryColor} />
      <Text fontWeight="semibold" fontSize="sm" color={textColor} textAlign="center">
        {t("streamerSettings.cardTitle", "主播设置")}
      </Text>
      <Text color={subTextColor} fontSize="xs" textAlign="center">
        {t("streamerSettings.cardDesc", "主播同款游戏设置、驱动与外设配置")}
      </Text>
    </VStack>
  );

  if (liquidGlassEnabled) {
    return (
      <Box flex={1} as={Link} to="/delta-force/streamers" style={{ textDecoration: "none" }}>
        <LiquidGlassCard p={4} h="100%" cursor="pointer">
          {content}
        </LiquidGlassCard>
      </Box>
    );
  }

  return (
    <Box
      flex={1}
      bg={cardBg}
      borderRadius="3xl"
      p={4}
      border="1px solid"
      borderColor={borderColor}
      cursor="pointer"
      _hover={{ bg: cardHoverBg }}
      transition="background-color 0.2s"
      as={Link}
      to="/delta-force/streamers"
      style={{ textDecoration: "none" }}
    >
      {content}
    </Box>
  );
}

// ── Main Page ──
export default function DeltaForcePage() {
  const { t } = useTranslation();
  const headingColor = useColorModeValue("gray.900", "#ffffff");
  const adaptiveTitle = useAdaptiveTextColor();

  return (
    <Box pt={8} pb={8}>
      <Heading size="lg" color={adaptiveTitle.text} textShadow={adaptiveTitle.shadow} mb={6}>
        {t("deltaForce.title")}
      </Heading>

      {/* 主播设置 / 随机装备 / 更多改枪码 / 官方地图 / 官方壁纸 */}
      <HStack align="stretch" spacing={6} mb={6} wrap="wrap">
        <StreamerSettingsCard />
        <RouletteCard />
        <MoreGunCodesCard />
        <OfficialMapCard />
        <OfficialWallpaperCard />
      </HStack>

      {/* 改枪码浏览器 */}
      <GunLoadoutBrowser />
    </Box>
  );
}

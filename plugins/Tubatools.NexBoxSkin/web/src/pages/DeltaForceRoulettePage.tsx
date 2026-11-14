import {
  Box,
  Text,
  VStack,
  HStack,
  SimpleGrid,
  Badge,
  Skeleton,
  useColorModeValue,
  Checkbox,
} from "@chakra-ui/react";
import { useMemo, useState, useCallback, useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import {
  Dices,
  Map as MapIcon,
  User,
  Crosshair,
  HardHat,
  Shield,
  Target,
} from "lucide-react";
import { PageBackButton } from "@/components/special/page-back-button";
import { PageHeader } from "@/components/special/page-header";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { LiquidGlassButton } from "@/components/special/liquid-glass-button";
import { useBackground } from "@/contexts/background-context";
import { useThemeColor } from "@/contexts/theme-color-context";
import { useRouletteImage } from "@/hooks/use-roulette-image";
import { weapons, isPistol, RouletteWeapon } from "@/data/delta-roulette/weapons";
import { armors } from "@/data/delta-roulette/armors";
import { helmets } from "@/data/delta-roulette/helmets";
import {
  maps,
  isClassifiedDifficulty,
  isNormalDifficulty,
  NORMAL_LOADOUT_LIMITS,
  RouletteMap,
  RouletteDifficulty,
} from "@/data/delta-roulette/maps";
import { operators } from "@/data/delta-roulette/operators";
import { tasks } from "@/data/delta-roulette/tasks";

// ── 随机结果 ──
interface RouletteResult {
  map: RouletteMap;
  operatorName: string;
  operatorPic: string;
  weapon: RouletteWeapon;
  helmetName: string;
  helmetPic: string;
  helmetLevel: number;
  armorName: string;
  armorPic: string;
  armorLevel: number;
  task: string;
}

// 通用随机取一项
function pickOne<T>(arr: T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

// 按弹药等级排序的段内随机：上上签取偏高段，下下签取偏低段
function pickWeaponByGrade(
  list: RouletteWeapon[],
  lucky: boolean,
  unlucky: boolean,
): RouletteWeapon {
  if (!lucky && !unlucky) return pickOne(list);
  const sorted = [...list].sort((a, b) => a.selectedAmmoGrade - b.selectedAmmoGrade);
  const band = Math.max(1, Math.floor(sorted.length / 3));
  if (lucky) {
    const high = sorted.slice(-band);
    return pickOne(high);
  }
  const low = sorted.slice(0, band);
  return pickOne(low);
}

// 按地图难度裁剪装备池：
// 普通（常规）难度排除 6 套（护甲/头盔 6 级）与 5 级及以上弹药；机密/绝密/永夜不做限制。
// 抽装备与结果区的滚动候选图池都走这里，保证「候选图里不会出现本该被排除的装备」。
function poolsForDifficulty(difficulty: RouletteDifficulty) {
  if (!isNormalDifficulty(difficulty)) {
    return { helmets, armors, weapons };
  }
  return {
    helmets: helmets.filter((h) => h.protectLevel <= NORMAL_LOADOUT_LIMITS.maxProtectLevel),
    armors: armors.filter((a) => a.protectLevel <= NORMAL_LOADOUT_LIMITS.maxProtectLevel),
    weapons: weapons.filter((w) => w.selectedAmmoGrade <= NORMAL_LOADOUT_LIMITS.maxAmmoGrade),
  };
}

// 可重复使用的结果卡片壳（玻璃 / 非玻璃两套）
function ResultCard({
  icon,
  title,
  children,
}: {
  icon: React.ReactNode;
  title: string;
  children: React.ReactNode;
}) {
  const { liquidGlassEnabled } = useBackground();
  const textColor = useColorModeValue("#000000", "#ffffff");
  const subTextColor = useColorModeValue("#000000", "#ffffff");
  const cardBg = useColorModeValue("white", "#111111");
  const borderColor = useColorModeValue("gray.200", "#333333");
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();

  const contentInner = (
    <VStack align="stretch" spacing={3}>
      <HStack spacing={2}>
        {icon}
        <Text fontWeight="bold" fontSize="sm" color={primaryColor}>
          {title}
        </Text>
      </HStack>
      {children}
    </VStack>
  );

  if (liquidGlassEnabled) {
    return <LiquidGlassCard p={4}>{contentInner}</LiquidGlassCard>;
  }
  return (
    <Box
      bg={cardBg}
      borderRadius="3xl"
      p={4}
      border="1px solid"
      borderColor={borderColor}
      color={textColor}
    >
      {contentInner}
    </Box>
  );
}

// 弹药小图（本地缓存显示）
function AmmoThumb({ url }: { url: string }) {
  const { src } = useRouletteImage(url);
  return (
    <Box
      as="img"
      src={src || url}
      alt="ammo"
      w="100%"
      h="100%"
      objectFit="contain"
      display="block"
    />
  );
}

// 单张滚动图的本地缓存显示：走 cache_delta_image -> convertFileSrc，失败回退远程直连
function CachedSlotImg({
  src,
  alt,
  width,
  heightVal,
  objectFit,
}: {
  src: string;
  alt: string;
  width: string;
  heightVal: string;
  objectFit: "cover" | "contain";
}) {
  const { src: cachedSrc } = useRouletteImage(src);
  return (
    <Box
      as="img"
      src={cachedSrc || src}
      alt={alt}
      w={width}
      h={heightVal}
      objectFit={objectFit}
      flexShrink={0}
      display="block"
      loading="lazy"
    />
  );
}

// 横向滚动定格：候选图条从右侧滚入并减速停在结果图，定格后保留同一滚动条（不再二次加载）
function SlotScroller({
  urls,
  winnerUrl,
  alt,
  slotW = 112,
  height = 130,
  objectFit = "cover",
  delay = 0,
  instant = false,
  fill = false,
}: {
  urls: string[];
  winnerUrl: string;
  alt: string;
  slotW?: number;
  height?: number;
  objectFit?: "cover" | "contain";
  delay?: number;
  instant?: boolean;
  fill?: boolean;
}) {
  const boxRef = useRef<HTMLDivElement>(null);
  const stripRef = useRef<HTMLDivElement>(null);
  const [boxW, setBoxW] = useState(fill ? 0 : slotW);

  const winnerIndex = Math.max(0, urls.indexOf(winnerUrl));
  // 固定滚动的格数：始终从“若干张装饰候选图”滚到结果图，保证每次滚动格数一致
  const ROLL = 6;
  // 取结果图，并在其前面补足 ROLL 张装饰候选图（从池中挑非结果的其他图）
  const candidates = urls.filter((u) => u !== winnerUrl);
  const decor: string[] = [];
  if (candidates.length > 0) {
    // 伪随机顺序但每次结果稳定：从结果位置附近向前取，避免重复
    let k = 0;
    while (decor.length < ROLL) {
      decor.push(candidates[(winnerIndex + k) % candidates.length]);
      k++;
    }
  }
  const cells = [...decor, winnerUrl];

  // 测量容器宽度：fill 模式下每格占满卡片宽度
  useEffect(() => {
    if (!fill) return;
    const box = boxRef.current;
    if (!box) return;
    const ro = new ResizeObserver(() => {
      setBoxW(box.clientWidth || slotW);
    });
    ro.observe(box);
    setBoxW(box.clientWidth || slotW);
    return () => ro.disconnect();
  }, [fill, slotW]);

  useEffect(() => {
    if (cells.length === 0) return;
    const el = stripRef.current;
    if (!el) return;

    // fill 模式用容器实际宽度作为每个 slot 的宽度，保证占满卡片
    const effSlot = fill ? boxRef.current?.clientWidth || slotW : slotW;
    // 结果图在条末位（index=ROLL），固定向左滚 ROLL 格即停到结果图
    const target = -(ROLL * effSlot);
    // 跳过动画：直接落在结果位置，无滚动
    if (instant) {
      el.style.transform = `translateX(${target}px)`;
      return;
    }

    let raf = 0;
    const startT = performance.now() + delay;
    const duration = 1200;
    const easeOut = (t: number) => 1 - Math.pow(1 - t, 3);
    const step = (now: number) => {
      const t = Math.min(1, Math.max(0, (now - startT) / duration));
      const eased = easeOut(t);
      el.style.transform = `translateX(${target * eased}px)`;
      if (t < 1) {
        raf = requestAnimationFrame(step);
      }
    };
    raf = requestAnimationFrame(step);
    return () => cancelAnimationFrame(raf);
  }, [cells.length, boxW, slotW, delay, instant, fill, ROLL]);

  return (
    <Box
      ref={boxRef}
      w={fill ? "100%" : `${slotW}px`}
      h={`${height}px`}
      overflow="hidden"
      borderRadius="lg"
      bg="blackAlpha.200"
      position="relative"
    >
      <Box
        ref={stripRef}
        display="flex"
        style={{
          willChange: "transform",
          width: fill ? "max-content" : cells.length * boxW,
          height,
        }}
        position="absolute"
        left={0}
        top={0}
      >
        {cells.map((u, i) => (
          <CachedSlotImg
            key={`${u}-${i}`}
            src={u}
            alt={alt}
            width={fill ? `${boxW}px` : `${slotW}px`}
            heightVal={`${height}px`}
            objectFit={objectFit}
          />
        ))}
      </Box>
    </Box>
  );
}
  export default function DeltaForceRoulettePage() {
  const { t } = useTranslation();
  const subTextColor = useColorModeValue("#000000", "#ffffff");
  const textColor = useColorModeValue("#000000", "#ffffff");
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();

  const [onlyClassified, setOnlyClassified] = useState(false);
  const [skipAnimation, setSkipAnimation] = useState(false);
  const [noPistol, setNoPistol] = useState(false);
  const [result, setResult] = useState<RouletteResult | null>(null);
  const [spinKey, setSpinKey] = useState(0);

  const generate = useCallback(() => {
    const mapPool = onlyClassified ? maps.filter((m) => isClassifiedDifficulty(m.difficulty)) : maps;
    // 先定地图，再按地图难度裁剪装备池（普通/常规 不给 6 套与 5 级及以上弹药）
    const map = pickOne(mapPool);
    const pool = poolsForDifficulty(map.difficulty);
    const op = pickOne(operators);
    const helmet = pickOne(pool.helmets);
    const armor = pickOne(pool.armors);
    const weaponPool = noPistol ? pool.weapons.filter((w) => !isPistol(w)) : pool.weapons;
    const weapon = pickWeaponByGrade(weaponPool, false, false);
    setResult({
      map,
      operatorName: op.name,
      operatorPic: op.pic,
      weapon,
      helmetName: helmet.objectName,
      helmetPic: helmet.pic,
      helmetLevel: helmet.protectLevel,
      armorName: armor.objectName,
      armorPic: armor.pic,
      armorLevel: armor.protectLevel,
      task: pickOne(tasks),
    });
    setSpinKey((k) => k + 1);
  }, [onlyClassified, skipAnimation, noPistol]);

  // 各物品的候选图池（用于滚动定格动画），与 generate 中的过滤逻辑保持一致。
  // 装备图池跟随抽到的地图难度，避免动画里滚出本该被排除的 6 套 / 5 级弹药图。
  // 这些池只在有结果时渲染，没有结果时用全量即可。
  const loadoutPools = result ? poolsForDifficulty(result.map.difficulty) : null;
  const mapPicPool = (onlyClassified ? maps.filter((m) => isClassifiedDifficulty(m.difficulty)) : maps).map(
    (m) => m.pic,
  );
  const weaponPicPool = (loadoutPools?.weapons ?? weapons)
    .filter((w) => !noPistol || !isPistol(w))
    .map((w) => w.pic);
  const operatorPicPool = operators.map((o) => o.pic);
  const helmetPicPool = (loadoutPools?.helmets ?? helmets).map((h) => h.pic);
  const armorPicPool = (loadoutPools?.armors ?? armors).map((a) => a.pic);

  const highlightBadge = useMemo(
    () => (level: number) => ({
      bg: level >= 5 ? primaryColor : "transparent",
      color: level >= 5 ? "#fff" : subTextColor,
      border: "1px solid",
      borderColor: level >= 5 ? primaryColor : "currentColor",
    }),
    [primaryColor, subTextColor],
  );

  return (
    <Box pt={8} pb={8}>
      <PageHeader title={t("deltaForce.roulette.title", "三角洲随机装备")} mb={6} />

        {/* 控件区 */}
        <LiquidGlassCard p={5} mb={6}>
        <VStack align="stretch" spacing={4}>
          <HStack spacing={6} wrap="wrap">
            {[
              { label: t("deltaForce.roulette.onlyClassified", "只玩绝密"), checked: onlyClassified, set: setOnlyClassified },
              { label: t("deltaForce.roulette.skipAnimation", "跳过动画"), checked: skipAnimation, set: setSkipAnimation },
              { label: t("deltaForce.roulette.noPistol", "不要手枪"), checked: noPistol, set: setNoPistol },
            ].map((opt) => (
              <Checkbox
                key={opt.label}
                isChecked={opt.checked}
                onChange={(e) => opt.set(e.target.checked)}
                color={primaryColor}
                _checked={{
                  ".chakra-checkbox__control": {
                    borderColor: primaryColor,
                    bg: primaryColor,
                  },
                  ".chakra-checkbox__label": { color: primaryColor },
                }}
                iconColor="#fff"
      >
                {opt.label}
              </Checkbox>
            ))}
          </HStack>

          <HStack spacing={2} align="center" justify="space-between" wrap="wrap">
            <HStack spacing={2} align="center">
              <Target size={16} color={primaryColor} />
              <Text fontWeight="semibold" color={textColor} fontSize="sm">
                {t("deltaForce.roulette.specialTask", "特殊任务")}
              </Text>
              <Text color={result ? textColor : subTextColor}>
                {result ? result.task : "?"}
              </Text>
            </HStack>
            <LiquidGlassButton
              leftIcon={<Dices size={16} />}
              onClick={generate}
            >
              {t("deltaForce.roulette.generate", "生成随机装备")}
            </LiquidGlassButton>
          </HStack>
        </VStack>
      </LiquidGlassCard>

      {/* 结果区 */}
      {result ? (
        <VStack align="stretch" spacing={4}>
          {/* 上排：地图 / 主武器（横向长方形） */}
          <SimpleGrid columns={{ base: 1, md: 2 }} spacing={4}>
            <ResultCard icon={<MapIcon size={18} />} title={t("deltaForce.roulette.map", "地图")}>
              <SlotScroller
                key={`map-${spinKey}`}
                urls={mapPicPool}
                winnerUrl={result.map.pic}
                alt={result.map.map}
                height={180}
                fill
                instant={skipAnimation}
                delay={0}
              />
              <Text fontWeight="semibold" color={textColor}>
                {result.map.map}
              </Text>
              <Badge fontSize="xs" alignSelf="flex-start" {...highlightBadge(6)}>
                {result.map.difficulty}
              </Badge>
            </ResultCard>

            <ResultCard
              icon={<Crosshair size={18} />}
              title={t("deltaForce.roulette.weapon", "主武器")}
            >
              <SlotScroller
                key={`weapon-${spinKey}`}
                urls={weaponPicPool}
                winnerUrl={result.weapon.pic}
                alt={result.weapon.objectName}
                height={180}
                fill
                instant={skipAnimation}
                delay={0.06}
              />
              <Text fontWeight="semibold" color={textColor}>
                {result.weapon.objectName}
              </Text>
              <HStack spacing={2} wrap="wrap">
                {result.weapon.ammoPic && (
                  <HStack spacing={1} align="center">
                    <Box
                      w="28px"
                      h="28px"
                      borderRadius="md"
                      bg="blackAlpha.400"
                      overflow="hidden"
                      p={0.5}
                    >
                      <AmmoThumb url={result.weapon.ammoPic} />
                    </Box>
                    <Badge fontSize="xs" {...highlightBadge(result.weapon.selectedAmmoGrade)}>
                      {t("deltaForce.roulette.ammoGrade", "弹药等级")} {result.weapon.selectedAmmoGrade}
                    </Badge>
                  </HStack>
                )}
                <Text fontSize="sm" color={subTextColor}>
                  {result.weapon.caliber} · {result.weapon.fireMode}
                </Text>
              </HStack>
            </ResultCard>
          </SimpleGrid>

          {/* 下排：干员 / 头盔 / 护甲（正方形，固定卡片尺寸） */}
          <SimpleGrid columns={{ base: 1, md: 3 }} spacing={4}>
            {[
              {
                icon: <User size={18} />,
                title: t("deltaForce.roulette.operator", "干员"),
                pic: result.operatorPic,
                name: result.operatorName,
                badge: null,
                objectFit: "cover" as const,
                pool: operatorPicPool,
                delay: 0.12,
              },
              {
                icon: <HardHat size={18} />,
                title: t("deltaForce.roulette.helmet", "头盔"),
                pic: result.helmetPic,
                name: result.helmetName,
                badge: <Badge fontSize="xs" {...highlightBadge(result.helmetLevel)}>{result.helmetLevel} 级</Badge>,
                objectFit: "contain" as const,
                pool: helmetPicPool,
                delay: 0.18,
              },
              {
                icon: <Shield size={18} />,
                title: t("deltaForce.roulette.armor", "护甲"),
                pic: result.armorPic,
                name: result.armorName,
                badge: <Badge fontSize="xs" {...highlightBadge(result.armorLevel)}>{result.armorLevel} 级</Badge>,
                objectFit: "contain" as const,
                pool: armorPicPool,
                delay: 0.24,
              },
            ].map((item, idx) => (
              <ResultCard key={idx} icon={item.icon} title={item.title}>
                <VStack spacing={2} align="center">
                  <SlotScroller
                    key={`item-${idx}-${spinKey}`}
                    urls={item.pool}
                    winnerUrl={item.pic}
                    alt={item.name}
                    slotW={140}
                    height={140}
                    objectFit={item.objectFit}
                    instant={skipAnimation}
                    delay={item.delay}
                  />
                  <Text fontWeight="semibold" color={textColor} textAlign="center">
                    {item.name}
                  </Text>
                  {item.badge}
                </VStack>
              </ResultCard>
            ))}
          </SimpleGrid>
        </VStack>
      ) : (
        <VStack align="stretch" spacing={4}>
          {/* 上排骨架：地图 / 主武器 */}
          <SimpleGrid columns={{ base: 1, md: 2 }} spacing={4}>
            {[0, 1].map((n) => (
              <ResultCard key={n} icon={<Skeleton w="18px" h="18px" />} title=" ">
                <Box
                  w="100%"
                  h="180px"
                  borderRadius="lg"
                  bg="blackAlpha.200"
                  display="flex"
                  alignItems="center"
                  justifyContent="center"
                >
                  <Text fontSize="5xl" color="whiteAlpha.500" fontWeight="bold">
                    ?
                  </Text>
                </Box>
                <Skeleton w="60%" h="16px" mt={2} />
                <Skeleton w="30%" h="16px" mt={2} />
              </ResultCard>
            ))}
          </SimpleGrid>

          {/* 下排骨架：干员 / 头盔 / 护甲 */}
          <SimpleGrid columns={{ base: 1, md: 3 }} spacing={4}>
            {[0, 1, 2].map((n) => (
              <ResultCard key={n} icon={<Skeleton w="18px" h="18px" />} title=" ">
                <VStack spacing={2} align="center">
                  <Box
                    w="140px"
                    h="140px"
                    borderRadius="lg"
                    bg="blackAlpha.200"
                    display="flex"
                    alignItems="center"
                    justifyContent="center"
                  >
                    <Text fontSize="5xl" color="whiteAlpha.500" fontWeight="bold">
                      ?
                    </Text>
                  </Box>
                  <Skeleton w="70%" h="16px" />
                  <Skeleton w="40%" h="16px" />
                </VStack>
              </ResultCard>
            ))}
          </SimpleGrid>
        </VStack>
      )}
    </Box>
  );
}

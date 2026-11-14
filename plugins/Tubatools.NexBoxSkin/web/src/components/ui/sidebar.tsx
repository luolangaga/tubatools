import { Box as ChakraBox, Flex, IconButton, Text, Tooltip, useColorModeValue, Badge, Image, useColorMode } from "@chakra-ui/react";
import { Home, Wrench, Settings, Cpu, TrendingUp, LogOut, SlidersHorizontal } from "lucide-react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useBackground } from "@/contexts/background-context";
import { useThemeColor } from "@/contexts/theme-color-context";
import { getBorderGlowStyle } from "@/hooks/use-glow-effect";
import { store } from "@/lib/store";
import { gamebox } from "@/lib/gamebox";
import deltaForceIconLight from "@/assets/deltaforce-light.webp";
import deltaForceIconDark from "@/assets/deltaforce-dark.webp";
import { useState, useEffect, useLayoutEffect, useRef, useCallback, useMemo } from "react";

const HIDEABLE_NAV_PATHS = ["/hardware", "/tools", "/optimize", "/display-filter", "/delta-force"];
const getNavStorageKey = (path: string) => `nexbox_nav_visible_${path.replace(/\//g, "").replace(/-/g, "_")}`;

// 同步从 localStorage 预填充导航可见性：store 的读取是异步的，若首帧用空对象初始化，
// 过滤条件 `navVisibility[path] !== false` 会把全部按钮（含默认隐藏的“自定义页面”）先渲染出来，
// 异步加载完成后再隐藏被关掉的项，导致启动时闪一下。切换可见性时会同时写入 localStorage 与 store，
// 二者保持一致，故这里以 localStorage 作为首帧种子即可消除闪烁（存储逻辑与下方 store 加载分支一致）。
function readInitialNavVisibility(): Record<string, boolean> {
  const initial: Record<string, boolean> = {};
  for (const path of HIDEABLE_NAV_PATHS) {
    const raw = localStorage.getItem(getNavStorageKey(path));
    // /custom 默认隐藏（仅显式 "true" 才显示），其余默认显示（仅显式 "false" 才隐藏）
    initial[path] = path === "/custom" ? raw === "true" : raw !== "false";
  }
  return initial;
}

interface NavItem {
  path: string;
  icon: React.ComponentType<{ size?: number; strokeWidth?: number }> | null;
  customIcon?: string;
  customIconDark?: string;
  customIconSize?: string;
  ariaLabel: string;
  beta?: boolean;
  alwaysShowLabel?: boolean;
}

// 长按拖动跳转：按住按钮后，按钮底下的主题色滑块（高亮块）沿导航栏轨道滑动，
// 轨道方向外的移动被忽略（左侧栏只能上下、顶栏只能左右），按钮本体不动；
// 滑块滑到哪个按钮上该按钮高亮为落点，松手即跳转该页面
interface DragState {
  path: string;
  from: number;
  vertical: boolean;
  target: string;
  posX: number;
  posY: number;
  originX: number;
  originY: number;
  baseX: number;
  baseY: number;
  w: number;
  h: number;
  startX: number;
  startY: number;
}

const NAV_ORDER_KEY = "nexbox_nav_order";
const NAV_DRAG_LONG_PRESS_MS = 300;
const NAV_DRAG_MOVE_ACTIVATE_PX = 12;

function NavButton({ item, isActive, isDropTarget, hoverBg, iconColor, activeIconColor, showLabel, registerRef, onPointerDown, onNavClick, shouldBlockBurst }: {
  item: NavItem;
  isActive: boolean;
  isDropTarget?: boolean;
  hoverBg: string;
  iconColor: string;
  activeIconColor: string;
  showLabel: boolean;
  registerRef?: (el: HTMLAnchorElement | null) => void;
  onPointerDown?: (e: React.PointerEvent) => void;
  onNavClick?: (e: React.MouseEvent) => void;
  shouldBlockBurst?: () => boolean;
}) {
  const isCustom = !!item.customIcon || !!item.customIconDark;
  // 文字是否显示完全跟随全局设置（无字模式下不显示文字）；alwaysShowLabel 仅用于控制图标放大
  const showText = showLabel;
  const { colorMode } = useColorMode();

  // 浅色模式用 light 图标（hei黑底），深色模式用 dark 图标（bai白底）
  const resolvedCustomIcon = colorMode === "dark"
    ? (item.customIconDark || item.customIcon)
    : (item.customIcon || item.customIconDark);

  // 仅对始终显示标签的项（如 Steam）在文字模式下放大图标，其他自定义图标项保持原尺寸
  const resolvedIconSize = (showText && item.alwaysShowLabel && item.customIconSize && item.customIconSize !== "22px")
    ? "30px"
    : (item.customIconSize || "22px");
  const iconElement = isCustom ? (
    <Image
      src={resolvedCustomIcon}
      alt={item.ariaLabel}
      w={resolvedIconSize}
      h={resolvedIconSize}
      objectFit="contain"
      filter={isActive ? "none" : "grayscale(30%) opacity(0.7)"}
      transition="filter 0.2s"
    />
  ) : (
    <item.icon size={20} strokeWidth={2.2} />
  );

  return (
    <Link
      ref={registerRef}
      to={item.path}
      className="jelly-bounce-nav-button"
      draggable={false}
      onDragStart={(e) => e.preventDefault()}
      onPointerDown={onPointerDown}
      onClick={(e) => {
        // 拖动中 / 松手派生点击 / 刚完成 drop 的短时间窗内：拦截导航避免误触
        if (shouldBlockBurst?.()) {
          e.preventDefault();
          e.stopPropagation();
          return;
        }
        onNavClick?.(e);
      }}
      style={{ position: "relative", zIndex: 1, transition: "all 0.4s cubic-bezier(0.4, 0, 0.2, 1)", flexShrink: 0, display: "flex" }}
    >
      <ChakraBox position="relative">
        {showText ? (
          <Flex
            direction="column"
            align="center"
            justify="center"
            gap={0.5}
            aria-label={item.ariaLabel}
            w="48px"
            h="48px"
            borderRadius="xl"
            cursor="pointer"
            bg={isDropTarget ? hoverBg : "transparent"}
            color={isActive || isDropTarget ? activeIconColor : iconColor}
            _hover={{ bg: isActive ? "transparent" : hoverBg }}
            transition="all 0.2s cubic-bezier(0.4, 0, 0.2, 1)"
            as="span"
            role="button"
            tabIndex={0}
            position="relative"
            overflow="hidden"
          >
            <ChakraBox display="flex" alignItems="center" justifyContent="center" lineHeight={0} position="relative" zIndex={1} overflow="visible" sx={item.alwaysShowLabel && showText ? { transform: "scale(1.4)", transformOrigin: "center" } : undefined}>
              {iconElement}
            </ChakraBox>
            <Text fontSize="2xs" fontWeight="medium" noOfLines={1} textAlign="center" lineHeight="1.1" position="relative" zIndex={1}>
              {item.ariaLabel}
            </Text>
          </Flex>
        ) : (
          <ChakraBox position="relative" overflow="hidden" borderRadius="xl">
            <ChakraBox position="relative" zIndex={1}>
              <IconButton
                aria-label={item.ariaLabel}
                icon={iconElement}
                variant="ghost"
                borderRadius="xl"
                bg={isDropTarget ? hoverBg : "transparent"}
                color={isActive || isDropTarget ? activeIconColor : iconColor}
                _hover={{ bg: isActive ? "transparent" : hoverBg }}
                _active={{ transform: "scale(0.95)" }}
                transition="all 0.2s cubic-bezier(0.4, 0, 0.2, 1)"
                size="lg"
                w="48px"
                h="48px"
              />
            </ChakraBox>
          </ChakraBox>
        )}
        {!showText && item.beta && (
          <Badge
            position="absolute"
            top="-2px"
            right="-2px"
            colorScheme="purple"
            fontSize="8px"
            px={1}
            py={0}
            borderRadius="full"
            textTransform="uppercase"
            fontWeight="bold"
          >
            BETA
          </Badge>
        )}
      </ChakraBox>
    </Link>
  );
}

export function Sidebar() {
  const location = useLocation();
  const navigate = useNavigate();
  const { t } = useTranslation();
  const { liquidGlassEnabled, liquidGlassBlur, jellyBounceEnabled } = useBackground();
  const { getActiveColor, getHoverColor, getContrastTextColor } = useThemeColor();
  // 默认显示文字标签（未配置过时为开；显式存过 "false" 的用户保持关闭）
  const [showLabel, setShowLabel] = useState(() => localStorage.getItem("nexbox_sidebar_show_label") !== "false");
  const [navPosition, setNavPosition] = useState<"left" | "top">("left");

  useEffect(() => {
    (async () => {
      let v = await store.get<boolean>("nexbox_sidebar_show_label");
      if (v !== null && v !== undefined) {
        setShowLabel(v);
      } else {
        setShowLabel(localStorage.getItem("nexbox_sidebar_show_label") !== "false");
      }
      let nv = await store.get<string>("nexbox_nav_position");
      if (nv === "top" || nv === "left") {
        setNavPosition(nv);
      } else {
        setNavPosition(localStorage.getItem("nexbox_nav_position") === "top" ? "top" : "left");
      }
    })();
  }, []);

  // 果冻弹跳效果：当导航栏状态切换（位置/标签/可见性）时触发
  const sidebarContentRef = useRef<HTMLDivElement>(null);
  const sidebarContainerRef = useRef<HTMLDivElement>(null);
  const isFirstRender = useRef(true);

  const triggerJellyBounce = useCallback(() => {
    if (!jellyBounceEnabled) return;
    // 触发内容区弹跳
    const contentEl = sidebarContentRef.current;
    if (contentEl) {
      contentEl.classList.remove("jelly-bouncing");
      void contentEl.offsetWidth;
      contentEl.classList.add("jelly-bouncing");
    }
    // 触发外层容器弹跳
    const containerEl = sidebarContainerRef.current;
    if (containerEl) {
      containerEl.classList.remove("jelly-bouncing");
      void containerEl.offsetWidth;
      containerEl.classList.add("jelly-bouncing");
    }
  }, [jellyBounceEnabled]);
  
  useEffect(() => {
    const handler = (e: CustomEvent) => {
      setShowLabel(e.detail === true);
    };
    window.addEventListener("sidebar-show-label-changed", handler as EventListener);
    return () => {
      window.removeEventListener("sidebar-show-label-changed", handler as EventListener);
    };
  }, []);

  useEffect(() => {
    const handler = (e: CustomEvent) => {
      const val = e.detail;
      setNavPosition(val === "top" ? "top" : "left");
    };
    window.addEventListener("nav-position-changed", handler as EventListener);
    return () => {
      window.removeEventListener("nav-position-changed", handler as EventListener);
    };
  }, []);

  const isTop = navPosition === "top";
  
  const [navVisibility, setNavVisibility] = useState<Record<string, boolean>>(readInitialNavVisibility);
  const [navOrder, setNavOrder] = useState<string[] | null>(null);

  useEffect(() => {
    (async () => {
      const initial: Record<string, boolean> = {};
      for (const path of HIDEABLE_NAV_PATHS) {
        const key = getNavStorageKey(path);
        let vis = await store.get<boolean>(key);
        if (vis === null || vis === undefined) {
          if (path === "/custom") {
            vis = localStorage.getItem(key) === "true";
          } else {
            vis = localStorage.getItem(key) !== "false";
          }
        }
        initial[path] = vis;
      }
      setNavVisibility(initial);

      // 读取导航栏排序
      let orderStr = await store.get<string>("nexbox_nav_order");
      if (orderStr) {
        try {
          const parsed = JSON.parse(orderStr) as string[];
          const allPaths = ["/hardware", "/tools", "/optimize", "/delta-force"];
          let changed = false;
          for (const p of allPaths) {
            if (!parsed.includes(p)) { parsed.push(p); changed = true; }
          }
          if (changed) {
            parsed.sort((a: string, b: string) => allPaths.indexOf(a) - allPaths.indexOf(b));
            await store.set("nexbox_nav_order", JSON.stringify(parsed));
            await store.save();
          }
          setNavOrder(parsed);
        } catch { setNavOrder(null); }
      } else {
        try {
          const saved = localStorage.getItem(NAV_ORDER_KEY);
          if (saved) {
            const parsed = JSON.parse(saved) as string[];
            const allPaths = ["/hardware", "/tools", "/optimize", "/delta-force"];
            let changed = false;
            for (const p of allPaths) {
              if (!parsed.includes(p)) { parsed.push(p); changed = true; }
            }
            if (changed) {
              parsed.sort((a: string, b: string) => allPaths.indexOf(a) - allPaths.indexOf(b));
              localStorage.setItem(NAV_ORDER_KEY, JSON.stringify(parsed));
            }
            setNavOrder(parsed);
          }
        } catch {}
      }
    })();
  }, []);

  // 监听排序变化
  useEffect(() => {
    const handler = async () => {
      try {
        let saved = await store.get<string>("nexbox_nav_order");
        if (saved) {
          setNavOrder(JSON.parse(saved));
        } else {
          const ls = localStorage.getItem(NAV_ORDER_KEY);
          setNavOrder(ls ? JSON.parse(ls) : null);
        }
      } catch { setNavOrder(null); }
    };
    window.addEventListener("nav-order-changed", handler as EventListener);
    return () => { window.removeEventListener("nav-order-changed", handler as EventListener); };
  }, []);

  useEffect(() => {
    const handler = (e: CustomEvent) => {
      const { path, visible } = e.detail || {};
      if (path) {
        setNavVisibility(prev => ({ ...prev, [path]: visible }));
      }
    };
    window.addEventListener("nav-visibility-changed", handler as EventListener);
    return () => { window.removeEventListener("nav-visibility-changed", handler as EventListener); };
  }, []);

  // 监听导航栏状态变化/页面切换，触发果冻弹跳动画（跳过初次渲染）
  const navVisibilityKey = JSON.stringify(navVisibility);
  const navOrderKey = JSON.stringify(navOrder);
  useEffect(() => {
    if (isFirstRender.current) {
      return;
    }
    triggerJellyBounce();
  }, [navPosition, showLabel, navVisibilityKey, location.pathname, triggerJellyBounce]);

  useEffect(() => {
    isFirstRender.current = false;
  }, []);

  // 模糊立即生效
  const effectiveBlur = liquidGlassEnabled ? liquidGlassBlur : 0;
  const backdropFilter = `blur(${effectiveBlur}px)`;

  const defaultBgColor = useColorModeValue("rgba(255,255,255,1)", "rgba(17,17,17,1)");
  const glassBgColor = useColorModeValue("rgba(255,255,255,0.25)", "rgba(0,0,0,0.25)");
  const defaultBorderColor = useColorModeValue("rgba(200,200,200,0.3)", "rgba(51,51,51,0.5)");
  const glassBorderColor = useColorModeValue("rgba(255,255,255,0.2)", "rgba(255,255,255,0.1)");
  const glowColor = useColorModeValue("rgba(255,255,255,0.8)", "rgba(255,255,255,0.5)");
  
  const iconColor = useColorModeValue("rgba(0,0,0,0.75)", "rgba(255,255,255,0.8)");

  const activeBg = getActiveColor();
  const hoverBg = getHoverColor(true);
  const activeIconColor = getContrastTextColor();

  const navItems: NavItem[] = [
    { path: "/", icon: Home, ariaLabel: t("sidebar.home") },
    { path: "/hardware", icon: Cpu, ariaLabel: t("sidebar.hardware") },
    { path: "/tools", icon: Wrench, ariaLabel: t("sidebar.tools") },
    { path: "/optimize", icon: TrendingUp, ariaLabel: t("sidebar.optimization") },
    { path: "/display-filter", icon: SlidersHorizontal, ariaLabel: t("sidebar.displayFilter") },
    { path: "/delta-force", icon: null, customIcon: deltaForceIconLight, customIconDark: deltaForceIconDark, customIconSize: "20px", ariaLabel: t("sidebar.deltaForce") },
    { path: "/settings", icon: Settings, ariaLabel: t("sidebar.settings") },
  ];

  // 根据保存的顺序排序 navItems（首页和设置固定首尾）
  const sortedNavItems = useMemo(() => {
    if (!navOrder) return navItems;
    const map = new Map(navItems.map(i => [i.path, i]));
    const ordered: NavItem[] = [];
    // 首页始终在最前
    const home = map.get("/");
    if (home) ordered.push(home);
    // 按 navOrder 排序中间项
    for (const p of navOrder) {
      if (p === "/" || p === "/settings") continue;
      const item = map.get(p);
      if (item) ordered.push(item);
    }
    // 追加未在 order 中的新项
    navItems.forEach(item => {
      if (item.path === "/" || item.path === "/settings") return;
      if (!ordered.find(i => i.path === item.path)) ordered.push(item);
    });
    // 设置始终在最后
    const settings = map.get("/settings");
    if (settings) ordered.push(settings);
    return ordered;
  }, [navItems, navOrder]);

  const visibleNavItems = sortedNavItems.filter(item => {
    if (item.path === "/" || item.path === "/settings") return true;
    return navVisibility[item.path] !== false;
  });

  // ── 滑动高亮块（active pill）：高亮背景不再画在按钮上，而是由一个独立的色块
  //    通过 transform 从旧按钮平滑滑动到新按钮 ──
  const itemEls = useRef<Map<string, HTMLAnchorElement>>(new Map());
  const [pill, setPill] = useState<{ x: number; y: number; w: number; h: number } | null>(null);
  const pillAnimatedRef = useRef(false);

  const measurePill = useCallback(() => {
    const el = itemEls.current.get(location.pathname);
    if (!el) { setPill(null); return; }
    // 必须用布局坐标（offsetLeft/Top）而不是 getBoundingClientRect：
    // 后者会把果冻弹跳等 transform 缩放算进去，导致高亮块停偏、图标不居中
    if (el.offsetWidth === 0 || el.offsetHeight === 0) { setPill(null); return; }
    setPill({ x: el.offsetLeft, y: el.offsetTop, w: el.offsetWidth, h: el.offsetHeight });
    pillAnimatedRef.current = true;
  }, [location.pathname]);
  const measurePillRef = useRef<() => void>(() => {});
  measurePillRef.current = measurePill;

  // 路由/布局变化时立即测量（paint 前，避免首帧高亮缺失），并延时校准（等布局过渡/字体加载完成）
  useLayoutEffect(() => {
    measurePill();
    const t = setTimeout(() => measurePill(), 550);
    return () => clearTimeout(t);
  }, [measurePill, showLabel, navVisibilityKey, navOrderKey, isTop]);

  // 容器尺寸变化（位置切换的 max-width 过渡、窗口缩放）时跟随测量
  useEffect(() => {
    const el = sidebarContainerRef.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const ro = new ResizeObserver(() => measurePillRef.current());
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  // ── 长按拖动跳转：按住中间任意按钮 350ms 抬起为"滑块"，拖到其他按钮上松手即跳转 ──
  const [drag, setDrag] = useState<DragState | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const dragMetaRef = useRef<{ paths: string[]; centers: Array<{ x: number; y: number }> } | null>(null);
  const pressRef = useRef<{ path: string; index: number; startX: number; startY: number; timer: number } | null>(null);
  const suppressClickRef = useRef(false);
  const lastDropRef = useRef(0);
  const visiblePathsRef = useRef<string[]>([]);

  visiblePathsRef.current = visibleNavItems.map(i => i.path);

  const beginDrag = (path: string, index: number, startX: number, startY: number) => {
    const paths = visiblePathsRef.current;
    if (paths.length < 2) return;
    const centers = paths.map(p => {
      const el = itemEls.current.get(p);
      if (!el) return { x: 0, y: 0 };
      const r = el.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    });
    const el = itemEls.current.get(path);
    // 记录内容区视口原点，用于把光标的视口坐标换算为滑块的布局坐标
    const flexRect = sidebarContentRef.current?.getBoundingClientRect();
    dragMetaRef.current = { paths, centers };
    suppressClickRef.current = true;
    document.body.style.userSelect = "none";
    document.body.style.cursor = "grabbing";
    const vertical = !isTop;
    const w = el?.offsetWidth ?? 48;
    const h = el?.offsetHeight ?? 48;
    const originX = el?.offsetLeft ?? 0;
    const originY = el?.offsetTop ?? 0;
    const baseX = flexRect?.left ?? 0;
    const baseY = flexRect?.top ?? 0;
    const trackFirst = centers[0];
    const trackLast = centers[centers.length - 1];
    const trackStart = vertical ? trackFirst.y : trackFirst.x;
    const trackEnd = vertical ? trackLast.y : trackLast.x;
    // 拖动开始时滑块直接出现在光标处（不做从原位置滑入的动画）
    const center0 = Math.max(trackStart, Math.min(trackEnd, vertical ? startY : startX));
    const st: DragState = {
      path,
      from: index,
      vertical,
      startX,
      startY,
      target: path,
      posX: vertical ? originX : (center0 - baseX) - w / 2,
      posY: vertical ? (center0 - baseY) - h / 2 : originY,
      originX,
      originY,
      baseX,
      baseY,
      w,
      h,
    };
    dragRef.current = st;
    setDrag(st);
  };
  const beginDragRef = useRef<(path: string, index: number, startX: number, startY: number) => void>(() => {});
  beginDragRef.current = beginDrag;

  const finishDragRef = useRef<() => void>(() => {});
  finishDragRef.current = () => {
    const d = dragRef.current;
    dragRef.current = null;
    dragMetaRef.current = null;
    setDrag(null);
    document.body.style.userSelect = "";
    document.body.style.cursor = "";
    lastDropRef.current = performance.now();
    window.setTimeout(() => { suppressClickRef.current = false; }, 100);
    if (!d) return;
    // 滑块从松手位置以弹性动画滑向落点按钮（落点不精准时自动归位到最近按钮），
    // 不再跳回原位置，避免原位置闪一下
    const targetEl = itemEls.current.get(d.target);
    if (targetEl) {
      setPill({ x: targetEl.offsetLeft, y: targetEl.offsetTop, w: targetEl.offsetWidth, h: targetEl.offsetHeight });
      pillAnimatedRef.current = true;
    }
    // 松手时滑块停在哪个按钮上就跳转到该页面（跳回当前页则无操作）
    if (d.target !== location.pathname) navigate(d.target);
  };

  useEffect(() => {
    const cancelPress = () => {
      const press = pressRef.current;
      if (press) { clearTimeout(press.timer); pressRef.current = null; }
    };
    const onMove = (e: PointerEvent) => {
      // 按住后移动超过阈值立即进入拖动（不必等长按计时器）
      const press = pressRef.current;
      if (press && Math.hypot(e.clientX - press.startX, e.clientY - press.startY) > NAV_DRAG_MOVE_ACTIVATE_PX) {
        clearTimeout(press.timer);
        pressRef.current = null;
        beginDragRef.current(press.path, press.index, press.startX, press.startY);
      }
      const d = dragRef.current;
      if (!d) return;
      const meta = dragMetaRef.current;
      if (!meta) return;
      // 滑块只能沿导航栏轨道滑动（左侧栏上下、顶栏左右），垂直于轨道方向的移动忽略；
      // 滑块中心直接取光标在轨道轴上的投影（视口坐标），夹在轨道两端按钮中心之间，
      // 再减去内容区的视口原点换算为布局坐标（否则滑块会被内容区的视口偏移推离光标）
      const pointer = d.vertical ? e.clientY : e.clientX;
      const first = meta.centers[0];
      const last = meta.centers[meta.centers.length - 1];
      const trackStart = d.vertical ? first.y : first.x;
      const trackEnd = d.vertical ? last.y : last.x;
      const center = Math.max(trackStart, Math.min(trackEnd, pointer));
      let target = d.path;
      let best = Infinity;
      meta.paths.forEach((p, i) => {
        const c = meta.centers[i];
        const dist = Math.abs((d.vertical ? c.y : c.x) - center);
        if (dist < best) { best = dist; target = p; }
      });
      const posX = d.vertical ? d.originX : (center - d.baseX) - d.w / 2;
      const posY = d.vertical ? (center - d.baseY) - d.h / 2 : d.originY;
      if (target !== d.target || posX !== d.posX || posY !== d.posY) {
        const next: DragState = { ...d, target, posX, posY };
        dragRef.current = next;
        setDrag(next);
      }
    };
    const onUp = () => {
      cancelPress();
      if (dragRef.current) finishDragRef.current();
    };
    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
    window.addEventListener("pointercancel", onUp);
    window.addEventListener("blur", onUp);
    return () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
      window.removeEventListener("pointercancel", onUp);
      window.removeEventListener("blur", onUp);
    };
  }, []);

  const handleNavPointerDown = (item: NavItem, index: number, e: React.PointerEvent) => {
    if (e.button !== 0) return;
    if (dragRef.current || pressRef.current) return;
    const startX = e.clientX;
    const startY = e.clientY;
    const timer = window.setTimeout(() => {
      pressRef.current = null;
      beginDrag(item.path, index, startX, startY);
    }, NAV_DRAG_LONG_PRESS_MS);
    pressRef.current = { path: item.path, index, startX, startY, timer };
  };

  // 拖动激活后拦截由松手派生的 click，避免误导航
  const handleNavClick = useCallback((e: React.MouseEvent) => {
    if (!suppressClickRef.current) return;
    e.preventDefault();
    e.stopPropagation();
  }, []);

  const shouldBlockBurst = useCallback(() =>
    suppressClickRef.current || dragRef.current !== null || performance.now() - lastDropRef.current < 300
  , []);

  // 拖动时按钮底下的主题色滑块沿导航栏轨道滑动（按钮本体不动）
  const pillRect = drag
    ? { x: drag.posX, y: drag.posY, w: drag.w, h: drag.h }
    : pill;

  const sidebarContent = (
    <Flex
      ref={sidebarContentRef}
      className="jelly-bounce-sidebar-content"
      direction="row" wrap="wrap" gap={3} align="center" justify="center" transition="gap 0.4s cubic-bezier(0.4, 0, 0.2, 1)"
      position="relative"
      userSelect="none"
      pointerEvents={drag ? "none" : undefined}
      onAnimationEnd={(e: React.AnimationEvent) => {
        if (e.animationName === "jellyBounce") {
          sidebarContentRef.current?.classList.remove("jelly-bouncing");
        }
      }}
    >
      {pillRect && (
        <ChakraBox
          aria-hidden
          pointerEvents="none"
          position="absolute"
          top={0}
          left={0}
          zIndex={0}
          borderRadius="xl"
          bg={activeBg}
          w={`${pillRect.w}px`}
          h={`${pillRect.h}px`}
          style={{
            transform: `translate(${pillRect.x}px, ${pillRect.y}px)`,
            willChange: "transform",
            // 拖动中无过渡 1:1 跟手；松手/点击后弹性滑向落点按钮
            transition: !pillAnimatedRef.current || drag
              ? "none"
              : "transform 0.45s cubic-bezier(0.34, 1.3, 0.4, 1), width 0.45s cubic-bezier(0.34, 1.3, 0.4, 1), height 0.45s cubic-bezier(0.34, 1.3, 0.4, 1)",
          }}
        />
      )}
      {visibleNavItems.map((item, index) => (
        <NavButton
          key={item.path}
          item={item}
          isActive={location.pathname === item.path}
          isDropTarget={drag?.target === item.path}
          hoverBg={hoverBg}
          iconColor={iconColor}
          activeIconColor={activeIconColor}
          showLabel={showLabel}
          registerRef={(el) => { if (el) itemEls.current.set(item.path, el); else itemEls.current.delete(item.path); }}
          onPointerDown={(e) => handleNavPointerDown(item, index, e)}
          onNavClick={handleNavClick}
          shouldBlockBurst={shouldBlockBurst}
        />
      ))}
    </Flex>
  );

  const containerTransition = "max-width 0.25s cubic-bezier(0.95, 0, 1, 1), left 0.4s cubic-bezier(0.4, 0, 0.2, 1), top 0.4s cubic-bezier(0.4, 0, 0.2, 1), transform 0.4s cubic-bezier(0.4, 0, 0.2, 1), padding 0.4s cubic-bezier(0.4, 0, 0.2, 1)";

  const leftContainerStyles = {
    position: "fixed" as const,
    left: 6,
    top: "50%",
    transform: "translateY(-50%) translateZ(0)",
    zIndex: 40,
    borderRadius: "3xl",
    boxShadow: "2xl",
    py: 6,
    px: 2,
    maxWidth: "64px",
    transition: containerTransition,
    sx: { WebkitBackfaceVisibility: "hidden" as const, backfaceVisibility: "hidden" as const },
  };

  const topContainerStyles = {
    position: "fixed" as const,
    top: "8px",
    left: "50%",
    transform: "translateX(-50%) translateZ(0)",
    zIndex: 40,
    borderRadius: "3xl",
    boxShadow: "2xl",
    py: 2,
    px: 3,
    maxWidth: "2000px",
    width: "max-content",
    transition: containerTransition,
    sx: { WebkitBackfaceVisibility: "hidden" as const, backfaceVisibility: "hidden" as const },
  };

  const containerStyles = isTop ? topContainerStyles : leftContainerStyles;

  return (
    <ChakraBox
      ref={sidebarContainerRef}
      id="main-sidebar"
      className={isTop ? "jelly-bounce-sidebar-top" : "jelly-bounce-sidebar-left"}
      {...containerStyles}
      bg={liquidGlassEnabled ? glassBgColor : defaultBgColor}
      border="1px solid"
      borderColor={liquidGlassEnabled ? glassBorderColor : defaultBorderColor}
      backdropFilter={backdropFilter}
      transition="max-width 0.25s cubic-bezier(0.95, 0, 1, 1), left 0.4s cubic-bezier(0.4, 0, 0.2, 1), top 0.4s cubic-bezier(0.4, 0, 0.2, 1), transform 0.4s cubic-bezier(0.4, 0, 0.2, 1), padding 0.4s cubic-bezier(0.4, 0, 0.2, 1), background 0.45s cubic-bezier(0.4, 0, 0.2, 1)"
      sx={{
        ...containerStyles.sx,
        willChange: "auto",
      }}
      onAnimationEnd={(e: React.AnimationEvent) => {
        const name = e.animationName;
        if (name === "jellyBounceSidebarLeft" || name === "jellyBounceSidebarTop") {
          sidebarContainerRef.current?.classList.remove("jelly-bouncing");
        }
      }}
    >
      <ChakraBox
        style={getBorderGlowStyle(glowColor)}
        opacity={liquidGlassEnabled ? 1 : 0}
        transition="opacity 0.45s cubic-bezier(0.4, 0, 0.2, 1)"
      />
      {sidebarContent}
      <ChakraBox
        mt={2}
        pt={2}
        borderTop="1px solid"
        borderColor={liquidGlassEnabled ? glassBorderColor : defaultBorderColor}
        display="flex"
        justifyContent="center"
      >
        <Tooltip label="返回经典界面" placement={isTop ? "bottom" : "right"} hasArrow>
          <IconButton
            aria-label="返回经典界面"
            icon={<LogOut size={16} />}
            size="sm"
            variant="ghost"
            borderRadius="xl"
            color={iconColor}
            onClick={() => void gamebox.setSkin(false)}
          />
        </Tooltip>
      </ChakraBox>
    </ChakraBox>
  );
}

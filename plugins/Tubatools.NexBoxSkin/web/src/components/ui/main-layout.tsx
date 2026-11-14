"use client";

import { Box, useColorModeValue } from "@chakra-ui/react";
import { ReactNode, useEffect, useLayoutEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { Sidebar } from "./sidebar";
import { FloatingScrollbar } from "./floating-scrollbar";
import { useBackground } from "@/contexts/background-context";
import { useThemeColor } from "@/contexts/theme-color-context";
import { store } from "@/lib/store";
import { useEscBack } from "@/hooks/use-esc-back";

export type LayoutMode = "skin" | "embedded";

export function useNavPosition() {
  const [navPosition, setNavPosition] = useState<"left" | "top">("left");

  useEffect(() => {
    (async () => {
      const saved = await store.get<string>("nexbox_nav_position");
      if (saved === "top" || saved === "left") {
        setNavPosition(saved);
      } else {
        setNavPosition(localStorage.getItem("nexbox_nav_position") === "top" ? "top" : "left");
      }
    })();
  }, []);

  useEffect(() => {
    const handler = (event: Event) => {
      const detail = (event as CustomEvent).detail;
      setNavPosition(detail === "top" ? "top" : "left");
    };
    window.addEventListener("nav-position-changed", handler);
    return () => window.removeEventListener("nav-position-changed", handler);
  }, []);

  return navPosition;
}

interface MainLayoutProps {
  children: ReactNode;
  mode?: LayoutMode;
}

export function MainLayout({ children, mode = "skin" }: MainLayoutProps) {
  const bgColor = useColorModeValue("#fafafa", "#0a0a0a");
  const {
    backgroundMode,
    customBgImages,
    activeBgIndex,
    activePresetIndex,
    presetBackgrounds,
    backgroundBlur,
    jellyBounceEnabled,
  } = useBackground();
  const { config } = useThemeColor();
  const navPosition = useNavPosition();
  const isEmbedded = mode === "embedded";
  useEscBack();
  const location = useLocation();
  const idCounter = useRef(0);

  useLayoutEffect(() => {
    const element = document.getElementById("app-main-scroll");
    if (element) {
      element.scrollTop = 0;
      element.scrollLeft = 0;
    }
  }, [location.pathname]);

  const activeImage = customBgImages[activeBgIndex];
  const activePreset = presetBackgrounds[activePresetIndex];
  const showImageBg = backgroundMode === "image" && activeImage;
  const showPresetBg = backgroundMode === "preset" && activePreset;
  const showMrBg = backgroundMode === "mr";

  interface BgLayer {
    url: string;
    id: number;
    fading: boolean;
  }
  const [bgLayers, setBgLayers] = useState<BgLayer[]>([]);

  useEffect(() => {
    const src = showPresetBg ? activePreset?.path ?? null : showImageBg ? activeImage : null;
    if (!src) {
      setBgLayers([]);
      return;
    }
    const newId = ++idCounter.current;
    setBgLayers((previous) => [
      ...previous.map((layer) => ({ ...layer, fading: true })),
      { url: src, id: newId, fading: false },
    ]);
    const timer = window.setTimeout(() => {
      setBgLayers((previous) => previous.filter((layer) => layer.id === newId));
    }, 600);
    return () => window.clearTimeout(timer);
  }, [showPresetBg, activePresetIndex, showImageBg, activeBgIndex, activeImage, activePreset]);

  useEffect(() => {
    const next = showImageBg || showPresetBg || showMrBg ? "transparent" : bgColor;
    document.body.style.backgroundColor = next;
    return () => {
      document.body.style.backgroundColor = "";
    };
  }, [showImageBg, showPresetBg, showMrBg, bgColor]);

  useEffect(() => {
    if (jellyBounceEnabled) {
      document.body.classList.add("jelly-bounce-enabled");
    } else {
      document.body.classList.remove("jelly-bounce-enabled");
      document.body.classList.remove("jelly-bounce-active");
    }
    return () => {
      document.body.classList.remove("jelly-bounce-enabled");
      document.body.classList.remove("jelly-bounce-active");
    };
  }, [jellyBounceEnabled]);

  useEffect(() => {
    if (!jellyBounceEnabled) return;
    document.body.classList.remove("jelly-bounce-active");
    void document.body.offsetWidth;
    document.body.classList.add("jelly-bounce-active");
    const timer = window.setTimeout(() => {
      document.body.classList.remove("jelly-bounce-active");
    }, 700);
    return () => {
      window.clearTimeout(timer);
      document.body.classList.remove("jelly-bounce-active");
    };
  }, [location.pathname, jellyBounceEnabled]);

  const isNavTop = navPosition === "top";

  return (
    <Box position="relative" height="100%" minHeight="100%" bg="transparent">
      <Box
        sx={{
          "@keyframes bgFadeIn": {
            from: { opacity: 0 },
            to: { opacity: 1 },
          },
        }}
      >
        {bgLayers.map((layer) => (
          <Box
            key={layer.id}
            position="fixed"
            top={0}
            left={0}
            right={0}
            bottom={0}
            zIndex={0}
            opacity={layer.fading ? 0 : 1}
            overflow="hidden"
            transition={layer.fading ? "opacity 0.5s ease-in-out" : undefined}
            animation={!layer.fading ? "bgFadeIn 0.5s ease-in-out" : undefined}
          >
            <img
              src={layer.url}
              alt=""
              style={{
                width: "100%",
                height: "100%",
                objectFit: "cover",
                display: "block",
                filter: backgroundBlur > 0 ? `blur(${backgroundBlur}px)` : undefined,
              }}
            />
          </Box>
        ))}
      </Box>

      {showMrBg && (
        <Box
          position="fixed"
          inset={0}
          zIndex={0}
          bgGradient={`radial(circle at 20% 20%, ${config.primaryColor}33, transparent 45%), radial(circle at 80% 70%, #7C9EFF22, transparent 45%)`}
          filter={backgroundBlur > 0 ? `blur(${backgroundBlur}px)` : undefined}
        />
      )}

      {!isEmbedded && <Sidebar />}

      <Box
        id="app-main-scroll"
        tabIndex={-1}
        outline="none"
        position="relative"
        zIndex={1}
        ml={isEmbedded ? 0 : isNavTop ? 0 : "96px"}
        pt={isEmbedded ? 0 : isNavTop ? "76px" : "12px"}
        pb={isEmbedded ? 0 : 6}
        px={isEmbedded ? 0 : 4}
        pr={isEmbedded ? 0 : 8}
        overflowY="auto"
        overflowX="hidden"
        h="100%"
        sx={{
          scrollbarGutter: "stable",
          "&::-webkit-scrollbar": { width: "6px", height: "6px" },
          "&::-webkit-scrollbar-track": { background: "transparent", margin: "10px 0" },
          "&::-webkit-scrollbar-thumb": {
            background: config.primaryColor,
            borderRadius: "3px",
            minHeight: "40px",
          },
        }}
      >
        <Box position="relative" minHeight="100%">
          {children}
        </Box>
      </Box>

      {!isEmbedded && <FloatingScrollbar targetId="app-main-scroll" />}
    </Box>
  );
}

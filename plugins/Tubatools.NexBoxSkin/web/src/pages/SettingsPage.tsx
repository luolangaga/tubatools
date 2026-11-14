import {
  Badge,
  Box,
  Button,
  Divider,
  HStack,
  Heading,
  SimpleGrid,
  Slider,
  SliderFilledTrack,
  SliderThumb,
  SliderTrack,
  Switch,
  Text,
  VStack,
  useColorModeValue,
  useToast,
} from "@chakra-ui/react";
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import {
  Cpu,
  Eye,
  EyeOff,
  Info,
  Laptop,
  Moon,
  Palette,
  Settings,
  Sun,
} from "lucide-react";
import { useTranslation } from "react-i18next";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { useBackground } from "@/contexts/background-context";
import { useThemeColor } from "@/contexts/theme-color-context";
import { useThemeMode } from "@/contexts/theme-mode-context";
import { gamebox } from "@/lib/gamebox";
import { setLanguagePreference } from "@/lib/i18n";
import { resolveScheme } from "@/lib/theme";

const ACCENTS = ["#98DDD0", "#7C9EFF", "#FF8FA3", "#FFB86B", "#A78BFA", "#4ADE80"];

function Row({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children: React.ReactNode;
}) {
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  return (
    <HStack justify="space-between" align="center" spacing={4} py={2}>
      <Box minW={0}>
        <Text fontWeight="medium" fontSize="sm">
          {title}
        </Text>
        {description && (
          <Text fontSize="xs" color={sub} mt={0.5}>
            {description}
          </Text>
        )}
      </Box>
      <Box flexShrink={0}>{children}</Box>
    </HStack>
  );
}

export default function SettingsPage() {
  const toast = useToast();
  const { t, i18n } = useTranslation();
  const adaptive = useAdaptiveTextColor();
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  const border = useColorModeValue("gray.200", "#333333");
  const { themeMode, setThemeMode } = useThemeMode();
  const { config, setPrimaryColor, resetToDefault } = useThemeColor();
  const {
    liquidGlassEnabled,
    setLiquidGlassEnabled,
    liquidGlassBlur,
    setLiquidGlassBlur,
    jellyBounceEnabled,
    setJellyBounceEnabled,
  } = useBackground();
  const [skinEnabled, setSkinEnabled] = useState(false);
  const [skinBusy, setSkinBusy] = useState(false);
  const [showLabel, setShowLabel] = useState(
    () => localStorage.getItem("nexbox_sidebar_show_label") !== "false",
  );
  const [navPosition, setNavPosition] = useState<"left" | "top">(
    () => (localStorage.getItem("nexbox_nav_position") === "top" ? "top" : "left"),
  );

  useEffect(() => {
    let alive = true;
    gamebox
      .skin()
      .then((state) => {
        if (alive) setSkinEnabled(Boolean(state?.enabled));
      })
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, []);

  const toggleSkin = async (enabled: boolean) => {
    setSkinBusy(true);
    try {
      await gamebox.setSkin(enabled);
      setSkinEnabled(enabled);
      toast({
        title: enabled ? "已启用 NexBox 皮肤" : "已恢复经典界面",
        status: "success",
        duration: 2200,
      });
    } catch (error) {
      toast({
        title: "皮肤切换失败",
        description: String(error),
        status: "error",
        duration: 4000,
      });
    } finally {
      setSkinBusy(false);
    }
  };

  const changeShowLabel = (value: boolean) => {
    setShowLabel(value);
    localStorage.setItem("nexbox_sidebar_show_label", value ? "true" : "false");
    window.dispatchEvent(new CustomEvent("sidebar-show-label-changed", { detail: value }));
  };

  const changeNavPosition = (value: "left" | "top") => {
    setNavPosition(value);
    localStorage.setItem("nexbox_nav_position", value);
    window.dispatchEvent(new CustomEvent("nav-position-changed", { detail: value }));
  };

  const changeLanguage = async (language: string) => {
    await setLanguagePreference(language);
  };

  return (
    <Box pt={6} pb={10} px={{ base: 2, md: 4 }}>
      <Heading size="lg" mb={1} color={adaptive.text} textShadow={adaptive.shadow}>
        设置
      </Heading>
      <Text fontSize="sm" color={adaptive.text} opacity={0.78} mb={4} textShadow={adaptive.shadow}>
        游戏盒外观、皮肤与宿主入口
      </Text>

      <SimpleGrid columns={{ base: 1, xl: 2 }} spacing={4}>
        <LiquidGlassCard p={5}>
          <VStack align="stretch" spacing={2} divider={<Divider borderColor={border} />}>
            <HStack justify="space-between">
              <HStack spacing={2}>
                <Palette size={18} />
                <Heading size="sm">NexBox 皮肤</Heading>
              </HStack>
              <Badge colorScheme={skinEnabled ? "green" : "gray"} borderRadius="full">
                {skinEnabled ? "已启用" : "已关闭"}
              </Badge>
            </HStack>
            <Text fontSize="sm" color={sub}>
              安装插件后默认开启，主界面为 NexBox 风格。可随时在这里或皮肤设置页关闭。
            </Text>
            <Row title="启用 NexBox 皮肤" description="切换主窗口的外壳界面">
              <Switch
                colorScheme="teal"
                isChecked={skinEnabled}
                isDisabled={skinBusy}
                onChange={(event) => void toggleSkin(event.target.checked)}
              />
            </Row>
            <Button
              size="sm"
              variant="outline"
              colorScheme="red"
              isDisabled={!skinEnabled || skinBusy}
              onClick={() => void toggleSkin(false)}
            >
              关闭皮肤并返回经典界面
            </Button>
          </VStack>
        </LiquidGlassCard>

        <LiquidGlassCard p={5}>
          <VStack align="stretch" spacing={2} divider={<Divider borderColor={border} />}>
            <HStack spacing={2}>
              {resolveScheme(themeMode) === "dark" ? <Moon size={18} /> : <Sun size={18} />}
              <Heading size="sm">主题</Heading>
            </HStack>
            <Row title="主题模式">
              <HStack spacing={1}>
                {(
                  [
                    ["system", "跟随系统", Laptop],
                    ["light", "浅色", Sun],
                    ["dark", "深色", Moon],
                  ] as const
                ).map(([value, label, Icon]) => (
                  <Button
                    key={value}
                    size="xs"
                    variant={themeMode === value ? "solid" : "ghost"}
                    colorScheme={themeMode === value ? "teal" : undefined}
                    leftIcon={<Icon size={12} />}
                    onClick={() => setThemeMode(value)}
                  >
                    {label}
                  </Button>
                ))}
              </HStack>
            </Row>
            <Row title="主题色">
              <HStack spacing={1.5}>
                {ACCENTS.map((color) => (
                  <Box
                    key={color}
                    as="button"
                    w="22px"
                    h="22px"
                    borderRadius="full"
                    bg={color}
                    border="2px solid"
                    borderColor={config.primaryColor.toLowerCase() === color.toLowerCase() ? "white" : "transparent"}
                    onClick={() => setPrimaryColor(color)}
                  />
                ))}
                <Button size="xs" variant="ghost" onClick={resetToDefault}>
                  重置
                </Button>
              </HStack>
            </Row>
          </VStack>
        </LiquidGlassCard>

        <LiquidGlassCard p={5}>
          <VStack align="stretch" spacing={2} divider={<Divider borderColor={border} />}>
            <HStack spacing={2}>
              <Settings size={18} />
              <Heading size="sm">侧栏与动效</Heading>
            </HStack>
            <Row title="显示侧栏文字" description="关闭后只显示图标">
              <HStack spacing={2}>
                {showLabel ? <Eye size={14} /> : <EyeOff size={14} />}
                <Switch colorScheme="teal" isChecked={showLabel} onChange={(event) => changeShowLabel(event.target.checked)} />
              </HStack>
            </Row>
            <Row title="导航位置">
              <HStack spacing={1}>
                <Button
                  size="xs"
                  variant={navPosition === "left" ? "solid" : "ghost"}
                  colorScheme={navPosition === "left" ? "teal" : undefined}
                  onClick={() => changeNavPosition("left")}
                >
                  左侧
                </Button>
                <Button
                  size="xs"
                  variant={navPosition === "top" ? "solid" : "ghost"}
                  colorScheme={navPosition === "top" ? "teal" : undefined}
                  onClick={() => changeNavPosition("top")}
                >
                  顶部
                </Button>
              </HStack>
            </Row>
            <Row title="液态玻璃" description="半透明毛玻璃卡片效果">
              <Switch
                colorScheme="teal"
                isChecked={liquidGlassEnabled}
                onChange={(event) => setLiquidGlassEnabled(event.target.checked)}
              />
            </Row>
            <Row title={`玻璃模糊 ${liquidGlassBlur}px`}>
              <Slider
                min={0}
                max={30}
                step={1}
                value={liquidGlassBlur}
                onChange={(value) => setLiquidGlassBlur(value)}
                w="140px"
              >
                <SliderTrack>
                  <SliderFilledTrack bg="teal.300" />
                </SliderTrack>
                <SliderThumb />
              </Slider>
            </Row>
            <Row title="果冻弹跳" description="页面切换与侧栏动效">
              <Switch
                colorScheme="teal"
                isChecked={jellyBounceEnabled}
                onChange={(event) => setJellyBounceEnabled(event.target.checked)}
              />
            </Row>
          </VStack>
        </LiquidGlassCard>

        <LiquidGlassCard p={5}>
          <VStack align="stretch" spacing={2} divider={<Divider borderColor={border} />}>
            <HStack spacing={2}>
              <Cpu size={18} />
              <Heading size="sm">语言与宿主</Heading>
            </HStack>
            <Row title="界面语言">
              <HStack spacing={1}>
                <Button
                  size="xs"
                  variant={i18n.language?.startsWith("zh") ? "solid" : "ghost"}
                  colorScheme={i18n.language?.startsWith("zh") ? "teal" : undefined}
                  onClick={() => void changeLanguage("zh")}
                >
                  中文
                </Button>
                <Button
                  size="xs"
                  variant={i18n.language?.startsWith("en") ? "solid" : "ghost"}
                  colorScheme={i18n.language?.startsWith("en") ? "teal" : undefined}
                  onClick={() => void changeLanguage("en")}
                >
                  English
                </Button>
              </HStack>
            </Row>
            <Row title="宿主设置" description="打开图吧工具箱的设置页">
              <Button size="xs" variant="outline" onClick={() => void gamebox.navigate("settings")}>
                打开
              </Button>
            </Row>
            <Row title="硬件信息" description="打开宿主完整硬件信息页">
              <Button size="xs" variant="outline" onClick={() => void gamebox.navigate("hardware")}>
                打开
              </Button>
            </Row>
            <Row title="内置工具" description="打开宿主内置工具索引">
              <Button size="xs" variant="outline" onClick={() => void gamebox.navigate("builtin-tools")}>
                打开
              </Button>
            </Row>
            <Row title="关于" description="版本、上游与第三方许可">
              <Button as={Link} to="/about" size="xs" variant="ghost" leftIcon={<Info size={12} />}>
                查看
              </Button>
            </Row>
          </VStack>
        </LiquidGlassCard>
      </SimpleGrid>
    </Box>
  );
}

import {
  Badge,
  Box,
  Button,
  HStack,
  Heading,
  SimpleGrid,
  Slider,
  SliderFilledTrack,
  SliderThumb,
  SliderTrack,
  Spinner,
  Switch,
  Text,
  VStack,
  Wrap,
  WrapItem,
  useColorModeValue,
  useToast,
} from "@chakra-ui/react";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Monitor, RotateCcw, Settings2 } from "lucide-react";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { useThemeColor } from "@/contexts/theme-color-context";
import {
  gamebox,
  type DisplayFilterPreset,
  type DisplayFilterResult,
  type DisplayFilterSettings,
  type DisplayFilterValues,
  type DisplayInfo,
} from "@/lib/gamebox";

function FilterSlider({
  label,
  value,
  min,
  max,
  step,
  unit,
  accent,
  onChange,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step: number;
  unit?: string;
  accent: string;
  onChange: (value: number) => void;
}) {
  const [draft, setDraft] = useState(value);

  useEffect(() => {
    setDraft(value);
  }, [value]);

  const display = step < 1 ? draft.toFixed(2) : Math.round(draft).toString();

  return (
    <Box>
      <HStack justify="space-between" mb={1}>
        <Text fontSize="sm" fontWeight="medium">
          {label}
        </Text>
        <Text fontSize="sm" color="gray.500">
          {display}
          {unit ?? ""}
        </Text>
      </HStack>
      <Slider
        value={draft}
        min={min}
        max={max}
        step={step}
        focusThumbOnChange={false}
        onChange={setDraft}
        onChangeEnd={(next) => {
          setDraft(next);
          onChange(next);
        }}
      >
        <SliderTrack>
          <SliderFilledTrack bg={accent} />
        </SliderTrack>
        <SliderThumb />
      </Slider>
    </Box>
  );
}

export default function DisplayFilterPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const { getActiveColor } = useThemeColor();
  const accent = getActiveColor();
  const adaptiveTitle = useAdaptiveTextColor();
  const sub = useColorModeValue("gray.600", "#a0a0a0");
  const borderColor = useColorModeValue("gray.200", "#333333");

  const [displays, setDisplays] = useState<DisplayInfo[]>([]);
  const [presets, setPresets] = useState<DisplayFilterPreset[]>([]);
  const [activeIndex, setActiveIndex] = useState(0);
  const [settings, setSettings] = useState<DisplayFilterSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const [nextDisplays, nextPresets] = await Promise.all([
          gamebox.displays(),
          gamebox.filterPresets(),
        ]);
        if (!alive) return;
        setDisplays(nextDisplays);
        setPresets(nextPresets);
        const primary = nextDisplays.findIndex((display) => display.primary);
        const index = primary >= 0 ? primary : 0;
        setActiveIndex(index);
        if (nextDisplays.length > 0) {
          setSettings(await gamebox.filterGet(index));
        }
      } catch (cause) {
        if (alive) setError(cause instanceof Error ? cause.message : String(cause));
      } finally {
        if (alive) setLoading(false);
      }
    })();
    return () => {
      alive = false;
    };
  }, []);

  const showResult = (result: DisplayFilterResult): boolean => {
    if (result.ok) {
      setError("");
      return true;
    }

    const message = result.error || t("displayFilter.error");
    setError(message);
    toast({
      title: t("displayFilter.error"),
      description: message,
      status: "error",
      duration: 4000,
      isClosable: true,
    });
    return false;
  };

  const selectDisplay = async (index: number) => {
    setActiveIndex(index);
    setError("");
    try {
      setSettings(await gamebox.filterGet(index));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  };

  const commit = async (patch: Partial<DisplayFilterValues>) => {
    if (!settings) return;
    const next = { ...settings, ...patch };
    setSettings(next);
    setBusy(true);
    try {
      showResult(await gamebox.filterSet(activeIndex, next));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  };

  const applyPreset = async (preset: DisplayFilterPreset) => {
    setBusy(true);
    try {
      const result = await gamebox.filterApplyPreset(activeIndex, preset.id);
      if (showResult(result) && result.settings) {
        setSettings(result.settings);
      }
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  };

  const resetAll = async () => {
    setBusy(true);
    try {
      const result = await gamebox.filterReset(activeIndex);
      if (!showResult(result)) return;
      if (result.settings) {
        setSettings(result.settings);
      }
      toast({
        title: t("displayFilter.resetSuccess"),
        status: "success",
        duration: 2000,
        isClosable: true,
      });
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  };

  const openColorManagement = async () => {
    try {
      showResult(await gamebox.openColorManagement());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  };

  if (loading) {
    return (
      <HStack justify="center" py={20}>
        <Spinner color={accent} />
      </HStack>
    );
  }

  if (displays.length === 0 || !settings) {
    return (
      <Box pt={8}>
        <Heading size="lg" color={adaptiveTitle.text}>
          {t("displayFilter.title")}
        </Heading>
        <Text color={sub} mt={2}>
          {error || "没有检测到可用的显示器。"}
        </Text>
      </Box>
    );
  }

  return (
    <Box pt={8} pb={8}>
      <Heading size="lg" color={adaptiveTitle.text} textShadow={adaptiveTitle.shadow} mb={1}>
        {t("displayFilter.title")}
      </Heading>
      <Text color={sub} fontSize="sm" mb={4}>
        {t("displayFilter.tip")}
      </Text>

      {displays.length > 1 && (
        <Wrap mb={4} spacing={2}>
          {displays.map((display) => (
            <WrapItem key={display.index}>
              <Button
                size="sm"
                variant={activeIndex === display.index ? "solid" : "outline"}
                borderColor={borderColor}
                leftIcon={<Monitor size={15} />}
                onClick={() => void selectDisplay(display.index)}
              >
                {display.name}
                {display.primary && (
                  <Badge ml={2} colorScheme="teal" fontSize="2xs">
                    {t("displayFilter.primary")}
                  </Badge>
                )}
              </Button>
            </WrapItem>
          ))}
        </Wrap>
      )}

      <LiquidGlassCard p={5} mb={4}>
        <HStack justify="space-between" align="center">
          <VStack align="start" spacing={1}>
            <Text fontWeight="semibold">{t("displayFilter.enable")}</Text>
            <Text color={sub} fontSize="sm">
              {settings.device} · {t("displayFilter.selectDisplay")}
            </Text>
          </VStack>
          <Switch
            size="lg"
            colorScheme="teal"
            isChecked={settings.enabled}
            isDisabled={busy}
            onChange={(event) => void commit({ enabled: event.target.checked })}
          />
        </HStack>
        {error && (
          <Text color="red.400" fontSize="sm" mt={3}>
            {error}
          </Text>
        )}
      </LiquidGlassCard>

      <SimpleGrid columns={{ base: 1, lg: 2 }} spacing={4}>
        <LiquidGlassCard p={5}>
          <Text fontWeight="semibold" mb={1}>
            {t("displayFilter.presets")}
          </Text>
          <Text color={sub} fontSize="sm" mb={3}>
            {t("displayFilter.groupNexbox")}
          </Text>
          <SimpleGrid columns={{ base: 2, md: 3 }} spacing={2}>
            {presets.map((preset) => {
              const active = settings.enabled && settings.mode === preset.mode;
              return (
                <Button
                  key={preset.id}
                  size="sm"
                  variant={active ? "solid" : "outline"}
                  borderColor={borderColor}
                  isDisabled={busy}
                  onClick={() => void applyPreset(preset)}
                >
                  {preset.name}
                </Button>
              );
            })}
          </SimpleGrid>
        </LiquidGlassCard>

        <LiquidGlassCard p={5}>
          <Text fontWeight="semibold" mb={3}>
            {t("displayFilter.currentSettings")}
          </Text>
          <VStack align="stretch" spacing={3}>
            <FilterSlider
              label={t("displayFilter.colorTemperature")}
              value={settings.temperature}
              min={1000}
              max={10000}
              step={100}
              unit="K"
              accent={accent}
              onChange={(value) => void commit({ temperature: value })}
            />
            <FilterSlider
              label={t("displayFilter.brightness")}
              value={settings.brightness}
              min={50}
              max={150}
              step={1}
              unit="%"
              accent={accent}
              onChange={(value) => void commit({ brightness: value, mode: 0 })}
            />
            <FilterSlider
              label={t("displayFilter.contrast")}
              value={settings.contrast}
              min={50}
              max={150}
              step={1}
              unit="%"
              accent={accent}
              onChange={(value) => void commit({ contrast: value, mode: 0 })}
            />
            <FilterSlider
              label={t("displayFilter.saturation")}
              value={settings.saturation}
              min={50}
              max={150}
              step={1}
              unit="%"
              accent={accent}
              onChange={(value) => void commit({ saturation: value, mode: 0 })}
            />
            <FilterSlider
              label={t("displayFilter.shadowBoost")}
              value={settings.shadow}
              min={-100}
              max={100}
              step={1}
              accent={accent}
              onChange={(value) => void commit({ shadow: value })}
            />
            <Text fontSize="sm" fontWeight="medium" mt={1}>
              {t("displayFilter.rgbGamma")}
            </Text>
            <FilterSlider
              label={t("displayFilter.rGamma")}
              value={settings.rGamma}
              min={0.5}
              max={2.5}
              step={0.01}
              accent="#E05A5A"
              onChange={(value) => void commit({ rGamma: value, mode: 0 })}
            />
            <FilterSlider
              label={t("displayFilter.gGamma")}
              value={settings.gGamma}
              min={0.5}
              max={2.5}
              step={0.01}
              accent="#5ABF7A"
              onChange={(value) => void commit({ gGamma: value, mode: 0 })}
            />
            <FilterSlider
              label={t("displayFilter.bGamma")}
              value={settings.bGamma}
              min={0.5}
              max={2.5}
              step={0.01}
              accent="#5A8FE0"
              onChange={(value) => void commit({ bGamma: value, mode: 0 })}
            />
            <HStack spacing={2} pt={2}>
              <Button
                size="sm"
                variant="outline"
                borderColor={borderColor}
                leftIcon={<RotateCcw size={14} />}
                isDisabled={busy}
                onClick={() => void resetAll()}
              >
                {t("displayFilter.resetDefault")}
              </Button>
              <Button
                size="sm"
                variant="outline"
                borderColor={borderColor}
                leftIcon={<Settings2 size={14} />}
                onClick={() => void openColorManagement()}
              >
                Windows 颜色管理
              </Button>
            </HStack>
          </VStack>
        </LiquidGlassCard>
      </SimpleGrid>
    </Box>
  );
}

import {
  Box,
  Text,
  Heading,
  VStack,
  SimpleGrid,
  useColorModeValue,
  Button,
} from "@chakra-ui/react";
import { useTranslation } from "react-i18next";
import { ExternalLink } from "lucide-react";
import { PageBackButton } from "@/components/special/page-back-button";
import { PageHeader } from "@/components/special/page-header";
import { invoke } from "@tauri-apps/api/core";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useBackground } from "@/contexts/background-context";
import { useThemeColor } from "@/contexts/theme-color-context";

interface PlatformInfo {
  id: string;
  name: string;
  url: string;
  description: string;
}

export default function OtherGunCodePlatformsPage() {
  const { t } = useTranslation();
  const headingColor = useColorModeValue("gray.900", "#ffffff");
  const textColor = useColorModeValue("#000000", "#ffffff");
  const subTextColor = useColorModeValue("#000000", "#ffffff");
  const cardBg = useColorModeValue("white", "#111111");
  const borderColor = useColorModeValue("gray.200", "#333333");
  const { liquidGlassEnabled } = useBackground();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();

  const platforms: PlatformInfo[] = [
    {
      id: "maqt",
      name: t("deltaForce.otherPlatforms.maqt.name", "码枪堂"),
      url: "https://maqt.top/home",
      description: t("deltaForce.otherPlatforms.maqt.description", "码枪堂改枪码平台"),
    },
    {
      id: "anxu",
      name: t("deltaForce.otherPlatforms.anxu.name", "ANXU改枪码"),
      url: "https://guns.anxu.cc/",
      description: t("deltaForce.otherPlatforms.anxu.description", "ANXU改枪码平台"),
    },
    {
      id: "aitags",
      name: t("deltaForce.otherPlatforms.aitags.name", "主播改枪码"),
      url: "https://g.aitags.cn/live",
      description: t("deltaForce.otherPlatforms.aitags.description", "主播改枪码平台"),
    },
    {
      id: "xiaotao",
      name: t("deltaForce.otherPlatforms.xiaotao.name", "小涛查"),
      url: "https://orzice.com/v/gun_gqm",
      description: t("deltaForce.otherPlatforms.xiaotao.description", "小涛查改枪码平台"),
    },
  ];

  const handleOpenLink = async (platform: PlatformInfo) => {
    try {
      await invoke("open_platform_window", {
        url: platform.url,
        title: platform.name,
        label: `${platform.id}-${Date.now()}`,
      });
    } catch (error) {
      console.error("打开平台窗口失败:", error);
    }
  };

  return (
    <Box pt={8} pb={8}>
      <PageHeader title={t("deltaForce.otherPlatforms.title", "其他改枪码平台")} mb={6} />

        <Text color={subTextColor} fontSize="sm" mb={6}>
        {t("deltaForce.otherPlatforms.description", "以下是一些其他改枪码平台，点击可在浏览器中打开")}
      </Text>

      <SimpleGrid columns={{ base: 1, md: 2 }} spacing={4}>
        {platforms.map((platform) => {
          const cardContent = (
            <VStack align="stretch" spacing={3}>
              <Heading size="sm" color={textColor}>
                {platform.name}
              </Heading>
              <Text fontSize="sm" color={subTextColor}>
                {platform.description}
              </Text>
              <Text fontSize="xs" color={primaryColor} wordBreak="break-all">
                {platform.url}
              </Text>
              <Button
                size="sm"
                variant="outline"
                color={primaryColor}
                borderColor={primaryColor}
                _hover={{ bg: `${primaryColor}15` }}
                leftIcon={<ExternalLink size={14} />}
                onClick={() => handleOpenLink(platform)}
                alignSelf="flex-start"
              >
                {t("deltaForce.openPlatform", "打开平台")}
              </Button>
            </VStack>
          );

          if (liquidGlassEnabled) {
            return (
              <LiquidGlassCard key={platform.id} p={5}>
                {cardContent}
              </LiquidGlassCard>
            );
          }

          return (
            <Box
              key={platform.id}
              bg={cardBg}
              borderRadius="3xl"
              p={5}
              border="1px solid"
              borderColor={borderColor}
            >
              {cardContent}
            </Box>
          );
        })}
      </SimpleGrid>
    </Box>
  );
}

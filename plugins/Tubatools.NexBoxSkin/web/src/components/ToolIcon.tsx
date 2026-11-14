import { useColorModeValue } from "@chakra-ui/react";
import { useEffect, useState } from "react";
import {
  Box as BoxIcon,
  Cpu,
  Gamepad2,
  Network,
  Settings,
  Wrench,
} from "lucide-react";
import { gamebox, type ToolInfo } from "@/lib/gamebox";

export function categoryIcon(category: string) {
  if (category.includes("硬件")) return Cpu;
  if (category.includes("网络")) return Network;
  if (category.includes("游戏")) return Gamepad2;
  if (category.includes("系统")) return Settings;
  if (category.includes("实用")) return Wrench;
  return BoxIcon;
}

export function ToolIcon({ tool, size = 34 }: { tool: ToolInfo; size?: number }) {
  const [source, setSource] = useState<string | undefined>();
  const accent = useColorModeValue("gray.600", "#98DDD0");

  useEffect(() => {
    let alive = true;
    gamebox
      .icon(tool.id)
      .then((uri) => {
        if (alive && uri) setSource(uri);
      })
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, [tool.id]);

  if (source) {
    return (
      <img
        src={source}
        width={size}
        height={size}
        style={{ objectFit: "contain", display: "block" }}
        alt=""
      />
    );
  }

  const Icon = categoryIcon(tool.category);
  return <Icon size={size} color={accent} />;
}

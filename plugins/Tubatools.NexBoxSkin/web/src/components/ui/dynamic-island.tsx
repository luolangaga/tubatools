import { useToast } from "@chakra-ui/react";

export interface DynamicIslandToastOptions {
  title?: string;
  description?: string;
  status?: "info" | "warning" | "success" | "error";
  duration?: number;
  isClosable?: boolean;
}

/**
 * Minimal drop-in replacement for NexBox's Dynamic Island toast hook.
 * We only need the `toast({ title, description, status, duration })` shape.
 */
export function useDynamicIsland(_target?: string) {
  const toast = useToast();
  return (options: DynamicIslandToastOptions) =>
    toast({
      position: "top",
      title: options.title,
      description: options.description,
      status: options.status ?? "info",
      duration: options.duration ?? 2500,
      isClosable: options.isClosable ?? true,
    });
}

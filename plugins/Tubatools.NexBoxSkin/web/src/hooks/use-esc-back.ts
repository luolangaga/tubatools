import { useEffect } from "react";
import { useLocation, useNavigate } from "react-router-dom";

/** 是否存在打开的浮层（弹窗/菜单/下拉）——浮层自己响应 Esc 关闭，全局返回让路 */
export function isOverlayOpen(): boolean {
  return !!document.querySelector(
    '[role="dialog"], [role="alertdialog"], [role="menu"], [role="listbox"]'
  );
}

/**
 * 各详情页的入口页映射：Esc 回到打开该页面的入口页（如内置工具/优化/三角洲），
 * 不按浏览历史一路回退。入口页本身（侧边栏一级页面）不在映射内，按 Esc 无动作。
 * 与 BuiltinToolsPage.defaultTools / OptimizePage 子页列表保持同步。
 */
const PAGE_PARENT: Record<string, string> = {
  // 内置工具页打开的工具
  "/display-filter": "/builtin-tools",
  "/crosshair": "/builtin-tools",
  "/overlay-panel": "/builtin-tools",
  "/gpu-rename": "/builtin-tools",
  "/cpu-rename": "/builtin-tools",
  "/mouse-polling": "/builtin-tools",
  "/resolution-converter": "/builtin-tools",
  "/dlss-preset": "/builtin-tools",
  "/disk-health": "/builtin-tools",
  "/nvidia-driver": "/builtin-tools",
  "/audio-eq": "/builtin-tools",
  "/autoclicker": "/builtin-tools",
  "/speedtest": "/builtin-tools",
  "/runtime-repair": "/builtin-tools",
  "/vtx-virtualization": "/builtin-tools",
  "/hidden-features": "/builtin-tools",
  "/context-menu": "/builtin-tools",
  "/screen-test": "/builtin-tools",
  "/download-accelerator": "/builtin-tools",
  "/nvidia-recording": "/builtin-tools",
  "/bsod-log": "/builtin-tools",
  "/vac-repair": "/builtin-tools",
  "/app-manager": "/builtin-tools",
  "/time-sync": "/builtin-tools",
  // NVIDIA 驱动页内打开的下载页（层级上属于驱动页子页）
  "/nvidia-driver-download": "/nvidia-driver",
  // 优化页子页
  "/optimize/storage-clean": "/optimization",
  "/optimize/memory-cleanup": "/optimization",
  "/optimize/ace-optimize": "/optimization",
  "/optimize/memory-limit": "/optimization",
  "/optimize/shader-cache": "/optimization",
  "/optimize/pso-cache": "/optimization",
  "/optimize/power-management": "/optimization",
  "/optimize/startup-manager": "/optimization",
  "/optimize/network-optimizer": "/optimization",
  "/optimize/peripheral-optimize": "/optimization",
  "/optimize/windows-update": "/optimization",
  "/optimize/cpu-scheduler": "/optimization",
  "/optimize/game-process-optimize": "/optimization",
  // 三角洲行动子页
  "/delta-force/other-platforms": "/delta-force",
  "/delta-force/random-equipment": "/delta-force",
  "/delta-force/streamers": "/delta-force",
};

/**
 * 动态路由（带参数）的返回映射：按前缀匹配，取最长命中。
 * 例：/delta-force/streamers/<id> → 回到主播列表，而不是直接回三角洲首页。
 */
const PAGE_PARENT_PREFIX: Array<[string, string]> = [
  ["/delta-force/streamers/", "/delta-force/streamers"],
];

export function resolveParent(pathname: string): string | undefined {
  let best: [string, string] | undefined;
  for (const rule of PAGE_PARENT_PREFIX) {
    if (pathname.startsWith(rule[0]) && (!best || rule[0].length > best[0].length)) {
      best = rule;
    }
  }
  return best ? best[1] : PAGE_PARENT[pathname];
}

/**
 * 全局 Esc 返回入口页（仅主窗口，挂载于 MainLayout）。
 * 以下情况不处理，交给具体组件：
 * - 事件已被其他处理器消费（preventDefault / stopPropagation）
 * - 焦点在输入框/可编辑元素（页内 Esc 取消编辑、热键录制取消等）
 * - 存在打开的浮层（Chakra Modal / 菜单 / 下拉）
 * - 当前是入口页/一级页面（无上级可返回）
 */
export function useEscBack() {
  const navigate = useNavigate();
  const location = useLocation();

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key !== "Escape" || e.ctrlKey || e.altKey || e.metaKey || e.shiftKey) return;
      if (e.defaultPrevented) return;

      const target = e.target as HTMLElement | null;
      if (
        target &&
        (target.tagName === "INPUT" ||
          target.tagName === "TEXTAREA" ||
          target.tagName === "SELECT" ||
          target.isContentEditable)
      ) {
        return;
      }

      if (isOverlayOpen()) return;

      const parent = resolveParent(location.pathname);
      if (!parent) return;
      navigate(parent);
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [navigate, location.pathname]);
}

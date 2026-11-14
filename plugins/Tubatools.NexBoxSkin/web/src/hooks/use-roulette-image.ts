import { invoke, convertFileSrc } from "@tauri-apps/api/core";
import { useState, useEffect } from "react";

// 相同 URL 只并发一次缓存请求，避免同一页多个组件重复下载
const inflight = new Map<string, Promise<string>>();

async function cacheImage(url: string): Promise<string> {
  let p = inflight.get(url);
  if (!p) {
    p = invoke<string>("cache_delta_image", { url })
      .catch((e) => {
        console.error("[RouletteImage] cache_delta_image failed:", e);
        // 失败时返回空串，由 hook 回退直连远程
        return "";
      })
      .finally(() => inflight.delete(url));
    inflight.set(url, p);
  }
  return p;
}

/**
 * 随机装备图片的本地缓存 Hook：
 *   loading=true 表示图片还在下载中等候；
 *   成功 → 返回 convertFileSrc 的本地缓存地址；
 *   失败/空 → 回退用原始远程 URL 直接显示（WebView 能直连腾讯图片域名）。
 */
export function useRouletteImage(url?: string): { src?: string; loading: boolean } {
  const [state, setState] = useState<{ src?: string; loading: boolean }>({
    src: undefined,
    loading: false,
  });

  useEffect(() => {
    if (!url) {
      setState({ src: undefined, loading: false });
      return;
    }
    setState((s) => ({ ...s, src: undefined, loading: true }));
    let alive = true;
    (async () => {
      const path = await cacheImage(url);
      if (!alive) return;
      if (path) {
        setState({ src: convertFileSrc(path), loading: false });
      } else {
        // 缓存失败：回退直连远程
        setState({ src: url, loading: false });
      }
    })();
    return () => {
      alive = false;
    };
  }, [url]);

  return state;
}

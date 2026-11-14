import { LazyStore } from "@tauri-apps/plugin-store";

/** 所有组件共享的 settings.json 存储实例 */
export const store = new LazyStore("settings.json");

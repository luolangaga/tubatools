import { invoke } from "@tauri-apps/api/core";

export async function open(url: string): Promise<void> {
  await invoke("host.openUri", { url });
}

export async function openPath(path: string): Promise<void> {
  await open(path);
}

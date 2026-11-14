import { invoke } from "@tauri-apps/api/core";

export async function writeText(text: string): Promise<void> {
  await invoke("host.copyText", { text });
}

export async function readText(): Promise<string> {
  return "";
}

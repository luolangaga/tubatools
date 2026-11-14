import { invoke } from "@tauri-apps/api/core";

export async function open(options?: { title?: string; directory?: boolean }): Promise<string | null> {
  return invoke<string | null>(options?.directory ? "host.pickFolder" : "host.pickFile", {
    title: options?.title ?? "选择",
  });
}

export async function save(): Promise<string | null> {
  return null;
}

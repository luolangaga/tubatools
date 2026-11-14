import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "node:path";

export default defineConfig({
  base: "./",
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "src"),
      "@tauri-apps/api/core": path.resolve(__dirname, "src/shims/tauri-core.ts"),
      "@tauri-apps/api/event": path.resolve(__dirname, "src/shims/tauri-event.ts"),
      "@tauri-apps/api/window": path.resolve(__dirname, "src/shims/tauri-window.ts"),
      "@tauri-apps/plugin-store": path.resolve(__dirname, "src/shims/tauri-store.ts"),
      "@tauri-apps/plugin-shell": path.resolve(__dirname, "src/shims/tauri-shell.ts"),
      "@tauri-apps/plugin-clipboard-manager": path.resolve(__dirname, "src/shims/tauri-clipboard.ts"),
      "@tauri-apps/plugin-dialog": path.resolve(__dirname, "src/shims/tauri-dialog.ts"),
    },
  },
  build: {
    outDir: "../assets/web",
    emptyOutDir: true,
    target: "es2022",
  },
});

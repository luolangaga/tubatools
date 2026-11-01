import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-vue';
import fs from 'node:fs';
import path from 'node:path';

let outDir = 'dist';

// https://vitejs.dev/config/
export default defineConfig({
    base: '/',
    plugins: [plugin(), {
        name: 'generate-404-fallback',
        configResolved(config) {
            outDir = config.build.outDir;
        },
        closeBundle() {
            // Cloudflare Pages 对未命中静态文件的路径（如 /guide/x 深层链接）会返回 404.html，
            // 因此把 index.html 复制为 404.html 作为 SPA 回退，前端路由照常渲染
            fs.copyFileSync(path.join(outDir, 'index.html'), path.join(outDir, '404.html'));
            // Give the public sponsor wall a real static entry (HTTP 200 on a direct visit).
            fs.copyFileSync(path.join(outDir, 'index.html'), path.join(outDir, 'sponsors.html'));
        }
    }],
    server: {
        port: 63179,
    }
})

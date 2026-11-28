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
        generateBundle(_options, bundle) {
            // Cloudflare 对未命中静态文件的路径（如 /guide/x 深层链接）会回退到 404.html，
            // 这里直接以最终 index.html 内容生成 SPA 回退页，避免依赖磁盘写入时序
            const index = bundle['index.html'];
            if (!index || !('source' in index)) return;
            const source = index.source;
            this.emitFile({ type: 'asset', fileName: '404.html', source });
            this.emitFile({ type: 'asset', fileName: 'sponsors.html', source });
        },
        closeBundle() {
            // 兜底：部分构建链路不会把 HTML 放进 bundle，此时等文件落盘后再复制
            try {
                const indexPath = path.join(outDir, 'index.html');
                if (!fs.existsSync(indexPath)) return;
                for (const name of ['404.html', 'sponsors.html']) {
                    const target = path.join(outDir, name);
                    if (!fs.existsSync(target)) fs.copyFileSync(indexPath, target);
                }
            } catch {
                /* 构建产物缺失时不阻断构建 */
            }
        }
    }],
    server: {
        port: 63179,
    }
})

import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const prefix=path.resolve(process.argv[2]||'.');
const require=createRequire(path.join(prefix,'package.json'));
const directory=path.dirname(fileURLToPath(import.meta.url));
require('esbuild').buildSync({entryPoints:[path.join(directory,'scene.js')],outfile:path.join(directory,'scene.bundle.js'),bundle:true,minify:true,format:'iife',nodePaths:[path.join(prefix,'node_modules')]});

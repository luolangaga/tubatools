#!/usr/bin/env node
import { spawn } from 'node:child_process';
import { readFile, mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';

// File transport works with the toolbox's Windows GUI executable, where stdout is unreliable.
export async function invoke(args, executable) {
  if (!executable || !isAbsolute(executable)) throw new Error('工具箱路径不可用。请在工具箱设置 → Agent Skills 重新安装技能，或设置 TUBA_TOOLBOX_EXE 为 exe 的绝对路径。');
  if (!args.length) args = ['help'];
  if (args.some(a => a.toLowerCase() === '--output')) throw new Error('--output 由桥接脚本管理，无需传入。');
  const directory = await mkdtemp(join(tmpdir(), 'tuba-agent-'));
  const output = join(directory, 'result.json');
  try {
    const result = await new Promise((resolve, reject) => {
      const child = spawn(executable, ['--agent-cli', args[0], '--output', output, ...args.slice(1)], {
        shell: false, windowsHide: true, stdio: 'ignore', timeout: 360_000
      });
      child.once('error', reject);
      child.once('exit', code => resolve(code));
    });
    let response;
    try { response = JSON.parse(await readFile(output, 'utf8')); }
    catch { throw new Error(`工具箱未返回有效结果（退出码 ${result}）。请检查安装路径、Windows 运行库及当前用户的访问权限。`); }
    if (response.schemaVersion !== 1 || typeof response.ok !== 'boolean') throw new Error('不支持的工具箱响应格式。');
    return response;
  } finally { await rm(directory, { recursive: true, force: true }); }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  try {
    let executable = process.env.TUBA_TOOLBOX_EXE;
    if (!executable) {
      try { executable = JSON.parse(await readFile(new URL('../toolbox.json', import.meta.url), 'utf8')).executable; }
      catch { /* Report the actionable configuration error from invoke(). */ }
    }
    const response = await invoke(process.argv.slice(2), executable);
    process.stdout.write(JSON.stringify(response, null, 2) + '\n');
    process.exitCode = response.ok ? 0 : 1;
  } catch (error) {
    process.stdout.write(JSON.stringify({ schemaVersion: 1, ok: false, error: { code: 'BridgeError', message: error.message } }) + '\n');
    process.exitCode = 1;
  }
}

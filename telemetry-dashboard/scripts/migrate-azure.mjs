import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { gzipSync, gunzipSync } from 'node:zlib';
import { EDGE, DATASET, queryAxiom } from '../src/axiom.js';

const folder = new URL('../../artifacts/telemetry-migration/', import.meta.url);
await mkdir(folder, { recursive: true });
const migrationToken = (await readFile(new URL('migration-token.txt', folder), 'utf8')).trim();
const ingestToken = (await readFile(new URL('ingest-token.txt', folder), 'utf8')).trim();
const queryToken = (await readFile(new URL('query-token.txt', folder), 'utf8')).trim();
const manifestUrl = new URL('migration-manifest.json', folder);
let manifest;
try { manifest = JSON.parse(await readFile(manifestUrl, 'utf8')); }
catch { manifest = { startedAt: new Date().toISOString(), tables: {}, imported: {} }; }
let saveQueue = Promise.resolve();
const save = () => {
  const text = JSON.stringify(manifest, null, 2);
  saveQueue = saveQueue.then(() => writeFile(manifestUrl, text));
  return saveQueue;
};

async function fetchRetry(url, options) {
  for (let attempt = 0; ; attempt++) {
    try {
      const response = await fetch(url, { ...options, signal: AbortSignal.timeout(40000) });
      if ((response.status === 429 || response.status >= 500) && attempt < 3) {
        await response.body?.cancel();
        continue;
      }
      return response;
    } catch (error) {
      if (attempt >= 3) throw error;
      console.log(`Temporary connection failure; retry ${attempt + 1}/3`);
    }
  }
}

async function azure(body) {
  const response = await fetchRetry('https://stats.tubawinui3.cn/_migration/azure', {
    method: 'POST', headers: { authorization: `Bearer ${migrationToken}`, 'content-type': 'application/json' },
    body: JSON.stringify(body), signal: AbortSignal.timeout(40000),
  });
  const payload = await response.json();
  if (!response.ok || payload.error) {
    const code = payload.error?.code || payload.error || response.status;
    throw new Error(`Azure export failed (${response.status}, ${code}): ${payload.error?.message || ''}`);
  }
  const table = payload.tables?.[0];
  if (!table) throw new Error('Azure returned no table');
  return table.rows.map(row => Object.fromEntries(table.columns.map((col, i) => [col.name, row[i]])));
}

function normalize(row, table) {
  const dimensions = typeof row.customDimensions === 'string' ? JSON.parse(row.customDimensions || '{}') : row.customDimensions || {};
  const details = typeof row.details === 'string' ? JSON.parse(row.details || '[]') : row.details || [];
  const id = row.itemId || createHash('sha256').update(JSON.stringify(row)).digest('hex');
  return {
    _time: row.timestamp, event_id: `azure:${table}:${id}`, kind: table === 'exceptions' ? 'exception' : 'event',
    name: row.name || 'exception', user_Id: row.user_Id || '', session_Id: row.session_Id || '',
    application_Version: row.application_Version || '', customDimensions: dimensions,
    version: dimensions.version || row.application_Version || '', tool: dimensions.tool || '', frames: JSON.stringify(details[0]?.parsedStack || []),
    type: row.type || '', outerMessage: row.outerMessage || '', innermostMessage: row.innermostMessage || '',
    details, origin: 'azure-migration',
  };
}

async function slice(table, start, end) {
  const key = `${table}-${start}-${end}`;
  if (manifest.imported[key]) return manifest.imported[key].count;
  const rows = await azure({ table, start: new Date(start).toISOString(), end: new Date(end).toISOString() });
  if (rows.length >= 5001) {
    if (end - start <= 1) throw new Error('More than 5000 records in one millisecond; refusing truncated export');
    const middle = Math.floor((start + end) / 2);
    return await slice(table, start, middle) + await slice(table, middle, end);
  }
  if (!rows.length) return 0;
  // 原始导出先备份，收到完整确认后才落导入检查点；中断后可恢复。
  await writeFile(new URL(`${key}.json.gz`, folder), gzipSync(JSON.stringify(rows)));
  const response = await fetchRetry(`${EDGE}/v1/ingest/${DATASET}`, {
    method: 'POST', headers: { authorization: `Bearer ${ingestToken}`, 'content-type': 'application/json', 'content-encoding': 'gzip' },
    body: gzipSync(JSON.stringify(rows.map(row => normalize(row, table)))), signal: AbortSignal.timeout(40000),
  });
  const result = await response.json();
  if (!response.ok || result.failed !== 0 || result.ingested !== rows.length)
    throw new Error(`Axiom import not fully accepted: ${response.status}, accepted=${result.ingested}, failed=${result.failed}`);
  manifest.imported[key] = { count: rows.length, importedAt: new Date().toISOString() };
  await save();
  console.log(`${table}: imported ${rows.length} (${new Date(start).toISOString()})`);
  return rows.length;
}

try {
  for (const table of ['customEvents', 'exceptions']) {
    if (!manifest.tables[table]) {
      const [inventory] = await azure({ table, mode: 'inventory' });
      manifest.tables[table] = inventory;
      await save();
    }
    const inventory = manifest.tables[table];
    console.log(`${table}: Azure records=${inventory.records}, from=${inventory.firstSeen}, to=${inventory.lastSeen}`);
    if (inventory.records) {
      const first = +new Date(inventory.firstSeen), last = +new Date(inventory.lastSeen) + 1;
      const completed = Object.entries(manifest.imported).filter(([key]) => key.startsWith(table + '-'))
        .map(([key, value]) => ({ start: Number(key.split('-')[1]), end: Number(key.split('-')[2]), count: value.count }))
        .sort((a, b) => a.start - b.start);
      let cursor = first;
      const tasks = [];
      const addRange = (start, end) => {
        while (start < end) {
          const boundary = Math.min(end, start + 86400000);
          tasks.push({ start, end: boundary }); start = boundary;
        }
      };
      for (const done of completed) {
        if (done.start > cursor) addRange(cursor, done.start);
        cursor = Math.max(cursor, done.end);
      }
      addRange(cursor, last);
      let next = 0;
      const counts = await Promise.all(Array.from({ length: 3 }, async () => {
        let workerCount = 0;
        while (next < tasks.length) {
          const task = tasks[next++];
          workerCount += await slice(table, task.start, task.end);
        }
        return workerCount;
      }));
      const count = completed.reduce((sum, row) => sum + row.count, 0) + counts.reduce((sum, value) => sum + value, 0);
      if (count !== inventory.records) throw new Error(`Export count mismatch: ${count} != ${inventory.records}`);
    }
  }
  // 核验窗口也受免费方案限制；按七天分段精确去重计数，不使用近似 dcount。
  const verification = { columns: ['kind', 'records'], rows: [] };
  manifest.verificationWindows = [];
  for (const [table, kind] of [['customEvents', 'event'], ['exceptions', 'exception']]) {
    const inventory = manifest.tables[table];
    let total = 0;
    if (inventory.records) {
      let cursor = +new Date(inventory.firstSeen);
      const end = +new Date(inventory.lastSeen) + 1;
      while (cursor < end) {
        const boundary = Math.min(end, cursor + 7 * 86400000);
        const startTime = new Date(cursor).toISOString(), endTime = new Date(boundary).toISOString();
        const check = await queryAxiom({ AXIOM_QUERY_TOKEN: queryToken },
          `['${DATASET}'] | where origin == "azure-migration" and kind == "${kind}" and _time >= datetime(${startTime}) and _time < datetime(${endTime}) | summarize by event_id | summarize records = count()`,
          { startTime, endTime });
        const records = check.rows[0]?.[0] || 0;
        manifest.verificationWindows.push({ table, startTime, endTime, records });
        total += records;
        cursor = boundary;
      }
    }
    verification.rows.push([kind, total]);
  }
  manifest.verification = verification;
  const actual = Object.fromEntries(verification.rows);
  const cutoff = Date.now() - 30 * 86400000;
  manifest.retentionCutoff = new Date(cutoff).toISOString();
  manifest.sourceAudit = {};
  const expiredBackup = [];
  for (const [table, kind] of [['customEvents', 'event'], ['exceptions', 'exception']]) {
    const ids = new Set();
    let duplicates = 0, expired = 0;
    for (const key of Object.keys(manifest.imported).filter(key => key.startsWith(table + '-'))) {
      const rows = JSON.parse(gunzipSync(await readFile(new URL(`${key}.json.gz`, folder))).toString('utf8'));
      for (const row of rows) {
        const id = normalize(row, table).event_id;
        if (ids.has(id)) { duplicates++; continue; }
        ids.add(id);
        if (+new Date(row.timestamp) < cutoff) { expired++; expiredBackup.push({ table, ...row }); }
      }
    }
    const expected = ids.size - expired;
    manifest.sourceAudit[table] = { raw: manifest.tables[table].records, unique: ids.size, duplicates, outsideRetention: expired, expectedQueryable: expected, actualQueryable: actual[kind] || 0 };
    if ((actual[kind] || 0) !== expected)
      throw new Error(`Axiom verification mismatch for ${table}: ${actual[kind] || 0} != ${expected} (after deduplication and 30-day retention)`);
  }
  await writeFile(new URL('expired-before-axiom-window.json.gz', folder), gzipSync(JSON.stringify(expiredBackup)));
  delete manifest.error;
  manifest.finishedAt = new Date().toISOString();
  await save();
  console.log('Migration completed. Verification:', JSON.stringify(verification));
} catch (error) {
  manifest.error = error.message;
  await save();
  console.error(error.message);
  process.exitCode = 1;
}

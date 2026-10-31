import { readFile, writeFile } from 'node:fs/promises';
import { PANELS, queryAxiom, EDGE } from '../src/axiom.js';
const folder = new URL('../../artifacts/telemetry-migration/', import.meta.url);
const queryToken = (await readFile(new URL('query-token.txt', folder), 'utf8')).trim();
const ingestToken = (await readFile(new URL('ingest-token.txt', folder), 'utf8')).trim();
let results = {};
if (process.env.VERIFY_FAILED_ONLY === '1') {
  results = JSON.parse(await readFile(new URL('axiom-verification.json', folder), 'utf8'));
}
for (const [name, panel] of Object.entries(PANELS)) {
  if (process.env.VERIFY_FAILED_ONLY === '1' && results[name]?.ok === true) continue;
  try {
    let value;
    for (let attempt = 0; ; attempt++) {
      try {
        value = await queryAxiom({ AXIOM_QUERY_TOKEN: queryToken }, panel.query,
          { startTime: new Date(Date.now() - panel.rangeSeconds * 1000).toISOString() });
        break;
      } catch (error) {
        if (attempt >= 2 || !/fetch failed|timeout|timed out|\(429\)|\(50\d\)/i.test(error.message)) throw error;
      }
    }
    results[name] = { ok: true, columns: value.columns, rows: value.rows.length };
    console.log(`${name}: OK (${value.rows.length} rows)`);
  } catch (error) {
    results[name] = { ok: false, error: error.message };
    console.log(`${name}: ${error.message}`);
  }
}
// 验证权限隔离：只读凭据不能写入，客户端只写凭据不能查询。
const queryWithIngest = await fetch(`${EDGE}/v1/query/_apl?format=tabular`, {
  method: 'POST', headers: { authorization: `Bearer ${ingestToken}`, 'content-type': 'application/json' },
  body: JSON.stringify({ apl: "['tuba-telemetry'] | count" }), signal: AbortSignal.timeout(25000),
});
const ingestWithQuery = await fetch(`${EDGE}/v1/ingest/tuba-telemetry`, {
  method: 'POST', headers: { authorization: `Bearer ${queryToken}`, 'content-type': 'application/json' },
  body: '[]', signal: AbortSignal.timeout(25000),
});
results.permissions = { clientCannotRead: queryWithIngest.status === 403, dashboardCannotWrite: ingestWithQuery.status === 403 };
console.log('Permission isolation:', results.permissions);
await writeFile(new URL('axiom-verification.json', folder), JSON.stringify(results, null, 2));
if (Object.values(results).some(r => r.ok === false) || !results.permissions.clientCannotRead || !results.permissions.dashboardCannotWrite)
  process.exitCode = 1;

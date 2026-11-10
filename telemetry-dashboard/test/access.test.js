import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { PANELS } from '../src/axiom.js';
import { toolRankings } from '../src/tools.js';
const source = (await readFile(new URL('../src/index.js', import.meta.url), 'utf8')).replace(/^import .*;$/gm, '').replace('export default', 'globalThis.worker =');
function worker() {
  const queries = [];
  const context = { PANELS, toolRankings, Request, Response, URL, Date, Map, console, caches: { default: { match: async () => null, put: async () => {} } }, queryAxiom: async (_, query) => {
    queries.push(query);
    return { rows: query.includes('tool_launch') ? [['CPUZ', 10, 1], ['GPUZ', 7, 7], ['format-converter', 2, 2], ['TestCustomTool', 1, 1]] : [] };
  }, fetch: async () => Response.json([]) };
  vm.runInNewContext(source, context);
  return { worker: context.worker, queries };
}
const env = { AXIOM_QUERY_TOKEN: 'read-only', DASHBOARD_TOKEN: 'admin' };
test('guest summary never queries or exposes either private ranking', async () => {
  const app = worker();
  const res = await app.worker.fetch(new Request('https://stats.test/api/summary?toolMetric=devices&force=1'), env);
  const data = await res.json();
  assert.equal(data.mode, 'public');
  assert.equal('tools' in data.panels, false);
  assert.equal('toolsLeast' in data.panels, false);
  assert.equal('toolsAll' in data.panels, false);
  assert.equal(app.queries.some(q => q.includes('tool_launch')), false);
});
test('invalid credentials are rejected before any telemetry query', async () => {
  const app = worker();
  const res = await app.worker.fetch(new Request('https://stats.test/api/summary', { headers: { 'x-dashboard-token': 'wrong' } }), env);
  assert.equal(res.status, 401);
  assert.equal(app.queries.length, 0);
});
test('admin gets searchable catalog, zero-use tools and both rankings from one query', async () => {
  const app = worker();
  async function summary(metric) {
    return (await app.worker.fetch(new Request('https://stats.test/api/summary?toolMetric=' + metric, { headers: { 'x-dashboard-token': 'admin' } }), env)).json();
  }
  const data = await summary('uses');
  assert.equal(data.mode, 'full');
  const rows = data.panels.toolsAll.rows;
  assert.ok(rows.length > 100);
  assert.ok(rows.some(([name, uses, devices]) => name === '句柄清理' && uses === 0 && devices === 0));
  assert.ok(rows.some(([name, uses]) => name === 'TestCustomTool' && uses === 1));
  assert.equal(rows.filter(([name]) => name === 'CPU-Z').length, 1);
  assert.equal(rows.find(([name]) => name === '格式转换')[1], 2);
  assert.equal(data.panels.tools.rows[0][0], 'CPU-Z');
  assert.equal(data.panels.toolsLeast.rows.length, 10);
  assert.ok(data.panels.toolsLeast.rows.every(row => row[1] === 0));
  const byDevices = await summary('devices');
  assert.equal(byDevices.panels.tools.rows[0][0], 'GPU-Z');
  assert.equal(app.queries.filter(q => q.includes('tool_launch')).length, 1);
});
test('tool usage deduplicates events and reads beyond the rank limits', () => {
  const query = PANELS.toolUsage.query;
  assert.match(query, /arg_max\(_time, user_Id, tool\) by event_id/);
  assert.match(query, /isnotempty\(tool\)/);
  assert.doesNotMatch(query, /\| (?:take|top|limit) /);
});

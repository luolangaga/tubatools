import test from 'node:test';
import assert from 'node:assert/strict';
import { decodeTable, queryAxiom } from '../src/axiom.js';

test('Axiom column-oriented output becomes row-oriented dashboard data', () => {
  assert.deepEqual(decodeTable({ tables: [{ fields: [{ name: 'timestamp' }, { name: 'devices' }], columns: [['day1', 'day2'], [12, 34]] }] }),
    { columns: ['timestamp', 'devices'], rows: [['day1', 12], ['day2', 34]] });
});
test('partial query output is rejected instead of displaying incorrect totals', () => {
  assert.throws(() => decodeTable({ status: { isPartial: true }, tables: [] }), /不完整/);
});
test('valid empty datasets do not fail or invent rows', () => {
  assert.deepEqual(decodeTable({ tables: [] }), { columns: [], rows: [] });
  assert.deepEqual(decodeTable({ tables: [{ fields: [{ name: 'count' }], columns: [[]] }] }), { columns: ['count'], rows: [] });
});
test('query uses a bounded time range and keeps credentials out of the request URL/body', async t => {
  t.mock.method(globalThis, 'fetch', async (url, options) => {
    assert.equal(url, 'https://us-east-1.aws.edge.axiom.co/v1/query/_apl?format=tabular');
    assert.equal(options.headers.authorization, 'Bearer query-only');
    const body = JSON.parse(options.body);
    assert.equal(body.startTime, '2026-10-01T00:00:00Z');
    assert.equal(body.endTime, '2026-10-02T00:00:00Z');
    assert.equal(JSON.stringify(body).includes('query-only'), false);
    return Response.json({ tables: [] });
  });
  await queryAxiom({ AXIOM_QUERY_TOKEN: 'query-only' }, "['tuba-telemetry'] | count",
    { startTime: '2026-10-01T00:00:00Z', endTime: '2026-10-02T00:00:00Z' });
});

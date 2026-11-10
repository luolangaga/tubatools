import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
const html = await readFile(new URL('../public/index.html', import.meta.url), 'utf8');
const source = html.slice(html.indexOf('  async function load('), html.indexOf('  function render(data)'));
function harness(fetch, token = '') {
  const elements = {};
  const saved = new Map();
  const context = { fetch, token, toolMetric: 'uses', requestGeneration: 0, activeRequest: null, lastData: null, REFRESH_MS: 60000, TOKEN_KEY: 'token', AbortController, setTimeout, clearTimeout,
    document: { getElementById: id => elements[id] ||= {} },
    localStorage: { setItem: (k, v) => saved.set(k, v), removeItem: k => saved.delete(k) },
    render: data => { context.lastData = data; },
    showGate: message => { context.gateError = message; },
    hideGate: () => { context.hidden = true; }
  };
  vm.runInNewContext(source, context);
  return { context, saved };
}
test('guest background refresh leaves a login dialog open', async () => {
  const { context } = harness(async () => Response.json({ mode: 'public' }));
  await context.load();
  assert.equal(context.hidden, undefined);
});
test('successful administrator login remembers the validated password and closes dialog', async () => {
  const { context, saved } = harness(async () => Response.json({ mode: 'full' }), 'valid');
  await context.load();
  assert.equal(saved.get('token'), 'valid');
  assert.equal(context.hidden, true);
});
test('incorrect password returns to public data while preserving error dialog', async () => {
  let calls = 0;
  const { context, saved } = harness(async () => ++calls === 1 ? new Response('{}', { status: 401 }) : Response.json({ mode: 'public' }), 'wrong');
  await context.load();
  assert.match(context.gateError, /不正确/);
  assert.equal(context.hidden, undefined);
  assert.equal(context.lastData.mode, 'public');
  assert.equal(saved.size, 0);
});
test('late privileged responses cannot replace a newer lock request', async () => {
  let completeOld;
  let calls = 0;
  const { context } = harness(() => ++calls === 1 ? new Promise(resolve => { completeOld = resolve; }) : Promise.resolve(Response.json({ mode: 'public' })), 'valid');
  const old = context.load();
  context.token = '';
  await context.load();
  completeOld(Response.json({ mode: 'full' }));
  await old;
  assert.equal(context.lastData.mode, 'public');
});

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const html = await readFile(new URL('../public/index.html', import.meta.url), 'utf8');
const source = html.slice(html.indexOf('  function renderTools('), html.indexOf('  function renderErrors('));
function renderer(metric = 'uses', search = '') {
  const elements = { 'c-tools': { innerHTML: '' }, 'c-tools-least': { innerHTML: '' }, 'tools-search': { value: search }, 'tools-locked': { classList: { toggle() {} } }, 'tools-least-locked': { classList: { toggle() {} } } };
  const bars = [];
  Object.assign(elements, { 'tool-results': {}, 'tool-result-count': {}, 'tool-directory': { classList: { toggle() {} } } });
  const context = { toolMetric: metric, document: { getElementById: id => elements[id] }, PALETTE: ['blue'], hbar: (...args) => bars.push(args), esc: String };
  vm.runInNewContext(source, context);
  return { render: context.renderTools, renderDirectory: context.renderToolDirectory, elements, bars };
}
test('query failure is not misreported as missing launch data', () => {
  const view = renderer();
  view.render(undefined, true, 'query failed');
  assert.match(view.elements['c-tools'].innerHTML, /查询失败/);
  assert.equal(view.bars.length, 0);
});
test('an empty ranking displays an empty state', () => {
  const view = renderer();
  view.render({ rows: [] }, true);
  assert.equal(view.bars[0][1].length, 0);
  assert.match(view.bars[0][3], /暂无工具统计/);
});
test('populated tool statistics render name, launches, and device counts', () => {
  const view = renderer();
  view.render({ rows: [['CPUZ', 7, 3]] }, true);
  assert.equal(view.bars[0][1][0].name, 'CPUZ');
  assert.equal(view.bars[0][1][0].value, 7);
  assert.equal(view.bars[0][1][0].sub, '3 台设备');
});
test('guest view never renders private tool statistics', () => {
  const view = renderer();
  view.render({ rows: [['private', 7, 3]] }, false);
  assert.equal(view.bars.length, 0);
  assert.equal(view.elements['c-tools'].innerHTML, '');
});

test('least-used tools render the independent ascending result, including ties', () => {
  const view = renderer();
  view.render({ rows: [['A', 1, 1], ['B', 1, 1], ['C', 2, 1]] }, true, null, true);
  assert.equal(view.bars[0][0], view.elements['c-tools-least']);
  assert.deepEqual(Array.from(view.bars[0][1], x => x.name), ['A', 'B', 'C']);
});

test('device metric displays devices as the primary value and launches as context', () => {
  const view = renderer('devices');
  view.render({ rows: [['CPUZ', 17, 3]] }, true);
  assert.equal(view.bars[0][1][0].value, 3);
  assert.equal(view.bars[0][1][0].unit, ' 台');
  assert.equal(view.bars[0][1][0].sub, '17 次启动');
});

test('full directory search finds a zero-use tool by its telemetry ID and clears on logout', () => {
  const view = renderer('uses', ' gpu ');
  const panel = { rows: [['CPU-Z', 17, 3, 'CPUZ'], ['显卡天梯图', 0, 0, 'gpu-ranking']] };
  view.renderDirectory(panel, true);
  assert.match(view.elements['tool-results'].innerHTML, /显卡天梯图/);
  assert.doesNotMatch(view.elements['tool-results'].innerHTML, /CPU-Z/);
  assert.match(view.elements['tool-result-count'].textContent, /1 \/ 2/);
  view.renderDirectory(panel, false);
  assert.equal(view.elements['tool-results'].innerHTML, '');
});

test('guest cannot render least-used tool details and clears stale data', () => {
  const view = renderer();
  view.elements['c-tools-least'].innerHTML = 'private';
  view.render({ rows: [['private', 1, 1]] }, false, null, true);
  assert.equal(view.bars.length, 0);
  assert.equal(view.elements['c-tools-least'].innerHTML, '');
});

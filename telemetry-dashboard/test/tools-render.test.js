import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const html = await readFile(new URL('../public/index.html', import.meta.url), 'utf8');
const source = html.slice(html.indexOf('  function renderTools('), html.indexOf('  function renderErrors('));
function renderer() {
  const elements = { 'c-tools': { innerHTML: '' }, 'tools-locked': { classList: { toggle() {} } } };
  const bars = [];
  const context = { document: { getElementById: id => elements[id] }, PALETTE: ['blue'], hbar: (...args) => bars.push(args) };
  vm.runInNewContext(source, context);
  return { render: context.renderTools, elements, bars };
}
test('query failure is not misreported as missing launch data', () => {
  const view = renderer();
  view.render(undefined, true, 'query failed');
  assert.match(view.elements['c-tools'].innerHTML, /查询失败/);
  assert.equal(view.bars.length, 0);
});
test('empty launch data explains that a client update is required', () => {
  const view = renderer();
  view.render({ rows: [] }, true);
  assert.equal(view.bars[0][1].length, 0);
  assert.match(view.bars[0][3], /更新到支持工具统计的版本/);
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

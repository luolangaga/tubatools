import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../TubaWinUi3.WinUI3/Services/FluentPageHtml.cs', import.meta.url), 'utf8');
const script = source.slice(source.indexOf('async function uploadFiles('), source.indexOf('const dropZone ='));
function harness() {
  const elements = Object.fromEntries(['uploadProgress', 'uploadFill', 'uploadTitle', 'fileInput', 'folderInput']
    .map(id => [id, { value: 'selected-file', style: {}, classList: { add() {}, remove() {} } }]));
  const messages = [];
  let xhr;
  const context = {
    document: { getElementById: id => elements[id] }, shareSession: Promise.resolve(),
    FormData: class { append() {} },
    XMLHttpRequest: class { constructor() { xhr = this; this.upload = {}; } open() {} send() {} },
    showToast: message => messages.push(message), refreshFiles() {},
  };
  vm.runInNewContext(script, context);
  return { run: context.uploadFiles, elements, messages, xhr: () => xhr };
}
for (const status of [401, 413, 429, 500]) {
  test(`HTTP ${status} preserves selection and reports failure`, async () => {
    const h = harness(); await h.run([{ name: 'test.txt' }]);
    Object.assign(h.xhr(), { status, responseText: '{"error":"server rejected upload"}' }); h.xhr().onload();
    assert.deepEqual(h.messages, ['server rejected upload']);
    assert.equal(h.elements.fileInput.value, 'selected-file');
  });
}
test('successful upload clears selection only after the response', async () => {
  const h = harness(); await h.run([{ name: 'test.txt' }]);
  assert.equal(h.elements.fileInput.value, 'selected-file');
  Object.assign(h.xhr(), { status: 200, responseText: '{"ok":true}' }); h.xhr().onload();
  assert.match(h.messages[0], /已上传/); assert.equal(h.elements.fileInput.value, '');
});

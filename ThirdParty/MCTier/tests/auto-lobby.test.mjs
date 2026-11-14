import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

function effect(file, marker) {
  const ast = ts.createSourceFile(file, fs.readFileSync(new URL(file, import.meta.url), 'utf8'), ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  let match;
  function visit(node) {
    if (ts.isCallExpression(node) && node.expression.getText(ast) === 'useEffect' && node.arguments[0].getText(ast).includes(marker)) match = node.arguments[0];
    ts.forEachChild(node, visit);
  }
  visit(ast);
  assert.ok(match);
  return ts.transpileModule(`(${match.getText(ast)})();`, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText;
}
const startup = effect('../src/components/MainWindow/MainWindow.tsx', 'const checkAutoLobby');
const formEffect = effect('../src/components/LobbyForm/LobbyForm.tsx', 'const autoConfig');

test('startup selects join and submits the saved encrypted password, including an empty password', async () => {
  for (const password of ['', 'mctier-local-v1:AAAA']) {
    const window = {};
    const timers = [];
    const settings = { autoLobbyEnabled: true, lobbyName: 'room', playerName: 'Alice', lobbyPassword: password };
    let mode;
    let formShown = false;
    const context = { window, startupReady: true, workspaceActive: true, async invoke() { return settings; }, setFormMode(value) { mode = value; },
      setShowForm(value) { formShown = value; }, setEnableGpuRendering() {}, console: { log() {}, error() {} },
      setTimeout(fn) { timers.push(fn); return 1; }, clearTimeout() {} };
    vm.runInNewContext(startup, context);
    await timers.shift()();
    assert.equal(mode, 'join');
    assert.equal(formShown, true);
    let fields;
    let submits = 0;
    const pendingAutoConfig = { current: null };
    const automaticEntry = { current: false };
    vm.runInNewContext(formEffect, { ...context, mode, pendingAutoConfig, automaticEntry, resolvedPreferredServer: 'udp://selected:11010',
      form: { setFieldsValue(value) { fields = value; }, submit() { submits++; } } });
    timers.shift()();
    assert.equal(fields.password, password);
    assert.equal(fields.lobbyName, settings.lobbyName);
    assert.equal(submits, 1);
    assert.equal(automaticEntry.current, true);
    assert.equal(window.__autoLobbyConfig, undefined);
    assert.equal(pendingAutoConfig.current, null);
  }
});

test('a manual action taken while settings load cannot be replaced by auto join', async () => {
  const window = {};
  let resolve;
  let timer;
  vm.runInNewContext(startup, { window, startupReady: true, workspaceActive: true, invoke: () => new Promise(r => { resolve = r; }),
    setFormMode() { assert.fail('manual mode must survive'); }, setShowForm() { assert.fail('manual view must survive'); },
    setEnableGpuRendering() {}, console: { log() {}, error() {} }, setTimeout(fn) { timer = fn; }, clearTimeout() {} });
  const pending = timer();
  window.__autoLobbyTriggered = true;
  resolve({ autoLobbyEnabled: true, lobbyName: 'room', playerName: 'Alice' });
  await pending;
});

test('startup prompts defer auto join without consuming its once-only flag', () => {
  const window = {};
  vm.runInNewContext(startup, {window, startupReady:false, workspaceActive:true, invoke(){assert.fail('No settings request before startup prompts settle');}, setTimeout(){assert.fail('No auto join timer while blocked');}});
  assert.equal(window.__autoLobbyTriggered, undefined);
});

test('hidden preloading defers automatic lobby entry until the multiplayer page is opened', async () => {
  const window = {};
  const timers = [];
  let settingsRequests = 0;
  let shown = false;
  const context = { window, startupReady: true, workspaceActive: false,
    async invoke() { settingsRequests++; return { autoLobbyEnabled: true, lobbyName: 'room', playerName: 'Alice', lobbyPassword: '' }; },
    setFormMode(mode) { assert.equal(mode, 'join'); }, setShowForm(value) { shown = value; }, setEnableGpuRendering() {},
    console: { log() {}, error() {} }, setTimeout(fn) { timers.push(fn); return 1; }, clearTimeout() {} };
  vm.runInNewContext(startup, context);
  assert.equal(settingsRequests, 0);
  assert.equal(timers.length, 0);
  assert.equal(window.__autoLobbyTriggered, undefined);
  context.workspaceActive = true;
  vm.runInNewContext(startup, context);
  await timers.shift()();
  assert.equal(settingsRequests, 1);
  assert.equal(shown, true);
  vm.runInNewContext(startup, context);
  assert.equal(timers.length, 0, 'returning to the page must not join again');
});

test('leaving the multiplayer page while settings load cancels its automatic entry', async () => {
  let timer;
  let resolve;
  const cleanup = vm.runInNewContext(startup, { window: {}, startupReady: true, workspaceActive: true,
    invoke: () => new Promise(done => { resolve = done; }), setTimeout(fn) { timer = fn; }, clearTimeout() {},
    setFormMode() { assert.fail('hidden page must not start a connection'); }, setShowForm() { assert.fail('hidden page must not open its form'); },
    setEnableGpuRendering() {}, console: { log() {}, error() {} } });
  const pending = timer();
  cleanup();
  resolve({ autoLobbyEnabled: true, lobbyName: 'room', playerName: 'Alice' });
  await pending;
});

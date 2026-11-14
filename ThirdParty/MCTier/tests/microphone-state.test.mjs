import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';

const context = vm.createContext({ exports: {} });
vm.runInContext(ts.transpileModule(fs.readFileSync('src/services/voice/microphoneState.ts', 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, context);

test('opening remains unlit until capture succeeds and stale opens cannot undo a close', async () => {
  const pending = [], displayed = [];
  const apply = context.exports.synchronizeMicrophoneState({
    apply: value => new Promise(resolve => pending.push({ value, resolve })),
    display: value => displayed.push(value), disableBackend: async () => {}, failed: () => assert.fail('unexpected failure'),
  });
  const opening = apply(true);
  assert.deepEqual(displayed, []);
  const closing = apply(false);
  assert.deepEqual(displayed, [false]);
  pending[0].resolve(); await opening;
  assert.deepEqual(displayed, [false]);
  pending[1].resolve(); await closing;
  assert.deepEqual(displayed, [false, false]);
  const retry = apply(true); pending[2].resolve(); await retry;
  assert.equal(displayed.at(-1), true);
});

test('capture failures clear UI and backend state, release audio and permit a later retry', async () => {
  let failure = true;
  const displayed = [], applications = [], errors = [];
  let disabled = 0;
  const apply = context.exports.synchronizeMicrophoneState({
    apply: async value => { applications.push(value); if (value && failure) throw new Error('device unavailable'); },
    display: value => displayed.push(value), disableBackend: async () => { disabled++; }, failed: error => errors.push(error),
  });
  await apply(true);
  assert.deepEqual(applications, [true, false]);
  assert.deepEqual(displayed, [false]); assert.equal(disabled, 1); assert.equal(errors.length, 1);
  failure = false; await apply(true);
  assert.equal(displayed.at(-1), true);
});

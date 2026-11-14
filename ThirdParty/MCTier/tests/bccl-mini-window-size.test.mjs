import test from 'node:test';
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';

async function fixture(embedded) {
  const result = await build({
    entryPoints: [fileURLToPath(new URL('../src/utils/miniWindowSize.ts', import.meta.url))],
    bundle: true, format: 'esm', write: false,
    define: { 'import.meta.env.VITE_BCCL_EMBEDDED': JSON.stringify(embedded ? '1' : '0') },
    plugins: [{ name: 'owned-window', setup(builder) {
      builder.onResolve({ filter: /^@tauri-apps\/api\/(window|dpi)$/ }, args => ({ path: args.path, namespace: 'fixture' }));
      builder.onLoad({ filter: /.*/, namespace: 'fixture' }, ({ path }) => ({ contents:
        path.endsWith('/window')
          ? 'export const getCurrentWindow=()=>{globalThis.miniSizeFixture.lookups++; return {setSize: async size=>globalThis.miniSizeFixture.sizes.push([size.width,size.height])};};'
          : 'export class LogicalSize { constructor(width,height) {this.width=width;this.height=height;} }'
      }));
    } }],
  });
  return import(`data:text/javascript,${encodeURIComponent(result.outputFiles[0].text)}`);
}

test('embedded room collapse/expand leaves the launcher-owned native viewport intact', async () => {
  const api = await fixture(true);
  globalThis.miniSizeFixture = { lookups: 0, sizes: [] };
  try {
    for (let cycle = 0; cycle < 10; cycle++) {
      await api.resizeMiniWindow(true);
      await api.resizeMiniWindow(false);
    }
    assert.equal(globalThis.miniSizeFixture.lookups, 0);
    assert.deepEqual(globalThis.miniSizeFixture.sizes, []);
  } finally { delete globalThis.miniSizeFixture; }
});

test('standalone mini window retains its original compact and expanded sizes', async () => {
  const api = await fixture(false);
  globalThis.miniSizeFixture = { lookups: 0, sizes: [] };
  try {
    await api.resizeMiniWindow(true);
    await api.resizeMiniWindow(false);
    assert.deepEqual(globalThis.miniSizeFixture.sizes, [[320, 50], [320, 520]]);
  } finally { delete globalThis.miniSizeFixture; }
});

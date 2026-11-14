import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';

// Exercise the real room component's event handlers and rendered content. The
// fixture supplies hook state and services without joining a real user's room.
const entry = fileURLToPath(new URL('../src/components/MiniWindow/MiniWindow.tsx', import.meta.url));
const syntax = ts.createSourceFile(entry, fs.readFileSync(entry, 'utf8'), ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const imports = new Map();
for (const node of syntax.statements) {
  if (!ts.isImportDeclaration(node)) continue;
  const clause = node.importClause;
  const previous = imports.get(node.moduleSpecifier.text);
  imports.set(node.moduleSpecifier.text, {
    default: !!clause?.name || previous?.default,
    names: [...new Set([...(previous?.names ?? []), ...(clause?.namedBindings && ts.isNamedImports(clause.namedBindings)
      ? clause.namedBindings.elements.filter(item => !item.isTypeOnly).map(item => (item.propertyName ?? item.name).text) : [])])],
  });
}
const bundle = await build({
  entryPoints: [entry], bundle: true, format: 'esm', write: false, jsx: 'automatic',
  define: { 'import.meta.env.VITE_BCCL_EMBEDDED': '"1"' },
  plugins: [{ name: 'room-ui-fixture', setup(builder) {
    builder.onResolve({ filter: /.*/ }, args => {
      if (args.kind === 'entry-point' || /(?:miniWindowSize|bcclEmbedding)$/.test(args.path)) return;
      return { path: args.path, namespace: 'fixture' };
    });
    builder.onLoad({ filter: /.*/, namespace: 'fixture' }, ({ path }) => {
      if (path === 'react/jsx-runtime') return { contents: 'export const jsx=(type,props)=>({type,props}); export const jsxs=jsx; export const Fragment="Fragment";' };
      const shape = imports.get(path) ?? { names: path.endsWith('/window') ? ['getCurrentWindow'] : ['LogicalSize'] };
      return { contents: `const m=globalThis.miniUiFixture.modules[${JSON.stringify(path)}]??{};\n`
        + shape.names.map(name => `export const ${name}=m.${name}??${JSON.stringify(name)};`).join('\n')
        + (shape.default ? '\nexport default m.default??{};' : '') };
    });
  } }],
});

function nodes(tree) {
  if (!tree || typeof tree !== 'object') return [];
  if (Array.isArray(tree)) return tree.flatMap(nodes);
  return [tree, ...nodes(tree.props?.children)];
}

test('real embedded room repeatedly folds, restores players/tools, and returns from collapsed chat without native resizing', async () => {
  const state = [], sizes = [];
  let index = 0;
  const react = {
    useState(initial) {
      const key = index++;
      if (!(key in state)) state[key] = typeof initial === 'function' ? initial() : initial;
      return [state[key], value => { state[key] = typeof value === 'function' ? value(state[key]) : value; }];
    },
    useEffect() {}, useCallback: value => value, useRef: current => ({ current }),
  };
  const store = {
    lobby: { name: 'Layout verification', virtualIp: '10.0.0.1', password: '' },
    players: [{ id: 'self', name: 'Local test', virtualIp: '10.0.0.1' }],
    config: {}, currentPlayerId: 'self', hostId: 'self', chatMessages: [],
    mutedPlayers: new Set(), speakingPlayers: new Set(), hostMutedPlayers: new Set(),
    playerVoiceGroups: new Map(), myVoiceGroup: 0, micEnabled: false,
    getPlayerVolume: () => 1,
  };
  const useAppStore = selector => selector ? selector(store) : store;
  useAppStore.getState = () => store;
  const originalStorage = globalThis.localStorage, originalWindow = globalThis.window;
  globalThis.localStorage = { getItem: () => null };
  globalThis.window = { setTimeout: callback => setTimeout(callback, 0) };
  globalThis.miniUiFixture = { modules: {
    react: { ...react, default: react },
    'framer-motion': { motion: new Proxy({}, { get: (_, key) => `motion.${key}` }), AnimatePresence: 'AnimatePresence' },
    antd: { App: { useApp: () => ({ message: {}, modal: {} }) } },
    'react-i18next': { useTranslation() {} },
    '../../stores': { useAppStore },
    '../../i18n': { tl: zh => zh, getLanguage: () => 'zh-CN' },
    '../../services/recent/recentService': { recentService: { getFavoritePlayers: () => [] } },
    '../../services/chat/peerPreferences': { notificationUnreadCount: () => 1 },
    '@tauri-apps/api/window': { getCurrentWindow: () => ({ setSize: async value => sizes.push(value) }) },
    '@tauri-apps/api/dpi': { LogicalSize: class { constructor(width, height) { Object.assign(this, { width, height }); } } },
  } };
  try {
    const { MiniWindow } = await import(`data:text/javascript,${encodeURIComponent(bundle.outputFiles[0].text)}`);
    const render = () => { index = 0; return nodes(MiniWindow()); };
    let tree = render();
    for (let cycle = 0; cycle < 6; cycle++) {
      assert.ok(tree.some(node => node.props?.className === 'mini-window-content'));
      await tree.find(node => node.props?.title === '收起').props.onClick();
      tree = render();
      assert.ok(!tree.some(node => node.props?.className === 'mini-window-content'));
      await tree.find(node => node.props?.title === '展开').props.onClick();
      tree = render();
      assert.ok(tree.some(node => node.props?.className === 'mini-window-content'));
    }
    await tree.find(node => node.props?.title === '收起').props.onClick();
    tree = render();
    await tree.find(node => node.props?.title === '1 条新消息').props.onClick();
    tree = render();
    assert.ok(tree.some(node => node.props?.className === 'chat-room-view'));
    const back = tree.find(node => node.props?.className === 'back-button');
    await back.props.onClick();
    tree = render();
    await tree.find(node => node.props?.title === '展开').props.onClick();
    tree = render();
    assert.ok(tree.some(node => node.props?.className === 'mini-window-content'));
    assert.deepEqual(sizes, []);
  } finally {
    delete globalThis.miniUiFixture;
    globalThis.localStorage = originalStorage; globalThis.window = originalWindow;
  }
});

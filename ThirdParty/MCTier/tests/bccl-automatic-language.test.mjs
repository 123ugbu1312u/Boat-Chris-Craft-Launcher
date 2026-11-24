import test from 'node:test';
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';

async function fixture(embedded, stored, systemLanguage) {
  const values = new Map([['mctier_language', stored]]);
  const previousNavigator = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  const previousStorage = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
  Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { language: systemLanguage } });
  Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
  } });
  try {
    const result = await build({
      entryPoints: [fileURLToPath(new URL('../src/i18n/index.ts', import.meta.url))],
      bundle: true, format: 'esm', write: false,
      define: { 'import.meta.env.VITE_BCCL_EMBEDDED': JSON.stringify(embedded ? '1' : '0') },
      plugins: [{ name: 'headless-language', setup(builder) {
        builder.onResolve({ filter: /^(react-i18next|@tauri-apps\/api\/event)$/ }, args => ({ path: args.path, namespace: 'fixture' }));
        builder.onLoad({ filter: /.*/, namespace: 'fixture' }, ({ path }) => ({ contents:
          path === 'react-i18next'
            ? "export const initReactI18next = {type:'3rdParty',init(){}};"
            : 'export async function emit() {}'
        }));
      } }],
    });
    const api = await import(`data:text/javascript,${encodeURIComponent(result.outputFiles[0].text)}#${Math.random()}`);
    return { api, values, close() {
      if (previousNavigator) Object.defineProperty(globalThis, 'navigator', previousNavigator);
      else delete globalThis.navigator;
      if (previousStorage) Object.defineProperty(globalThis, 'localStorage', previousStorage);
      else delete globalThis.localStorage;
    } };
  } catch (error) {
    if (previousNavigator) Object.defineProperty(globalThis, 'navigator', previousNavigator);
    if (previousStorage) Object.defineProperty(globalThis, 'localStorage', previousStorage);
    throw error;
  }
}

test('embedded Chinese launcher overrides both stored English and English WebView locale', async () => {
  const { api, values, close } = await fixture(true, 'en', 'en-US');
  try {
    api.synchronizeHostLanguage('zh-CN');
    assert.equal(api.getLanguage(), 'zh');
    assert.equal(api.getLanguagePreference(), 'system');
    assert.equal(values.get('mctier_language'), 'system');
    api.setLanguagePreference('en');
    assert.equal(api.getLanguage(), 'zh');
    assert.equal(api.tl('创建大厅', 'Create Lobby'), '创建大厅');
  } finally { close(); }
});

test('embedded language follows live launcher changes and falls back to Chinese for untranslated cultures', async () => {
  const { api, close } = await fixture(true, 'zh', 'zh-CN');
  try {
    for (const [culture, expected] of [['en-US', 'en'], ['zh-TW', 'zh'], ['en-GB', 'en'], ['ja-JP', 'zh'], ['fr-FR', 'zh']]) {
      api.synchronizeHostLanguage(culture);
      assert.equal(api.getLanguage(), expected);
    }
  } finally { close(); }
});

test('embedded mode uses system language when the launcher language is temporarily unavailable', async () => {
  const { api, close } = await fixture(true, 'en', 'zh-CN');
  try {
    assert.equal(api.getLanguage(), 'zh');
    api.synchronizeHostLanguage(undefined);
    assert.equal(api.getLanguage(), 'zh');
    navigator.language = 'en-GB';
    api.synchronizeHostLanguage(undefined);
    assert.equal(api.getLanguage(), 'en');
  } finally { close(); }
});

test('standalone language preferences remain independent of the launcher', async () => {
  const { api, close } = await fixture(false, 'en', 'zh-CN');
  try {
    assert.equal(api.getLanguage(), 'en');
    api.synchronizeHostLanguage('zh-CN');
    assert.equal(api.getLanguage(), 'en');
    api.setLanguagePreference('zh');
    assert.equal(api.getLanguage(), 'zh');
    assert.equal(api.getLanguagePreference(), 'zh');
  } finally { close(); }
});

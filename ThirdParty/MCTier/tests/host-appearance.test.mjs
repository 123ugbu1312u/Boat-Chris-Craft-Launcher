import test from 'node:test';
import assert from 'node:assert/strict';
import { isHostAppearance, resolveWorkspaceTheme } from '../src/theme/hostAppearancePolicy.ts';

const light = { theme: 'light', background: '#F1F5FA', top: '#F5F8FC', bottom: '#DEEAF3' };
const dark = { ...light, theme: 'dark' };
test('automatic theme follows the launcher when Windows uses the opposite theme', () => {
  assert.equal(resolveWorkspaceTheme('system', true, light), 'light');
  assert.equal(resolveWorkspaceTheme('system', false, dark), 'dark');
});
test('explicit theme choices remain available and standalone falls back to Windows', () => {
  assert.equal(resolveWorkspaceTheme('light', true, dark), 'light');
  assert.equal(resolveWorkspaceTheme('dark', false, light), 'dark');
  assert.equal(resolveWorkspaceTheme('system', true, null), 'dark');
  assert.equal(resolveWorkspaceTheme('system', false, null), 'light');
});
test('host palettes accept only complete literal RGB colors and known themes', () => {
  assert.equal(isHostAppearance(light), true);
  for (const value of [null, {}, { ...light, theme: 'system' }, { ...light, top: 'url(https://example.com)' }, { ...light, bottom: '#fff' }, { ...light, background: '#abcdef; color:red' }])
    assert.equal(isHostAppearance(value), false);
});

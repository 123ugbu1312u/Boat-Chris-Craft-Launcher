import { useSyncExternalStore } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { BCCL_EMBEDDED } from '../bcclEmbedding';
import { readThemePreference, THEME_CHANGED_EVENT } from './themePreference';
import { isHostAppearance, resolveWorkspaceTheme, type HostAppearance } from './hostAppearancePolicy';

let palette: HostAppearance | null = null;
const listeners = new Set<() => void>();
export function useHostAppearance() {
  return useSyncExternalStore(callback => { listeners.add(callback); return () => { listeners.delete(callback); }; }, () => palette);
}

function applyPalette() {
  const root = document.documentElement;
  if (!palette) return;
  const preference = readThemePreference();
  root.toggleAttribute('data-bccl-host-theme', preference === 'system');
  root.style.setProperty('--bccl-host-background', palette.background);
  root.style.setProperty('--bccl-host-top', palette.top);
  root.style.setProperty('--bccl-host-bottom', palette.bottom);
  const theme = resolveWorkspaceTheme(preference, matchMedia('(prefers-color-scheme: dark)').matches, palette);
  root.dataset.theme = theme;
  root.style.colorScheme = theme;
}

export function startHostAppearanceSync(): () => void {
  if (!BCCL_EMBEDDED) return () => {};
  let active = true;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const poll = async () => {
    try {
      const next: unknown = await invoke('bccl_host_appearance');
      if (active && isHostAppearance(next) && JSON.stringify(next) !== JSON.stringify(palette)) {
        palette = next;
        applyPalette();
        listeners.forEach(notify => notify());
      }
    } catch { /* Retain the last palette while the launcher is updating it. */ }
    finally { if (active) timer = setTimeout(poll, 750); }
  };
  window.addEventListener(THEME_CHANGED_EVENT, applyPalette);
  void poll();
  return () => { active = false; if (timer) clearTimeout(timer); window.removeEventListener(THEME_CHANGED_EVENT, applyPalette); };
}

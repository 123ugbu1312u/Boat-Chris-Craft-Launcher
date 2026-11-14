import { useEffect, useSyncExternalStore } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { BCCL_EMBEDDED } from './bcclEmbedding';

let active = !BCCL_EMBEDDED;
const subscribers = new Set<() => void>();
export function useWorkspaceActive() {
  return useSyncExternalStore(callback => { subscribers.add(callback); return () => { subscribers.delete(callback); }; }, () => active);
}

export function startWorkspaceActivitySync(): () => void {
  if (!BCCL_EMBEDDED || getCurrentWindow().label !== 'main') return () => {};
  let disposed = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const poll = async () => {
    try {
      const next = await invoke<boolean>('bccl_workspace_active');
      if (!disposed && typeof next === 'boolean' && next !== active) {
        active = next;
        subscribers.forEach(callback => callback());
      }
    } catch { /* Retain state while the native host is changing pages. */ }
    finally { if (!disposed) timer = setTimeout(poll, 250); }
  };
  void poll();
  return () => { disposed = true; if (timer) clearTimeout(timer); };
}

// Hidden WebViews may pause animation frames. A committed React layout and a
// short timer are used instead; the native host controls when the surface appears.
export function useWorkspaceReady(ready = true): void {
  useEffect(() => {
    if (!ready || !BCCL_EMBEDDED || getCurrentWindow().label !== 'main') return;
    const timer = setTimeout(() => { void invoke('bccl_workspace_ready').catch(error => console.warn('联机预加载就绪通知失败:', error)); }, 150);
    return () => clearTimeout(timer);
  }, [ready]);
}

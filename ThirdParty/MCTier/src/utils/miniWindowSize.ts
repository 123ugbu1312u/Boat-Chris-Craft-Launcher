import { getCurrentWindow } from '@tauri-apps/api/window';
import { LogicalSize } from '@tauri-apps/api/dpi';
import { BCCL_EMBEDDED } from '../bcclEmbedding';

/** Embedded room content folds inside the launcher-owned viewport. */
export async function resizeMiniWindow(collapsed: boolean): Promise<void> {
  if (BCCL_EMBEDDED) return;
  await getCurrentWindow().setSize(new LogicalSize(320, collapsed ? 50 : 520));
}

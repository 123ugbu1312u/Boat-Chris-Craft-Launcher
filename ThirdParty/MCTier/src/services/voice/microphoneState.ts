interface MicrophoneStateActions {
  apply: (enabled: boolean) => Promise<void>;
  display: (enabled: boolean) => void;
  disableBackend: () => Promise<unknown>;
  failed: (error: unknown) => void;
}

/** Publish an enabled microphone only after capture and sender binding succeed. */
export function synchronizeMicrophoneState(actions: MicrophoneStateActions) {
  let generation = 0;
  return async (enabled: boolean): Promise<void> => {
    const current = ++generation;
    if (!enabled) actions.display(false);
    try {
      await actions.apply(enabled);
      if (current === generation) actions.display(enabled);
    } catch (error) {
      if (current !== generation) return;
      actions.display(false);
      actions.failed(error);
      await actions.apply(false).catch(() => {});
      if (current === generation) await actions.disableBackend().catch(() => {});
    }
  };
}

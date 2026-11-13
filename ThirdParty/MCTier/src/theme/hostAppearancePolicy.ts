import type { EffectiveTheme, ThemePreference } from './themePreference';

export interface HostAppearance {
  theme: EffectiveTheme;
  background: string;
  top: string;
  bottom: string;
}

export function isHostAppearance(value: unknown): value is HostAppearance {
  if (!value || typeof value !== 'object') return false;
  const palette = value as Record<string, unknown>;
  return (palette.theme === 'light' || palette.theme === 'dark') &&
    ['background', 'top', 'bottom'].every(key => typeof palette[key] === 'string' && /^#[\da-f]{6}$/i.test(palette[key] as string));
}

export function resolveWorkspaceTheme(preference: ThemePreference, systemDark: boolean, host: HostAppearance | null): EffectiveTheme {
  return preference === 'system' ? host?.theme ?? (systemDark ? 'dark' : 'light') : preference;
}

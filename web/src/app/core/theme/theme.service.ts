import { DOCUMENT, Injectable, effect, inject, signal } from '@angular/core';

export type ThemeMode = 'system' | 'light' | 'dark';

const STORAGE_KEY = 'cr.ui.theme';
const MODES: readonly ThemeMode[] = ['system', 'light', 'dark'];

/**
 * Light / dark / system theme switch.
 *
 * The mode is reflected as `data-theme` on <html>; the CSS tokens (_tokens.scss, _dark.scss)
 * react to it. Only this UI preference is persisted in localStorage: no token, identifier or
 * business data may ever be stored in web storage (see web/README.md, "Security rules").
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly storage = this.resolveStorage();

  readonly mode = signal<ThemeMode>(this.readStoredMode());

  constructor() {
    effect(() => {
      const mode = this.mode();
      const root = this.document.documentElement;
      if (mode === 'system') {
        root.removeAttribute('data-theme');
      } else {
        root.setAttribute('data-theme', mode);
      }
      this.persist(mode);
    });
  }

  /** Cycles system -> light -> dark -> system. */
  cycle(): void {
    const next = MODES[(MODES.indexOf(this.mode()) + 1) % MODES.length] ?? 'system';
    this.mode.set(next);
  }

  private readStoredMode(): ThemeMode {
    const stored = this.safeGet();
    return MODES.find((mode) => mode === stored) ?? 'system';
  }

  private resolveStorage(): Storage | null {
    try {
      return this.document.defaultView?.localStorage ?? null;
    } catch {
      // Storage can be blocked (privacy settings, sandboxed iframe): fall back to memory only.
      return null;
    }
  }

  private safeGet(): string | null {
    try {
      return this.storage?.getItem(STORAGE_KEY) ?? null;
    } catch {
      return null;
    }
  }

  private persist(mode: ThemeMode): void {
    try {
      if (mode === 'system') {
        this.storage?.removeItem(STORAGE_KEY);
      } else {
        this.storage?.setItem(STORAGE_KEY, mode);
      }
    } catch {
      // Ignore quota or access errors: the preference is a convenience only.
    }
  }
}

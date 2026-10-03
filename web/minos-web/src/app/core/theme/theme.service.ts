import { DOCUMENT } from '@angular/common';
import { DestroyRef, inject, Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark';
const storageKey = 'minos.theme';
const isTheme = (value: unknown): value is Theme => value === 'light' || value === 'dark';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly window = this.document.defaultView;
  private readonly media = this.window?.matchMedia?.('(prefers-color-scheme: dark)');
  private preference = this.readPreference();
  private readonly value = signal<Theme>(this.preference ?? (this.media?.matches ? 'dark' : 'light'));
  readonly theme = this.value.asReadonly();

  constructor() {
    this.apply(this.theme());
    const onSystemChange = () => {
      if (this.preference === null) this.apply(this.media?.matches ? 'dark' : 'light');
    };
    const onStorage = (event: StorageEvent) => {
      if (event.key !== storageKey && event.key !== null) return;
      this.preference = this.readPreference();
      this.apply(this.preference ?? (this.media?.matches ? 'dark' : 'light'));
    };
    this.media?.addEventListener('change', onSystemChange);
    this.window?.addEventListener('storage', onStorage);
    inject(DestroyRef).onDestroy(() => {
      this.media?.removeEventListener('change', onSystemChange);
      this.window?.removeEventListener('storage', onStorage);
    });
  }

  toggle(): void {
    const next = this.theme() === 'dark' ? 'light' : 'dark';
    this.preference = next;
    this.apply(next);
    try { this.window?.localStorage.setItem(storageKey, next); } catch { /* Works in memory without storage. */ }
  }

  private readPreference(): Theme | null {
    try {
      const stored = this.window?.localStorage.getItem(storageKey);
      return isTheme(stored) ? stored : null;
    } catch { return null; }
  }

  private apply(theme: Theme): void {
    this.value.set(theme);
    this.document.documentElement.dataset['theme'] = theme;
    this.document.documentElement.style.colorScheme = theme;
    this.document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme === 'dark' ? '#11271b' : '#f7f8ef');
    this.document.querySelector('link[rel="icon"]')?.setAttribute('href', theme === 'dark' ? 'brand/light-minos.png' : 'brand/dark-minos.png');
  }
}

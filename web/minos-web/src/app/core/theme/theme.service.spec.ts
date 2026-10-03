import { TestBed } from '@angular/core/testing';

import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  let systemDark = false;
  let listener: (() => void) | undefined;
  beforeEach(() => {
    localStorage.removeItem('minos.theme');
    systemDark = false;
    listener = undefined;
    vi.stubGlobal('matchMedia', () => ({
      get matches() { return systemDark; },
      addEventListener: (_event: string, callback: () => void) => { listener = callback; },
      removeEventListener: vi.fn(),
    }));
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks(); vi.unstubAllGlobals();
    localStorage.removeItem('minos.theme');
    delete document.documentElement.dataset['theme'];
    document.documentElement.style.colorScheme = '';
  });

  it('follows system preference until the user explicitly chooses a theme', () => {
    systemDark = true;
    const theme = TestBed.inject(ThemeService);
    expect(theme.theme()).toBe('dark');
    expect(document.documentElement.dataset['theme']).toBe('dark');
    systemDark = false; listener?.();
    expect(theme.theme()).toBe('light');
    theme.toggle();
    expect(theme.theme()).toBe('dark');
    expect(localStorage.getItem('minos.theme')).toBe('dark');
    listener?.();
    expect(theme.theme()).toBe('dark');
  });

  it('restores a valid saved choice, including native control color scheme', () => {
    localStorage.setItem('minos.theme', 'dark');
    const theme = TestBed.inject(ThemeService);
    expect(theme.theme()).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
    theme.toggle();
    expect(document.documentElement.style.colorScheme).toBe('light');
  });

  it('ignores an invalid stored value and reflects changes from another tab', () => {
    localStorage.setItem('minos.theme', 'invalid');
    const theme = TestBed.inject(ThemeService);
    expect(theme.theme()).toBe('light');
    localStorage.setItem('minos.theme', 'dark');
    window.dispatchEvent(new StorageEvent('storage', { key: 'minos.theme', newValue: 'dark' }));
    expect(theme.theme()).toBe('dark');
    localStorage.removeItem('minos.theme');
    window.dispatchEvent(new StorageEvent('storage', { key: 'minos.theme', newValue: null }));
    expect(theme.theme()).toBe('light');
  });

  it('updates the supplied favicon variant and browser chrome together with the theme', () => {
    const icon = document.createElement('link');
    icon.rel = 'icon';
    const meta = document.createElement('meta');
    meta.name = 'theme-color';
    document.head.append(icon, meta);
    try {
      const theme = TestBed.inject(ThemeService);
      expect(icon.getAttribute('href')).toBe('brand/dark-minos.png');
      expect(meta.content).toBe('#f7f8ef');
      theme.toggle();
      expect(icon.getAttribute('href')).toBe('brand/light-minos.png');
      expect(meta.content).toBe('#11271b');
    } finally { icon.remove(); meta.remove(); }
  });

  it('still toggles when browser storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('Blocked'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Blocked'); });
    const theme = TestBed.inject(ThemeService);
    expect(() => theme.toggle()).not.toThrow();
    expect(theme.theme()).toBe('dark');
  });
});

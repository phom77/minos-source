import { DeferBlockState, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Title } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { routes } from './app.routes';
import { SessionService } from './core/auth/session.service';
import { ThemeService } from './core/theme/theme.service';
import { API_CONFIGURATION } from './core/config/api.config';

describe('application routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting(),
      { provide: API_CONFIGURATION, useValue: { apiBaseUrl: '', production: false } },
    ] });
    // Native modal focus/inert behavior is also verified in the real browser.
    HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
    HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); this.dispatchEvent(new Event('close')); };
    TestBed.inject(SessionService).signedIn({ id: 'route-account', displayName: 'An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' });
  });

  afterEach(() => {
    const http = TestBed.inject(HttpTestingController);
    for (const request of http.match(request => request.url === '/api/problems')) {
      if (!request.cancelled) request.flush({ items: [], page: Number(request.request.params.get('page')), pageSize: Number(request.request.params.get('pageSize')), total: 0 });
    }
    http.verify();
  });

  async function renderAuth(harness: RouterTestingHarness) {
    for (const block of await harness.fixture.getDeferBlocks()) await block.render(DeferBlockState.Complete);
    harness.detectChanges(); await harness.fixture.whenStable(); harness.detectChanges();
  }

  it('redirects the home route into the lazy Problems feature inside the shell', async () => {
    const harness = await RouterTestingHarness.create('/');

    expect(TestBed.inject(Router).url).toBe('/problems');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Problems');
    expect(harness.routeNativeElement?.querySelector('nav[aria-label="Primary navigation"]')).not.toBeNull();
    expect(TestBed.inject(Title).getTitle()).toBe('Problems — Minos');
  });

  it('navigates between feature boundaries while keeping the same shell', async () => {
    const harness = await RouterTestingHarness.create('/problems');
    const shell = harness.routeNativeElement;
    await harness.navigateByUrl('/account');

    expect(harness.routeNativeElement).toBe(shell);
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Settings');
    expect(harness.routeNativeElement?.querySelector('a.active')?.textContent?.trim()).toBe('Profile');
    expect(harness.routeNativeElement?.querySelector('a.active')?.getAttribute('aria-current')).toBe('page');
    expect(TestBed.inject(Title).getTitle()).toBe('Settings — Minos');
    expect(harness.routeNativeElement?.querySelector('.skip-link')?.getAttribute('href')).toBe('/settings/profile#main-content');
  });

  it('reaches Settings through the keyboard account menu and deep-links active local sections', async () => {
    const harness = await RouterTestingHarness.create('/problems'), shell = harness.routeNativeElement!;
    const trigger = shell.querySelector<HTMLButtonElement>('.account-menu button')!;
    trigger.focus(); trigger.click(); harness.detectChanges(); await harness.fixture.whenStable();
    const settingsLink = shell.querySelector<HTMLAnchorElement>('.account-utilities a')!;
    expect(trigger.getAttribute('aria-expanded')).toBe('true'); expect(document.activeElement).toBe(settingsLink);
    settingsLink.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })); harness.detectChanges();
    expect(trigger.getAttribute('aria-expanded')).toBe('false'); expect(document.activeElement).toBe(trigger);
    trigger.click(); harness.detectChanges(); shell.querySelector<HTMLAnchorElement>('.account-utilities a')!.click();
    await harness.fixture.whenStable(); harness.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/settings/profile'); expect(shell.querySelector('.account-utilities')).toBeNull();
    for (const section of ['security', 'account', 'profile']) {
      await harness.navigateByUrl('/settings/' + section);
      expect(shell.querySelector('.settings-navigation a.active')?.getAttribute('href')).toBe('/settings/' + section);
      expect(shell.querySelector('.settings-navigation a.active')?.getAttribute('aria-current')).toBe('page');
      expect(shell.querySelector('form')).toBeNull(); expect(shell.querySelector('h1')?.textContent).toBe('Settings');
    }
  });

  it('discards contextual credentials on local Settings navigation', async () => {
    TestBed.inject(SessionService).signedIn({ id: 'route-account', displayName: 'An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' }, true);
    const harness = await RouterTestingHarness.create('/settings/security'), shell = harness.routeNativeElement!;
    shell.querySelector<HTMLButtonElement>('.setting-row button')!.click(); harness.detectChanges();
    const input = shell.querySelector<HTMLInputElement>('#change-current')!;
    input.value = 'test-only proof'; input.dispatchEvent(new Event('input')); harness.detectChanges();
    await harness.navigateByUrl('/settings/profile'); expect(shell.querySelector('#change-current')).toBeNull();
    await harness.navigateByUrl('/settings/security'); shell.querySelector<HTMLButtonElement>('.setting-row button')!.click(); harness.detectChanges();
    expect(shell.querySelector<HTMLInputElement>('#change-current')!.value).toBe('');
    expect(shell.querySelector('.field-error')).toBeNull();
  });

  it.each(['/unknown', '/account/unknown', '/problems/unknown/path'])(
    'preserves the unknown URL and shows a useful not-found page: %s',
    async (url) => {
      const harness = await RouterTestingHarness.create(url);

      expect(TestBed.inject(Router).url).toBe(url);
      expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe("This page isn't here.");
      expect(harness.routeNativeElement?.querySelector('main a')?.getAttribute('href')).toBe('/problems');
      expect(TestBed.inject(Title).getTitle()).toBe('Page not found — Minos');
    },
  );

  it('renders only existing workspace destinations, not unsupported product features', async () => {
    const harness = await RouterTestingHarness.create('/problems');
    const links = harness.routeNativeElement?.querySelectorAll('.desktop-navigation a');
    expect(Array.from(links ?? []).map((link) => link.getAttribute('href'))).toEqual(['/problems', '/contests', '/submissions']);
    expect(harness.routeNativeElement?.querySelector('input[type="search"]')).toBeNull();
    expect(harness.routeNativeElement?.querySelector('.sidebar, .workspace-footer, .practice-notes')).toBeNull();
  });

  it('uses the supplied theme-specific mark in both desktop navigation and the mobile drawer', async () => {
    const harness = await RouterTestingHarness.create('/problems');
    const theme = TestBed.inject(ThemeService);
    const originalTheme = theme.theme();
    const checkMarks = () => {
      harness.detectChanges();
      const marks = harness.routeNativeElement!.querySelectorAll('.brand-mark image');
      expect(marks).toHaveLength(2);
      for (const mark of Array.from(marks)) {
        expect(mark.getAttribute('href')).toBe(theme.theme() === 'dark' ? 'brand/light-minos.png' : 'brand/dark-minos.png');
      }
    };
    try {
      checkMarks();
      theme.toggle();
      checkMarks();
    } finally {
      if (theme.theme() !== originalTheme) theme.toggle();
      localStorage.removeItem('minos.theme');
    }
  });

  it('keeps the practice landscape decorative behind the authentication popup', async () => {
    const harness = await RouterTestingHarness.create('/problems');
    const checkLandscape = () => {
      const landscape = harness.routeNativeElement!.querySelector('minos-forest-landscape')!;
      expect(landscape.getAttribute('aria-hidden')).toBe('true');
      expect(landscape.querySelector('svg')?.getAttribute('focusable')).toBe('false');
      expect(landscape.querySelectorAll('.forest path').length).toBeGreaterThan(20);
      expect(landscape.querySelectorAll('a, button, [tabindex]')).toHaveLength(0);
    };
    checkLandscape();
    TestBed.inject(SessionService).signedOut();
    await harness.navigateByUrl('/account/login');
    await renderAuth(harness);
    checkLandscape();
  });

  it('opens and dismisses the drawer, and closes it when navigating to another page', async () => {
    const harness = await RouterTestingHarness.create('/problems');
    const element = harness.routeNativeElement!;
    const drawer = element.querySelector('dialog')!;
    // jsdom has no native modal implementation; browser verification covers focus trapping/Escape.
    drawer.showModal = () => drawer.setAttribute('open', '');
    drawer.close = () => {
      drawer.removeAttribute('open');
      drawer.dispatchEvent(new Event('close'));
    };
    const trigger = element.querySelector<HTMLButtonElement>('.menu-button')!;
    trigger.click();
    harness.detectChanges();
    expect(drawer.open).toBe(true);
    expect(trigger.getAttribute('aria-expanded')).toBe('true');

    const firstControl = drawer.querySelector<HTMLAnchorElement>('.brand')!;
    const lastControl = drawer.querySelector<HTMLAnchorElement>('.primary-navigation a[href="/settings"]')!;
    firstControl.focus();
    firstControl.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(lastControl);
    lastControl.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(firstControl);

    element.querySelector<HTMLButtonElement>('.drawer-close')!.click();
    harness.detectChanges();
    expect(drawer.open).toBe(false);
    expect(trigger.getAttribute('aria-expanded')).toBe('false');

    trigger.click();
    await harness.navigateByUrl('/account');
    expect(drawer.open).toBe(false);
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
  });

  it('redirects guests into sign-in while preserving the protected destination', async () => {
    TestBed.inject(SessionService).signedOut();
    const harness = await RouterTestingHarness.create('/account');
    await renderAuth(harness);
    expect(TestBed.inject(Router).url).toBe('/problems?auth=login&authReturn=%2Faccount');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Problems');
    expect(harness.routeNativeElement?.querySelector('dialog.auth-dialog[open] #auth-title')?.textContent).toBe('Sign in');
    expect(TestBed.inject(Title).getTitle()).toBe('Problems — Minos');
  });

  it('allows guest registration deep links without a protected-route loop', async () => {
    TestBed.inject(SessionService).signedOut();
    const harness = await RouterTestingHarness.create('/account/register');
    await renderAuth(harness);
    expect(harness.routeNativeElement?.querySelector('#auth-title')?.textContent).toBe('Create account');
    expect(harness.routeNativeElement?.querySelector('input[autocomplete="new-password"]')).not.toBeNull();
  });

  it('keeps an unavailable account service distinguishable from a signed-out session', async () => {
    TestBed.inject(SessionService).refresh().subscribe();
    TestBed.inject(HttpTestingController).expectOne('/accounts/me').flush({}, { status: 503, statusText: 'Unavailable' });
    const harness = await RouterTestingHarness.create('/account');
    expect(TestBed.inject(Router).url).toBe('/settings/profile');
    expect(harness.routeNativeElement?.querySelector('[role="alert"]')?.textContent).toContain('does not mean you have signed out');
    TestBed.inject(HttpTestingController).verify();
  });

  it('preserves session-expiration feedback on protected navigation', async () => {
    TestBed.inject(SessionService).signedOut('expired');
    const harness = await RouterTestingHarness.create('/account');
    await renderAuth(harness);
    expect(TestBed.inject(Router).url).toBe('/problems?auth=login&authReturn=%2Faccount&authNotice=expired');
    expect(harness.routeNativeElement?.querySelector('.notice')?.textContent).toContain('session has ended');
  });

  it.each([
    ['password-changed', 'Sign in with your new password'],
    ['account-disabled', 'That account can no longer sign in'],
  ])('carries safe %s completion feedback into the sign-in popup', async (notice, message) => {
    TestBed.inject(SessionService).signedOut();
    const harness = await RouterTestingHarness.create('/account/login?notice=' + notice + '&returnUrl=%2Faccount');
    await renderAuth(harness);
    expect(TestBed.inject(Router).url).toBe('/problems?auth=login&authReturn=%2Faccount&authNotice=' + notice);
    expect(harness.routeNativeElement?.querySelector('.notice')?.textContent).toContain(message);
  });

  it('does not present arbitrary URL content as an account completion notice', async () => {
    TestBed.inject(SessionService).signedOut();
    const harness = await RouterTestingHarness.create('/account/login?notice=unsupported');
    await renderAuth(harness); expect(harness.routeNativeElement?.querySelector('.notice')).toBeNull();
  });
});

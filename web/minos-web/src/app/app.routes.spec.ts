import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Title } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { routes } from './app.routes';
import { SessionService } from './core/auth/session.service';
import { ThemeService } from './core/theme/theme.service';

describe('application routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).signedIn({ id: 'route-account', displayName: 'An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' });
  });

  it('redirects the home route into the lazy Problems feature inside the shell', async () => {
    const harness = await RouterTestingHarness.create('/');

    expect(TestBed.inject(Router).url).toBe('/problems');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Solve what matters.');
    expect(harness.routeNativeElement?.querySelector('nav[aria-label="Primary navigation"]')).not.toBeNull();
    expect(TestBed.inject(Title).getTitle()).toBe('Problems — Minos');
  });

  it('navigates between feature boundaries while keeping the same shell', async () => {
    const harness = await RouterTestingHarness.create('/problems');
    const shell = harness.routeNativeElement;
    await harness.navigateByUrl('/account');

    expect(harness.routeNativeElement).toBe(shell);
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Your work, kept together.');
    expect(harness.routeNativeElement?.querySelector('a.active')?.textContent?.trim()).toBe('Account');
    expect(harness.routeNativeElement?.querySelector('a.active')?.getAttribute('aria-current')).toBe('page');
    expect(TestBed.inject(Title).getTitle()).toBe('Account — Minos');
    expect(harness.routeNativeElement?.querySelector('.skip-link')?.getAttribute('href')).toBe('/account#main-content');
  });

  it.each(['/unknown', '/account/unknown', '/problems/unknown'])(
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
    const links = harness.routeNativeElement?.querySelectorAll('.sidebar .primary-navigation a');
    expect(Array.from(links ?? []).map((link) => link.getAttribute('href'))).toEqual(['/problems', '/account']);
    expect(harness.routeNativeElement?.querySelector('input[type="search"]')).toBeNull();
    expect(harness.routeNativeElement?.querySelector('.banner-actions a')?.getAttribute('href')).toBe('/problems#problem-library');
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

  it('keeps the shared landscape decorative on both practice and sign-in brand surfaces', async () => {
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

    const firstControl = drawer.querySelector<HTMLButtonElement>('.drawer-close')!;
    const lastControl = drawer.querySelector<HTMLAnchorElement>('.primary-navigation a[href="/account"]')!;
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
    expect(TestBed.inject(Router).url).toBe('/account/login?returnUrl=%2Faccount');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Welcome back.');
    expect(TestBed.inject(Title).getTitle()).toBe('Sign in — Minos');
  });

  it('allows guest registration deep links without a protected-route loop', async () => {
    TestBed.inject(SessionService).signedOut();
    const harness = await RouterTestingHarness.create('/account/register');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Make room for progress.');
    expect(harness.routeNativeElement?.querySelector('input[autocomplete="new-password"]')).not.toBeNull();
  });

  it('keeps an unavailable account service distinguishable from a signed-out session', async () => {
    TestBed.inject(SessionService).refresh().subscribe();
    TestBed.inject(HttpTestingController).expectOne('/accounts/me').flush({}, { status: 503, statusText: 'Unavailable' });
    const harness = await RouterTestingHarness.create('/account');
    expect(TestBed.inject(Router).url).toBe('/account');
    expect(harness.routeNativeElement?.querySelector('[role="alert"]')?.textContent).toContain('does not mean you have signed out');
    TestBed.inject(HttpTestingController).verify();
  });

  it('preserves session-expiration feedback on protected navigation', async () => {
    TestBed.inject(SessionService).signedOut('expired');
    const harness = await RouterTestingHarness.create('/account');
    expect(TestBed.inject(Router).url).toBe('/account/login?returnUrl=%2Faccount&expired=1');
    expect(harness.routeNativeElement?.querySelector('.notice')?.textContent).toContain('session has ended');
  });
});

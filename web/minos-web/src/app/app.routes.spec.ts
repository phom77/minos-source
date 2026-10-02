import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Title } from '@angular/platform-browser';

import { routes } from './app.routes';

describe('application routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes)] });
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
    expect(harness.routeNativeElement?.querySelector('a.active')?.textContent).toBe('Account');
    expect(TestBed.inject(Title).getTitle()).toBe('Account — Minos');
  });

  it.each(['/unknown', '/account/unknown', '/problems/unknown'])(
    'preserves the catalog fallback for an unknown URL: %s',
    async (url) => {
      const harness = await RouterTestingHarness.create(url);

      expect(TestBed.inject(Router).url).toBe('/problems');
      expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Solve what matters.');
    },
  );
});

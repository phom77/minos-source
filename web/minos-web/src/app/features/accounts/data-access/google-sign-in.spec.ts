import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../../core/auth/session.service';
import { API_CONFIGURATION } from '../../../core/config/api.config';
import { GoogleSignIn } from './google-sign-in';

const account = { id: 'google-account', displayName: 'An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' };
describe('server-owned Google sign-in', () => {
  let http: HttpTestingController;
  function configure(origin = 'https://minos.test', apiBaseUrl = '') {
    TestBed.configureTestingModule({ providers: [GoogleSignIn, provideHttpClient(), provideHttpClientTesting(),
      { provide: DOCUMENT, useValue: { location: new URL(origin), visibilityState: 'visible' } },
      { provide: API_CONFIGURATION, useValue: { apiBaseUrl, production: false } },
    ] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).signedOut();
    return TestBed.inject(GoogleSignIn);
  }
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => { http.verify({ ignoreCancelled: true }); vi.useRealTimers(); });

  it('confirms a cookie session without reading OAuth tokens or changing server callbacks', () => {
    const google = configure(); const completed = vi.fn(); google.completed.subscribe(completed);
    const event = new Event('click', { cancelable: true }); google.start(event);
    expect(event.defaultPrevented).toBe(false); expect(google.loginUrl).toBe('/accounts/google/login');
    vi.advanceTimersByTime(1500);
    const request = http.expectOne('/accounts/me'); expect(request.request.method).toBe('GET');
    request.flush(account);
    expect(TestBed.inject(SessionService).account()?.id).toBe('google-account');
    expect(completed).toHaveBeenCalledOnce(); expect(google.waiting()).toBe(false);
    vi.advanceTimersByTime(10000); http.expectNone('/accounts/me');
  });
  it('treats guest checks as pending and allows manual rechecking without overlapping requests', () => {
    const google = configure(); google.start(new Event('click')); vi.advanceTimersByTime(1500);
    http.expectOne('/accounts/me').flush({}, { status: 401, statusText: 'Guest' });
    expect(google.waiting()).toBe(true); expect(google.error()).toBeNull();
    google.check(true); google.check(true);
    http.expectOne('/accounts/me').flush({}, { status: 401, statusText: 'Guest' });
    expect(google.error()).toContain('Not signed in yet');
    expect(TestBed.inject(SessionService).state().status).toBe('guest'); google.stop();
  });
  it.each([{}, { ...account, role: 'Root' }])('rejects an invalid identity response', response => {
    const google = configure(); google.start(new Event('click')); google.check();
    http.expectOne('/accounts/me').flush(response);
    expect(google.error()).toContain('could not confirm'); expect(google.waiting()).toBe(false);
    expect(TestBed.inject(SessionService).state().status).toBe('guest');
  });
  it('stops polling on an outage, cancellation, timeout or destruction', () => {
    const google = configure(); google.start(new Event('click')); google.check();
    http.expectOne('/accounts/me').flush({ detail: 'secret' }, { status: 503, statusText: 'Unavailable' });
    expect(google.error()).toContain('could not check'); expect(google.error()).not.toContain('secret');
    google.start(new Event('click')); google.check(); const request = http.expectOne('/accounts/me');
    google.stop(); expect(request.cancelled).toBe(true);
    Object.defineProperty(TestBed.inject(DOCUMENT), 'visibilityState', { value: 'hidden' });
    google.start(new Event('click')); vi.advanceTimersByTime(300_000);
    for (const pending of http.match('/accounts/me')) expect(pending.cancelled).toBe(true);
    expect(google.error()).toContain('not confirmed'); expect(google.waiting()).toBe(false);
    google.start(new Event('click')); TestBed.resetTestingModule(); vi.advanceTimersByTime(10000);
    expect(google.waiting()).toBe(false);
  });
  it.each([['http://minos.test', ''], ['https://minos.test', 'https://other.test']])('blocks an unsupported cookie topology (%s, %s)', (origin, apiBaseUrl) => {
    const google = configure(origin, apiBaseUrl); const event = new Event('click', { cancelable: true }); google.start(event);
    expect(event.defaultPrevented).toBe(true); expect(google.error()).toContain('HTTPS app'); expect(google.waiting()).toBe(false);
    http.expectNone('/accounts/me');
  });
});

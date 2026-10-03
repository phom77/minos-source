import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SessionService } from '../auth/session.service';
import { API_CONFIGURATION } from '../config/api.config';
import { apiUrlInterceptor, MINOS_API_REQUEST } from './api-url.interceptor';
import { csrfInterceptor } from './csrf.interceptor';
import { CsrfService, ProtectionChangedError } from './csrf.service';
import { sessionErrorInterceptor, SESSION_REQUIRED } from './session-error.interceptor';

describe('Minos authentication transport', () => {
  let http: HttpClient;
  let requests: HttpTestingController;
  const marked = () => new HttpContext().set(MINOS_API_REQUEST, true);
  function configure(base = ''): void {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([csrfInterceptor, sessionErrorInterceptor, apiUrlInterceptor])),
      provideHttpClientTesting(), { provide: API_CONFIGURATION, useValue: { apiBaseUrl: base, production: false } },
    ] });
    http = TestBed.inject(HttpClient);
    requests = TestBed.inject(HttpTestingController);
  }
  afterEach(() => requests.verify());

  it('protects all Minos mutations and coalesces token requests without changing their bodies', () => {
    configure();
    http.post('/accounts/login', { email: 'an@example.test', password: 'test-only-passphrase' }, { context: marked() }).subscribe();
    http.post('/api/problems/1/submissions', { source: 'test source' }, { context: marked() }).subscribe();
    requests.expectNone('/accounts/login');
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'test-token' });
    const login = requests.expectOne('/accounts/login');
    const submission = requests.expectOne('/api/problems/1/submissions');
    expect(login.request.headers.get('X-CSRF-TOKEN')).toBe('test-token');
    expect(submission.request.headers.get('X-CSRF-TOKEN')).toBe('test-token');
    expect(submission.request.body).toEqual({ source: 'test source' });
    expect(login.request.headers.has('Authorization')).toBe(false);
    expect(login.request.withCredentials).toBe(false); // Same-origin cookies remain browser-owned.
    login.flush({}); submission.flush({});
  });

  it.each(['GET', 'HEAD', 'OPTIONS'])('does not fetch protection for a safe %s request', (method) => {
    configure();
    http.request(method, '/api/problems', { context: marked() }).subscribe();
    const request = requests.expectOne('/api/problems');
    expect(request.request.headers.has('X-CSRF-TOKEN')).toBe(false);
    request.flush({});
    requests.expectNone('/accounts/csrf');
  });

  it.each(['/assets/data.json', 'https://other.test/api/data', '//other.test/api/data', 'api/data', '/accounts\\other', '/accounts-extra/data'])(
    'does not send a token to unrelated or malformed paths: %s', (url) => {
      configure();
      http.post(url, {}, { context: marked() }).subscribe();
      const request = requests.expectOne(url);
      expect(request.request.headers.has('X-CSRF-TOKEN')).toBe(false);
      request.flush({}); requests.expectNone('/accounts/csrf');
    },
  );

  it('does not modify unmarked requests or leak tokens to a configured foreign origin', () => {
    configure('https://other.test');
    http.post('/accounts/login', {}).subscribe();
    requests.expectOne('/accounts/login').flush({});
    http.post('/accounts/login', {}, { context: marked() }).subscribe();
    const external = requests.expectOne('https://other.test/accounts/login');
    expect(external.request.headers.has('X-CSRF-TOKEN')).toBe(false);
    external.flush({}); requests.expectNone('/accounts/csrf');
  });

  it('does not send or blindly retry a mutation when protection fails', () => {
    configure();
    const failed = vi.fn();
    http.post('/accounts/register', {}, { context: marked() }).subscribe({ error: failed });
    requests.expectOne('/accounts/csrf').flush({}, { status: 503, statusText: 'Unavailable' });
    expect(failed).toHaveBeenCalledOnce(); requests.expectNone('/accounts/register');
  });

  it('refreshes rejected protection on the next manual attempt, without replaying a POST', () => {
    configure();
    http.post('/accounts/login', {}, { context: marked() }).subscribe({ error: () => {} });
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'stale-token' });
    requests.expectOne('/accounts/login').flush({}, { status: 400, statusText: 'Invalid request' });
    requests.expectNone('/accounts/login'); requests.expectNone('/accounts/csrf');
    http.post('/accounts/login', {}, { context: marked() }).subscribe();
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'fresh-token' });
    const login = requests.expectOne('/accounts/login');
    expect(login.request.headers.get('X-CSRF-TOKEN')).toBe('fresh-token'); login.flush({});
  });

  it('rejects stale in-flight protection after an identity change, then fetches a fresh token', () => {
    configure();
    const csrf = TestBed.inject(CsrfService);
    const failed = vi.fn();
    http.post('/api/problems', {}, { context: marked() }).subscribe({ error: failed });
    const old = requests.expectOne('/accounts/csrf');
    csrf.invalidate();
    old.flush({ requestToken: 'old-token' });
    expect(failed.mock.calls[0][0]).toBeInstanceOf(ProtectionChangedError);
    requests.expectNone('/api/problems');
    http.patch('/api/problems/1', {}, { context: marked() }).subscribe();
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'new-token' });
    const mutation = requests.expectOne('/api/problems/1');
    expect(mutation.request.headers.get('X-CSRF-TOKEN')).toBe('new-token'); mutation.flush({});
  });

  it('expires a protected session on 401 but does not treat incorrect login credentials as expiration', () => {
    configure();
    const session = TestBed.inject(SessionService);
    session.signedIn({ id: 'one', displayName: 'An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' });
    http.post('/accounts/login', {}, { context: marked() }).subscribe({ error: () => {} });
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'token' });
    requests.expectOne('/accounts/login').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.account()?.id).toBe('one');
    http.get('/api/me/submissions', { context: marked().set(SESSION_REQUIRED, true) }).subscribe({ error: () => {} });
    requests.expectOne('/api/me/submissions').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.state()).toEqual({ status: 'guest', reason: 'expired' });
  });

  it('does not expire a newer identity when an old protected request fails', () => {
    configure();
    const session = TestBed.inject(SessionService);
    const account = { id: 'one', displayName: 'An', role: 'Solver' as const, createdAtUtc: '2026-10-01T00:00:00Z' };
    session.signedIn(account);
    http.get('/api/me/submissions', { context: marked().set(SESSION_REQUIRED, true) }).subscribe({ error: () => {} });
    const old = requests.expectOne('/api/me/submissions');
    session.signedIn({ ...account, id: 'two' });
    old.flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.account()?.id).toBe('two');
  });

  it('keeps an authenticated session after a protected 403 or server failure', () => {
    configure();
    const session = TestBed.inject(SessionService);
    session.signedIn({ id: 'one', displayName: 'An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' });
    for (const status of [403, 500]) {
      http.get('/api/me/submissions', { context: marked().set(SESSION_REQUIRED, true) }).subscribe({ error: () => {} });
      requests.expectOne('/api/me/submissions').flush({}, { status, statusText: 'Failed' });
      expect(session.account()?.id).toBe('one');
    }
  });
});

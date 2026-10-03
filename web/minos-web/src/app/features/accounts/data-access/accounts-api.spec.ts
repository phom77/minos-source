import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Observable } from 'rxjs';
import { CurrentAccount } from '../../../core/auth/session.models';
import { SessionService } from '../../../core/auth/session.service';
import { API_CONFIGURATION } from '../../../core/config/api.config';
import { csrfInterceptor } from '../../../core/http/csrf.interceptor';
import { sessionErrorInterceptor } from '../../../core/http/session-error.interceptor';
import { AccountsApi } from './accounts-api';

const account: CurrentAccount = { id: 'one', displayName: 'Nguyễn An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' };
describe('account settings API', () => {
  let api: AccountsApi; let requests: HttpTestingController; let session: SessionService;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([csrfInterceptor, sessionErrorInterceptor])),
      provideHttpClientTesting(), { provide: API_CONFIGURATION, useValue: { apiBaseUrl: '', production: false } }] });
    api = TestBed.inject(AccountsApi); requests = TestBed.inject(HttpTestingController); session = TestBed.inject(SessionService);
    session.signedIn(account);
  });
  afterEach(() => requests.verify());
  function protection() { requests.expectOne('/accounts/csrf').flush({ requestToken: 'fixture-protection' }); }

  it('saves only the display name, validates the response and updates shared identity', () => {
    api.rename('Linh').subscribe(); protection();
    const request = requests.expectOne('/accounts/me');
    expect(request.request.method).toBe('PATCH'); expect(request.request.body).toEqual({ displayName: 'Linh' });
    expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('fixture-protection');
    request.flush({ ...account, displayName: 'Linh' }); expect(session.account()?.displayName).toBe('Linh');
    api.disable('fixture passphrase').subscribe(); protection(); // Profile refresh invalidates protection safely.
    requests.expectOne('/accounts/disable').flush(null); expect(session.account()).toBeNull();
  });

  it.each([{ ...account, id: 'different' }, { ...account, role: 'Admin' }, { ...account, displayName: '' }, {}])(
    'rejects malformed or mismatched profile responses', (response) => {
      const failed = vi.fn(); api.rename('Linh').subscribe({ error: failed }); protection();
      requests.expectOne('/accounts/me').flush(response); expect(failed).toHaveBeenCalledOnce(); expect(session.account()).toEqual(account);
    },
  );

  it.each(['password', 'disable'] as const)('sends the exact protected %s contract and clears only on success', (operation) => {
    const request = operation === 'password' ? api.changePassword({ currentPassword: ' current fixture ', newPassword: ' new fixture pass ' })
      : api.disable(' current fixture ');
    request.subscribe(); protection();
    const mutation = requests.expectOne('/accounts/' + operation);
    expect(mutation.request.method).toBe('POST'); expect(mutation.request.headers.get('X-CSRF-TOKEN')).toBe('fixture-protection');
    expect(mutation.request.body).toEqual(operation === 'password'
      ? { currentPassword: ' current fixture ', newPassword: ' new fixture pass ' } : { password: ' current fixture ' });
    expect(session.account()).not.toBeNull(); mutation.flush(null); expect(session.state()).toEqual({ status: 'guest', reason: 'signed-out' });
  });

  it.each(['password', 'disable'] as const)('does not mistake wrong %s proof for session expiration', (operation) => {
    const failed = vi.fn();
    (operation === 'password' ? api.changePassword({ currentPassword: 'incorrect fixture', newPassword: 'new fixture passphrase' }) : api.disable('incorrect fixture'))
      .subscribe({ error: failed }); protection();
    requests.expectOne('/accounts/' + operation).flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.state().status).toBe('authenticated'); expect(failed).not.toHaveBeenCalled();
    requests.expectOne('/accounts/me').flush(account); expect(session.account()).toEqual(account); expect(failed).toHaveBeenCalledOnce();
  });

  it.each([401, 503, 0])('checks ambiguous security 401 without converting an outage (%s) into logout', (status) => {
    api.disable('incorrect fixture').subscribe({ error: () => {} }); protection();
    requests.expectOne('/accounts/disable').flush({}, { status: 401, statusText: 'Unauthorized' });
    const check = requests.expectOne('/accounts/me');
    if (status === 0) check.error(new ProgressEvent('error')); else check.flush({}, { status, statusText: 'Failed' });
    expect(session.state().status).toBe(status === 401 ? 'guest' : 'authenticated');
    if (status === 401) expect(session.state()).toEqual({ status: 'guest', reason: 'expired' });
  });

  it('does not overwrite or clear newer identities after stale mutations or verification failures', () => {
    api.rename('Old name').subscribe(); protection(); const rename = requests.expectOne('/accounts/me');
    session.signedIn({ ...account, id: 'two' }); rename.flush({ ...account, displayName: 'Old name' });
    expect(session.account()?.id).toBe('two');
    api.disable('fixture passphrase').subscribe({ error: () => {} }); protection();
    requests.expectOne('/accounts/disable').flush({}, { status: 401, statusText: 'Unauthorized' }); const check = requests.expectOne('/accounts/me');
    session.signedIn({ ...account, id: 'three' }); check.flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.account()?.id).toBe('three');
    api.changePassword({ currentPassword: 'fixture passphrase', newPassword: 'new fixture passphrase' }).subscribe(); protection();
    const password = requests.expectOne('/accounts/password'); session.signedIn({ ...account, id: 'four' }); password.flush(null);
    expect(session.account()?.id).toBe('four');
    api.signOut().subscribe(); protection(); const logout = requests.expectOne('/accounts/logout');
    session.signedIn({ ...account, id: 'five' }); logout.flush(null); expect(session.account()?.id).toBe('five');
  });

  it('updates the session if verification discovers a different account in another tab', () => {
    api.disable('fixture passphrase').subscribe({ error: () => {} }); protection();
    requests.expectOne('/accounts/disable').flush({}, { status: 401, statusText: 'Unauthorized' });
    requests.expectOne('/accounts/me').flush({ ...account, id: 'two' }); expect(session.account()?.id).toBe('two');
  });

  it.each([400, 403, 409, 429, 500])('never replays an ambiguous security mutation (%s)', (status) => {
    api.changePassword({ currentPassword: 'fixture passphrase', newPassword: 'new fixture passphrase' }).subscribe({ error: () => {} }); protection();
    requests.expectOne('/accounts/password').flush({}, { status, statusText: 'Failed' });
    requests.expectNone('/accounts/password'); requests.expectNone('/accounts/me'); expect(session.account()).toEqual(account);
  });

  it('refuses account mutations without a confirmed session', () => {
    session.signedOut();
    const mutations: Observable<unknown>[] = [api.rename('Linh'), api.disable('fixture passphrase'), api.changePassword({ currentPassword: 'fixture passphrase', newPassword: 'replacement fixture' }), api.signOut()];
    for (const mutation of mutations) {
      const error = vi.fn(); mutation.subscribe({ error }); expect(error).toHaveBeenCalledOnce();
    }
    requests.expectNone('/accounts/csrf');
  });

  it('finishes an already sent security operation after its consumer leaves', () => {
    const subscription = api.disable('fixture passphrase').subscribe(); protection(); const mutation = requests.expectOne('/accounts/disable');
    subscription.unsubscribe(); expect(mutation.cancelled).toBe(false); mutation.flush(null); expect(session.account()).toBeNull();
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { CsrfService } from '../http/csrf.service';
import { CurrentAccount } from './session.models';
import { SessionService } from './session.service';

const account: CurrentAccount = { id: 'account-1', displayName: 'Nguyễn An', role: 'Solver', createdAtUtc: '2026-10-01T12:00:00Z' };

describe('SessionService', () => {
  let session: SessionService;
  let requests: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    session = TestBed.inject(SessionService);
    requests = TestBed.inject(HttpTestingController);
  });
  afterEach(() => requests.verify());

  it('does not infer password capability from a restored account or a provider sign-in', () => {
    session.ensure().subscribe(); requests.expectOne('/accounts/me').flush(account);
    expect(session.passwordConfirmed()).toBe(false);
    session.signedIn(account); expect(session.passwordConfirmed()).toBe(false);
  });

  it('preserves local password evidence across matching profile/refresh responses and clears it on identity changes or logout', () => {
    session.signedIn(account, true); expect(session.passwordConfirmed()).toBe(true);
    session.signedIn({ ...account, displayName: 'Linh' }); expect(session.passwordConfirmed()).toBe(true);
    session.refresh().subscribe(); requests.expectOne('/accounts/me').flush(account); expect(session.passwordConfirmed()).toBe(true);
    session.signedIn({ ...account, id: 'another-account' }); expect(session.passwordConfirmed()).toBe(false);
    session.signedIn(account, true); session.signedOut(); expect(session.passwordConfirmed()).toBe(false);
    session.signedIn(account); expect(session.passwordConfirmed()).toBe(false);
  });

  it('coalesces initial checks and keeps the account only in memory', () => {
    expect(session.state().status).toBe('checking');
    session.ensure().subscribe();
    session.ensure().subscribe();
    requests.expectOne('/accounts/me').flush(account);
    expect(session.account()).toEqual(account);
    session.ensure().subscribe();
    requests.expectNone('/accounts/me');
  });

  it('recognizes 401 as a guest session', () => {
    session.ensure().subscribe();
    requests.expectOne('/accounts/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.state()).toEqual({ status: 'guest', reason: 'signed-out' });
  });

  it.each([0, 400, 403, 500, 503])('does not turn status %s into logout; retry can recover', (status) => {
    session.ensure().subscribe();
    const request = requests.expectOne('/accounts/me');
    if (status === 0) request.error(new ProgressEvent('error'));
    else request.flush({}, { status, statusText: 'Failed' });
    expect(session.state().status).toBe('unavailable');
    session.refresh().subscribe();
    requests.expectOne('/accounts/me').flush(account);
    expect(session.account()).toEqual(account);
  });

  it('treats a malformed success payload as unavailable, not an authenticated identity', () => {
    session.ensure().subscribe();
    requests.expectOne('/accounts/me').flush({ id: 'invalid' });
    expect(session.state().status).toBe('unavailable');
  });

  it('ignores an old guest response after sign-in', () => {
    session.ensure().subscribe();
    const old = requests.expectOne('/accounts/me');
    session.signedIn(account);
    old.flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.account()).toEqual(account);
  });

  it('ignores an old account response after logout', () => {
    session.ensure().subscribe();
    const old = requests.expectOne('/accounts/me');
    session.signedOut();
    old.flush(account);
    expect(session.account()).toBeNull();
    expect(session.state().status).toBe('guest');
  });

  it('clears session protection after identity changes and detects expiration', () => {
    const invalidate = vi.spyOn(TestBed.inject(CsrfService), 'invalidate');
    session.signedIn(account);
    expect(invalidate).toHaveBeenCalledOnce();
    session.refresh().subscribe();
    requests.expectOne('/accounts/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.state()).toEqual({ status: 'guest', reason: 'expired' });
    expect(invalidate).toHaveBeenCalledTimes(2);
  });
});

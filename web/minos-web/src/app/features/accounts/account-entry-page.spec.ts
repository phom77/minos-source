import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { SessionService } from '../../core/auth/session.service';
import { API_CONFIGURATION } from '../../core/config/api.config';
import { csrfInterceptor } from '../../core/http/csrf.interceptor';
import { sessionErrorInterceptor } from '../../core/http/session-error.interceptor';
import { AccountEntryPage } from './account-entry-page';

describe('account session overview', () => {
  let requests: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]),
      provideHttpClient(withInterceptors([csrfInterceptor, sessionErrorInterceptor])), provideHttpClientTesting(),
      { provide: API_CONFIGURATION, useValue: { apiBaseUrl: '', production: false } },
    ] });
    requests = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });
  afterEach(() => requests.verify());

  it('shows a recoverable session outage rather than presenting it as logout', async () => {
    const session = TestBed.inject(SessionService);
    session.refresh().subscribe();
    requests.expectOne('/accounts/me').flush({}, { status: 503, statusText: 'Unavailable' });
    const fixture = TestBed.createComponent(AccountEntryPage); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('does not mean you have signed out');
    fixture.nativeElement.querySelector('button').click(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Checking your session');
    requests.expectOne('/accounts/me').flush({ id: 'one', displayName: 'An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' });
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Signed in');
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('.account-details > h3'));
  });

  it('does not clear a session on a failed logout; a successful retry clears it', () => {
    const session = TestBed.inject(SessionService);
    session.signedIn({ id: 'one', displayName: 'An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' });
    const fixture = TestBed.createComponent(AccountEntryPage); fixture.detectChanges();
    fixture.componentInstance.signOut(); fixture.componentInstance.signOut();
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'token' });
    requests.expectOne('/accounts/logout').flush({}, { status: 500, statusText: 'Failed' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('your session may still be active');
    expect(session.account()?.id).toBe('one');
    fixture.componentInstance.signOut();
    requests.expectOne('/accounts/logout').flush(null);
    expect(session.account()).toBeNull();
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/account/login'], { queryParams: { signedOut: '1' } });
  });

  it('treats revoked-session logout as expired rather than a generic server outage', () => {
    const session = TestBed.inject(SessionService);
    session.signedIn({ id: 'one', displayName: 'An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' });
    const fixture = TestBed.createComponent(AccountEntryPage); fixture.detectChanges();
    fixture.componentInstance.signOut();
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'token' });
    requests.expectOne('/accounts/logout').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.state().status).toBe('guest');
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/account/login'], { queryParams: { expired: '1', returnUrl: '/account' } });
  });
});

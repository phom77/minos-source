import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';

import { API_CONFIGURATION } from '../../../core/config/api.config';
import { SessionService } from '../../../core/auth/session.service';
import { csrfInterceptor } from '../../../core/http/csrf.interceptor';
import { sessionErrorInterceptor } from '../../../core/http/session-error.interceptor';
import { AuthPage } from './auth-page';

const account = { id: 'test-account', displayName: 'Nguyễn An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' };

describe('account authentication forms', () => {
  let requests: HttpTestingController;
  function configure(mode: 'login' | 'register' = 'login', returnUrl = '/account') {
    TestBed.configureTestingModule({ providers: [
      provideRouter([]), provideHttpClient(withInterceptors([csrfInterceptor, sessionErrorInterceptor])), provideHttpClientTesting(),
      { provide: API_CONFIGURATION, useValue: { apiBaseUrl: '', production: false } },
      { provide: ActivatedRoute, useValue: { snapshot: { data: { mode }, queryParamMap: convertToParamMap({ returnUrl }) } } },
    ] });
    requests = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).signedOut();
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(AuthPage);
    fixture.detectChanges();
    return fixture;
  }
  afterEach(() => requests.verify());

  it('shows field errors and a focused, linked summary without making a request', async () => {
    const fixture = configure('register');
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('.form-errors')).toBeNull();
    element.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(document.activeElement).toBe(element.querySelector('.form-errors'));
    expect(element.querySelectorAll('.form-errors a')).toHaveLength(4);
    expect(element.querySelector('#email')?.getAttribute('aria-invalid')).toBe('true');
    expect(element.querySelector('#email')?.getAttribute('aria-describedby')).toBe('email-error');
    element.querySelector<HTMLAnchorElement>('.form-errors a[href$="#email"]')!.click();
    expect(document.activeElement).toBe(element.querySelector('#email'));
    requests.expectNone('/accounts/csrf');
  });

  it('validates display names, email, password length and confirmation', () => {
    const fixture = configure('register');
    const page = fixture.componentInstance;
    page.form.setValue({ displayName: '\u0001Name', email: 'bad-email', password: 'short', confirmation: 'different' });
    page.submit(); fixture.detectChanges();
    expect(page.fieldError('displayName')).toContain('control characters');
    expect(page.fieldError('email')).toBe('Enter a valid email address.');
    expect(page.fieldError('password')).toBe('Use at least 12 characters.');
    expect(page.fieldError('confirmation')).toBe('Passwords must match.');
    page.form.controls.displayName.setValue('a'.repeat(81));
    page.form.controls.password.setValue('a'.repeat(257));
    expect(page.fieldError('displayName')).toBe('Use no more than 80 characters.');
    expect(page.fieldError('password')).toBe('Use no more than 256 characters.');
    requests.expectNone('/accounts/csrf');
  });

  it('registers with trimmed identity fields, never trims the password, and signs in immediately', () => {
    const fixture = configure('register', '/problems');
    const page = fixture.componentInstance;
    const password = '  test passphrase  ';
    page.form.setValue({ displayName: '  Nguyễn An  ', email: '  an@example.test  ', password, confirmation: password });
    page.submit(); page.submit(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(true);
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'token-guest' });
    const registration = requests.expectOne('/accounts/register');
    expect(registration.request.body).toEqual({ displayName: 'Nguyễn An', email: 'an@example.test', password });
    expect(registration.request.headers.get('X-CSRF-TOKEN')).toBe('token-guest');
    registration.flush(account);
    expect(TestBed.inject(SessionService).account()?.displayName).toBe('Nguyễn An');
    expect(TestBed.inject(Router).navigateByUrl).toHaveBeenCalledWith('/problems');
    expect(page.form.controls.password.value).toBe('');
    TestBed.inject(HttpClient).post('/accounts/logout', null, { context: registration.request.context }).subscribe();
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'token-account' });
    requests.expectOne('/accounts/logout').flush(null);
  });

  it('uses a safe fallback for an external return URL after login', () => {
    const fixture = configure('login', '//other.test');
    const page = fixture.componentInstance;
    page.form.patchValue({ email: 'an@example.test', password: 'test passphrase' });
    page.submit();
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'token' });
    requests.expectOne('/accounts/login').flush(account);
    expect(TestBed.inject(Router).navigateByUrl).toHaveBeenCalledWith('/account');
  });

  it('finishes an already submitted identity change when its page is destroyed', () => {
    const fixture = configure();
    fixture.componentInstance.form.patchValue({ email: 'an@example.test', password: 'test passphrase' });
    fixture.componentInstance.submit();
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'token' });
    const login = requests.expectOne('/accounts/login');
    fixture.destroy();
    expect(login.cancelled).toBe(false);
    login.flush(account);
    expect(TestBed.inject(SessionService).account()?.id).toBe('test-account');
    expect(TestBed.inject(Router).navigateByUrl).not.toHaveBeenCalled();
  });

  it('preserves query and fragment state in the already-signed-in return link', () => {
    const fixture = configure('login', '/problems?page=2#problem-library');
    TestBed.inject(SessionService).signedIn({ ...account, role: 'Solver' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.already-signed-in .button')?.getAttribute('href')).toBe('/problems?page=2#problem-library');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it.each([401, 429, 500])('recovers from a sign-in failure (%s), retains input and allows manual retry', async (status) => {
    const fixture = configure();
    const page = fixture.componentInstance;
    page.form.patchValue({ email: 'an@example.test', password: 'test passphrase' });
    page.submit();
    requests.expectOne('/accounts/csrf').flush({ requestToken: 'token' });
    requests.expectOne('/accounts/login').flush({ detail: 'private backend diagnostics' }, { status, statusText: 'Failed' });
    fixture.detectChanges(); await fixture.whenStable();
    const expected = status === 401 ? 'Check your email and password' : status === 429 ? 'Wait a minute' : 'could not confirm';
    expect(page.serverError()).toContain(expected);
    expect(fixture.nativeElement.textContent).not.toContain('private backend diagnostics');
    expect(page.form.controls.password.value).toBe('test passphrase');
    expect(page.pending()).toBe(false);
    expect(page.form.enabled).toBe(true);
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('.form-errors'));
    expect(TestBed.inject(SessionService).state().status).toBe('guest');
    page.submit();
    requests.expectNone('/accounts/csrf');
    requests.expectOne('/accounts/login').flush(account);
  });

  it('uses explicit labels, unique IDs, password-manager attributes, and a working reveal control', () => {
    const fixture = configure('register');
    const element: HTMLElement = fixture.nativeElement;
    const ids = Array.from(element.querySelectorAll('[id]')).map((node) => node.id);
    expect(new Set(ids).size).toBe(ids.length);
    expect(element.querySelector('#email')?.getAttribute('autocomplete')).toBe('username');
    expect(element.querySelector('#password')?.getAttribute('autocomplete')).toBe('new-password');
    for (const input of Array.from(element.querySelectorAll('input'))) expect(element.querySelector(`label[for="${input.id}"]`)).not.toBeNull();
    element.querySelector<HTMLButtonElement>('.password-toggle')!.click(); fixture.detectChanges();
    expect(element.querySelector('#password')?.getAttribute('type')).toBe('text');
    expect(element.querySelector('#confirmation')?.getAttribute('type')).toBe('text');
    expect(element.querySelector('.password-toggle')?.getAttribute('aria-pressed')).toBe('true');
    expect(element.querySelector('.password-toggle')?.getAttribute('aria-controls')).toBe('password confirmation');
    expect(element.querySelector('.password-toggle')?.getAttribute('aria-label')).toBe('Hide passwords');
  });

  it('uses an accessible eye/eye-off button for sign-in and only necessary password helper text', () => {
    const fixture = configure();
    const element: HTMLElement = fixture.nativeElement;
    const toggle = element.querySelector<HTMLButtonElement>('.password-toggle')!;
    const password = element.querySelector<HTMLInputElement>('#password')!;
    const initialIcon = toggle.querySelector('path')!.getAttribute('d');
    expect(toggle.textContent?.trim()).toBe('');
    expect(toggle.getAttribute('aria-label')).toBe('Show password');
    expect(toggle.getAttribute('aria-controls')).toBe('password');
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
    expect(toggle.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(password.autocomplete).toBe('current-password');
    expect(element.querySelector('#password-hint')?.textContent).toBe('12–256 characters.');
    expect(element.textContent).not.toContain('Password managers and paste are welcome.');
    toggle.click(); fixture.detectChanges();
    expect(password.type).toBe('text');
    expect(toggle.getAttribute('aria-label')).toBe('Hide password');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    expect(toggle.querySelector('path')!.getAttribute('d')).not.toBe(initialIcon);
    toggle.click(); fixture.detectChanges();
    expect(password.type).toBe('password');
    expect(toggle.getAttribute('aria-label')).toBe('Show password');
    expect(toggle.querySelector('path')!.getAttribute('d')).toBe(initialIcon);
    requests.expectNone('/accounts/csrf');
  });
});

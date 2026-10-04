import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { CurrentAccount } from '../../../core/auth/session.models';
import { SessionService } from '../../../core/auth/session.service';
import { API_CONFIGURATION } from '../../../core/config/api.config';
import { csrfInterceptor } from '../../../core/http/csrf.interceptor';
import { sessionErrorInterceptor } from '../../../core/http/session-error.interceptor';
import { ProfileForm } from '../profile-form/profile-form';
import { PasswordForm } from '../password-form/password-form';
import { DisableForm } from '../disable-form/disable-form';
import { SettingsHost } from '../testing/settings-host';
import { AccountSettings } from './account-settings';

const account: CurrentAccount = { id: 'fixture-account', displayName: 'Nguyễn An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' };
describe('contextual account settings and lifecycle', () => {
  let requests: HttpTestingController; let fixture: ComponentFixture<SettingsHost>;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]),
      provideHttpClient(withInterceptors([csrfInterceptor, sessionErrorInterceptor])), provideHttpClientTesting(),
      { provide: API_CONFIGURATION, useValue: { apiBaseUrl: '', production: false } }] });
    requests = TestBed.inject(HttpTestingController); TestBed.inject(SessionService).signedIn(account, true);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
    HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
    fixture = TestBed.createComponent(SettingsHost); fixture.detectChanges();
  });
  afterEach(() => requests.verify());
  const element = (): HTMLElement => fixture.nativeElement;
  function settings() { return fixture.debugElement.query(By.directive(AccountSettings)).componentInstance as AccountSettings; }
  function open(kind: 'profile' | 'password' | 'disable') {
    fixture.componentInstance.section.set(kind === 'profile' ? 'profile' : kind === 'password' ? 'security' : 'account');
    fixture.detectChanges();
    const button = element().querySelector<HTMLButtonElement>('.setting-row button')!;
    button.click(); fixture.detectChanges(); return button;
  }
  function profile() { return fixture.debugElement.query(By.directive(ProfileForm)).componentInstance as ProfileForm; }
  function password() { return fixture.debugElement.query(By.directive(PasswordForm)).componentInstance as PasswordForm; }
  function disable() { return fixture.debugElement.query(By.directive(DisableForm)).componentInstance as DisableForm; }
  function protection() { requests.expectOne('/accounts/csrf').flush({ requestToken: 'fixture-protection' }); }
  function validPassword() { password().form.setValue({ currentPassword: ' current fixture ', newPassword: ' new fixture pass ', confirmation: ' new fixture pass ' }); }
  function input(id: string, value: string) {
    const control = element().querySelector<HTMLInputElement>('#' + id)!;
    control.value = value; control.dispatchEvent(new Event('input', { bubbles: true })); fixture.detectChanges(); return control;
  }

  it('starts with compact supported settings and omits internal or invented values', () => {
    expect(element().querySelector('form')).toBeNull();
    expect(element().textContent).toContain('Nguyễn An');
    for (const text of ['Solver', 'fixture-account', 'UTC', 'administrator', 'cookie', 'Linking Google']) expect(element().textContent).not.toContain(text);
    expect(element().querySelectorAll('.settings-navigation a')).toHaveLength(3);
    expect(element().querySelector('input[type="email"], input[type="file"], select')).toBeNull();
  });

  it('opens one editor, cancels without a write, restores focus and resets its errors', async () => {
    const opener = open('profile'); await fixture.whenStable();
    expect(document.activeElement?.id).toBe('profile-name');
    input('profile-name', '').dispatchEvent(new Event('blur')); fixture.detectChanges();
    expect(element().querySelector('#profile-name')?.getAttribute('aria-invalid')).toBe('true');
    profile().cancel(); fixture.detectChanges(); await fixture.whenStable();
    expect(element().querySelector('form')).toBeNull(); expect(document.activeElement).toBe(opener);
    open('profile'); expect(profile().form.controls.displayName.value).toBe(account.displayName);
    expect(element().querySelector('.field-error')).toBeNull(); requests.expectNone('/accounts/csrf');
  });

  it('keeps untouched and initially edited profile fields quiet, then corrects shown errors immediately', () => {
    open('profile');
    const field = element().querySelector<HTMLInputElement>('#profile-name')!;
    field.dispatchEvent(new Event('focus')); field.dispatchEvent(new Event('blur')); fixture.detectChanges();
    expect(field.getAttribute('aria-invalid')).toBe('false');
    input('profile-name', ''); expect(element().querySelector('.field-error')).toBeNull();
    field.dispatchEvent(new Event('blur')); fixture.detectChanges(); expect(profile().fieldError()).toContain('display name');
    input('profile-name', 'Trần Linh'); expect(element().querySelector('.field-error')).toBeNull();
    profile().cancel(); fixture.detectChanges(); open('profile'); expect(profile().submitted()).toBe(false);
  });

  it('submits an empty profile with linked focus, retains failed edits and safely retries a trimmed name', async () => {
    open('profile'); profile().form.controls.displayName.setValue('  '); profile().submit(); fixture.detectChanges(); await fixture.whenStable();
    expect(document.activeElement).toBe(element().querySelector('.form-errors'));
    element().querySelector<HTMLAnchorElement>('a[href$="#profile-name"]')!.click(); expect(document.activeElement?.id).toBe('profile-name');
    profile().form.controls.displayName.setValue('a'.repeat(81)); profile().submit(); expect(profile().fieldError()).toContain('80');
    profile().form.controls.displayName.setValue('\u0001Name'); profile().submit(); expect(profile().fieldError()).toContain('control characters');
    profile().form.controls.displayName.setValue('  Trần Linh  '); profile().submit(); profile().submit(); protection();
    const mutation = requests.expectOne('/accounts/me'); expect(mutation.request.body).toEqual({ displayName: 'Trần Linh' });
    mutation.flush({ detail: 'private diagnostics' }, { status: 500, statusText: 'Failed' }); fixture.detectChanges(); await fixture.whenStable();
    expect(profile().form.controls.displayName.value).toBe('Trần Linh'); expect(element().textContent).not.toContain('private diagnostics');
    profile().submit(); requests.expectOne('/accounts/me').flush({ ...account, displayName: 'Trần Linh' }); fixture.detectChanges();
    expect(element().querySelector('form')).toBeNull(); expect(element().textContent).toContain('Display name saved.');
    expect(TestBed.inject(SessionService).account()?.displayName).toBe('Trần Linh');
    open('profile'); expect(profile().submitted()).toBe(false); expect(profile().fieldError()).toBeNull();
  });

  it('acknowledges an unchanged name and closes without an unnecessary write', () => {
    open('profile'); profile().submit(); fixture.detectChanges();
    expect(element().querySelector('form')).toBeNull(); expect(element().textContent).toContain('Display name saved'); requests.expectNone('/accounts/csrf');
  });

  it('does not offer password operations without verified local authentication evidence', () => {
    TestBed.inject(SessionService).signedOut(); TestBed.inject(SessionService).signedIn(account); fixture.detectChanges();
    fixture.componentInstance.section.set('security'); fixture.detectChanges();
    expect(element().textContent).toContain('If you use Google only'); expect(element().textContent).not.toContain('Change password');
    settings().openEditor('password', document.createElement('button')); fixture.detectChanges(); expect(element().querySelector('form')).toBeNull();
    fixture.componentInstance.section.set('account'); fixture.detectChanges();
    expect(element().querySelector('.setting-row button')).toBeNull(); expect(element().textContent).toContain('Google-only accounts cannot use this action');
    expect(element().textContent).not.toContain('********');
  });

  it('delays password and confirmation feedback until edit and blur, corrects it immediately, and resets on cancel', () => {
    open('password');
    const confirm = element().querySelector<HTMLInputElement>('#change-confirm')!;
    confirm.dispatchEvent(new Event('focus')); confirm.dispatchEvent(new Event('blur')); fixture.detectChanges();
    input('change-new', 'new valid passphrase'); expect(password().fieldError('confirmation')).toBeNull();
    input('change-confirm', 'different valid passphrase'); expect(password().fieldError('confirmation')).toBeNull();
    confirm.dispatchEvent(new Event('blur')); fixture.detectChanges(); expect(password().fieldError('confirmation')).toContain('must match');
    input('change-confirm', 'new valid passphrase'); expect(password().fieldError('confirmation')).toBeNull();
    password().cancel(); fixture.detectChanges(); open('password');
    expect(password().form.controls.confirmation.value).toBe(''); expect(element().querySelector('.field-error')).toBeNull();
  });

  it('validates empty password submit with focused links and preserves length constraints', async () => {
    open('password'); password().submit(); fixture.detectChanges(); await fixture.whenStable();
    expect(element().querySelectorAll('.form-errors a')).toHaveLength(3); expect(document.activeElement).toBe(element().querySelector('.form-errors'));
    element().querySelector<HTMLAnchorElement>('a[href$="#change-new"]')!.click(); expect(document.activeElement?.id).toBe('change-new');
    password().form.setValue({ currentPassword: 'short', newPassword: 'a'.repeat(257), confirmation: 'different fixture' }); password().submit();
    expect(password().fieldError('currentPassword')).toContain('12'); expect(password().fieldError('newPassword')).toContain('256');
    expect(password().fieldError('confirmation')).toContain('must match'); requests.expectNone('/accounts/csrf');
  });

  it('retains independent reveal controls and autofill semantics', () => {
    open('password');
    const toggle = element().querySelector<HTMLButtonElement>('button[aria-label="Show new password"]')!;
    expect(element().querySelector('#change-new')?.getAttribute('autocomplete')).toBe('new-password');
    expect(element().querySelector('#change-current')?.getAttribute('autocomplete')).toBe('current-password');
    expect(element().querySelector('#change-new-hint')?.textContent).toBe('At least 12 characters.');
    toggle.click(); fixture.detectChanges(); expect(toggle.getAttribute('aria-pressed')).toBe('true');
    expect(element().querySelector('#change-new')?.getAttribute('type')).toBe('text');
    expect(element().querySelector('#change-current')?.getAttribute('type')).toBe('password');
  });

  it('prevents duplicate or conflicting mutations and clears credentials on password completion', () => {
    open('password'); validPassword(); const form = password(); form.submit(); form.submit(); settings().signOut(); fixture.detectChanges();
    expect(element().querySelector<HTMLButtonElement>('.setting-row:last-of-type button')!.disabled).toBe(true);
    protection(); const mutation = requests.expectOne('/accounts/password');
    expect(mutation.request.body).toEqual({ currentPassword: ' current fixture ', newPassword: ' new fixture pass ' }); requests.expectNone('/accounts/logout');
    mutation.flush(null); expect(form.form.controls.newPassword.value).toBe(''); expect(TestBed.inject(SessionService).account()).toBeNull();
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/account/login'], { queryParams: { notice: 'password-changed', returnUrl: '/settings/security' }, replaceUrl: true });
  });

  it('keeps incorrect-password input and the session after independent verification', async () => {
    open('password'); validPassword(); password().submit(); protection();
    requests.expectOne('/accounts/password').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(password().pending()).toBe(true); requests.expectOne('/accounts/me').flush(account); fixture.detectChanges(); await fixture.whenStable();
    expect(password().form.controls.currentPassword.value).toBe(' current fixture '); expect(password().pending()).toBe(false);
    expect(element().textContent).toContain('current password could not be confirmed'); expect(TestBed.inject(SessionService).account()).toEqual(account);
    expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
  });

  it.each([400, 403, 409, 429, 500])('preserves password input and safe actionable failure (%s)', async (status) => {
    open('password'); validPassword(); password().submit(); protection();
    requests.expectOne('/accounts/password').flush({ detail: 'private error' }, { status, statusText: 'Failed' }); fixture.detectChanges(); await fixture.whenStable();
    expect(password().form.controls.newPassword.value).toBe(' new fixture pass '); expect(password().pending()).toBe(false);
    expect(element().textContent).not.toContain('private error'); expect(password().error()).toBeTruthy();
    if (status === 500) expect(password().error()).toContain('may have completed'); requests.expectNone('/accounts/password');
  });

  it('validates disabling quietly, then focuses Cancel, cycles confirmation focus and clears proof on cancellation', async () => {
    open('disable'); const field = element().querySelector<HTMLInputElement>('#disable-password')!;
    field.dispatchEvent(new Event('blur')); fixture.detectChanges(); expect(field.getAttribute('aria-invalid')).toBe('false');
    input('disable-password', 'short'); expect(disable().fieldError()).toBeNull();
    field.dispatchEvent(new Event('blur')); fixture.detectChanges(); expect(disable().fieldError()).toContain('12');
    input('disable-password', 'fixture passphrase'); expect(disable().fieldError()).toBeNull();
    const opener = element().querySelector<HTMLButtonElement>('minos-disable-form button[type="submit"]')!;
    opener.focus(); opener.click(); fixture.detectChanges(); await fixture.whenStable(); const dialog = element().querySelector('dialog')!;
    const cancel = dialog.querySelector<HTMLButtonElement>('button')!, confirm = dialog.querySelectorAll<HTMLButtonElement>('button')[1];
    expect(dialog.open).toBe(true); expect(document.activeElement).toBe(cancel); requests.expectNone('/accounts/csrf');
    cancel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true })); expect(document.activeElement).toBe(confirm);
    confirm.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })); expect(document.activeElement).toBe(cancel);
    dialog.dispatchEvent(new Event('cancel', { cancelable: true })); fixture.detectChanges();
    expect(element().querySelector('dialog')).toBeNull(); expect(document.activeElement).toBe(opener);
    expect(disable().form.controls.password.value).toBe(''); expect(disable().fieldError()).toBeNull();
    expect(document.documentElement.classList.contains('confirmation-open')).toBe(false);
  });

  it('requires explicit disable confirmation, blocks dismissal while pending and explains its consequence', async () => {
    open('disable'); disable().review(); fixture.detectChanges(); expect(disable().confirming()).toBe(false); expect(disable().fieldError()).toContain('current password');
    disable().form.controls.password.setValue(' fixture password '); disable().review(); fixture.detectChanges(); await fixture.whenStable();
    expect(element().textContent).toContain('Your data is not deleted.'); expect(element().textContent).toContain('no self-service reactivation');
    const form = disable(); form.confirm(); form.confirm(); fixture.detectChanges(); protection();
    const mutation = requests.expectOne('/accounts/disable'); expect(mutation.request.body).toEqual({ password: ' fixture password ' });
    element().querySelector('dialog')!.dispatchEvent(new Event('cancel', { cancelable: true })); expect(form.confirming()).toBe(true);
    mutation.flush(null); expect(form.form.controls.password.value).toBe('');
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/account/login'], { queryParams: { notice: 'account-disabled', returnUrl: '/settings/security' }, replaceUrl: true });
  });

  it('closes a failed disable confirmation and focuses feedback while preserving proof for retry', async () => {
    open('disable'); disable().form.controls.password.setValue('fixture passphrase'); disable().review(); fixture.detectChanges(); await fixture.whenStable();
    disable().confirm(); protection(); requests.expectOne('/accounts/disable').flush({}, { status: 500, statusText: 'Failed' });
    fixture.detectChanges(); await fixture.whenStable(); expect(element().querySelector('dialog')).toBeNull();
    expect(disable().form.controls.password.value).toBe('fixture passphrase'); expect(disable().error()).toContain('may have completed');
    expect(document.activeElement).toBe(element().querySelector('.form-errors')); requests.expectNone('/accounts/disable');
  });

  it('removes private fields and capability evidence after identity changes or expiration', () => {
    open('password'); validPassword(); const old = password();
    TestBed.inject(SessionService).signedIn({ ...account, id: 'another-account' }); fixture.detectChanges();
    expect(element().querySelector('form')).toBeNull(); expect(old.form.controls.newPassword.value).toBe('');
    TestBed.inject(SessionService).signedIn(account, true); fixture.detectChanges(); open('password'); validPassword(); password().submit(); protection();
    requests.expectOne('/accounts/password').flush({}, { status: 401, statusText: 'Unauthorized' });
    requests.expectOne('/accounts/me').flush({}, { status: 401, statusText: 'Unauthorized' }); fixture.detectChanges();
    expect(element().querySelector('form')).toBeNull(); expect(TestBed.inject(SessionService).passwordConfirmed()).toBe(false);
  });

  it('lets already sent writes finish after navigation without retaining credentials or navigating a destroyed view', () => {
    open('password'); validPassword(); const form = password(); form.submit(); protection(); const mutation = requests.expectOne('/accounts/password');
    fixture.destroy(); expect(form.form.controls.newPassword.value).toBe(''); expect(mutation.cancelled).toBe(false);
    mutation.flush(null); expect(TestBed.inject(SessionService).account()).toBeNull(); expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
  });
});

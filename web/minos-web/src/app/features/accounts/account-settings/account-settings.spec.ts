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
import { AccountEntryPage } from '../account-entry-page';
import { ProfileForm } from '../profile-form/profile-form';
import { PasswordForm } from '../password-form/password-form';
import { DisableForm } from '../disable-form/disable-form';

const account: CurrentAccount = { id: 'fixture-account', displayName: 'Nguyễn An', role: 'Solver', createdAtUtc: '2026-10-01T00:00:00Z' };
describe('account settings forms and lifecycle', () => {
  let requests: HttpTestingController; let fixture: ComponentFixture<AccountEntryPage>;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]),
      provideHttpClient(withInterceptors([csrfInterceptor, sessionErrorInterceptor])), provideHttpClientTesting(),
      { provide: API_CONFIGURATION, useValue: { apiBaseUrl: '', production: false } }] });
    requests = TestBed.inject(HttpTestingController); TestBed.inject(SessionService).signedIn(account);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
    HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
    fixture = TestBed.createComponent(AccountEntryPage); fixture.detectChanges();
  });
  afterEach(() => requests.verify());
  const element = (): HTMLElement => fixture.nativeElement;
  function forms() {
    return { profile: fixture.debugElement.query(By.directive(ProfileForm)).componentInstance as ProfileForm,
      password: fixture.debugElement.query(By.directive(PasswordForm)).componentInstance as PasswordForm,
      disable: fixture.debugElement.query(By.directive(DisableForm)).componentInstance as DisableForm };
  }
  function protection() { requests.expectOne('/accounts/csrf').flush({ requestToken: 'fixture-protection' }); }
  function validPassword() { forms().password.form.setValue({ currentPassword: ' current fixture ', newPassword: ' new fixture pass ', confirmation: ' new fixture pass ' }); }

  it('shows only returned account metadata and no unsupported editable identity fields', () => {
    expect(element().textContent).toContain('Nguyễn An'); expect(element().textContent).toContain('Solver');
    expect(element().textContent).toContain('fixture-account'); expect(element().textContent).toContain('administrator');
    expect(element().querySelector('input[type="email"], input[type="file"], select')).toBeNull();
    expect(element().textContent).toContain('Linking Google to an existing account is not available');
    expect(element().querySelector('a[href="/accounts/google/link"]')).toBeNull();
  });

  it('prefills, trims, validates and saves the name without clearing other form input', async () => {
    const { profile, password } = forms();
    expect(profile.form.controls.displayName.value).toBe(account.displayName);
    profile.form.controls.displayName.setValue('  '); profile.submit(); fixture.detectChanges(); await fixture.whenStable();
    expect(document.activeElement).toBe(element().querySelector('minos-profile-form .form-errors'));
    element().querySelector<HTMLAnchorElement>('a[href$="#profile-name"]')!.click(); expect(document.activeElement?.id).toBe('profile-name');
    profile.form.controls.displayName.setValue('a'.repeat(81)); profile.submit(); expect(profile.fieldError()).toContain('80');
    profile.form.controls.displayName.setValue('\u0001Name'); profile.submit(); expect(profile.fieldError()).toContain('control characters');
    password.form.controls.currentPassword.setValue('preserved fixture');
    profile.form.controls.displayName.setValue('  Trần Linh  '); profile.submit(); profile.submit(); fixture.detectChanges();
    expect(element().querySelector<HTMLInputElement>('#change-current')!.disabled).toBe(true);
    protection(); const mutation = requests.expectOne('/accounts/me'); expect(mutation.request.body).toEqual({ displayName: 'Trần Linh' });
    mutation.flush({ ...account, displayName: 'Trần Linh' }); fixture.detectChanges();
    expect(profile.form.controls.displayName.value).toBe('Trần Linh'); expect(profile.form.pristine).toBe(true);
    expect(element().textContent).toContain('Your display name is up to date');
    expect(TestBed.inject(SessionService).account()?.displayName).toBe('Trần Linh');
    expect(password.form.controls.currentPassword.value).toBe('preserved fixture');
  });

  it('keeps a failed profile edit and shows safe recovery without backend diagnostics', async () => {
    const { profile } = forms(); profile.form.controls.displayName.setValue('Linh'); profile.submit(); protection();
    requests.expectOne('/accounts/me').flush({ detail: 'sensitive diagnostics' }, { status: 500, statusText: 'Failed' });
    fixture.detectChanges(); await fixture.whenStable();
    expect(profile.form.controls.displayName.value).toBe('Linh'); expect(element().textContent).not.toContain('sensitive diagnostics');
    expect(document.activeElement).toBe(element().querySelector('minos-profile-form .form-errors'));
    expect(profile.pending()).toBe(false); expect(element().querySelector<HTMLInputElement>('#change-current')!.disabled).toBe(false);
    profile.submit(); requests.expectOne('/accounts/me').flush({ ...account, displayName: 'Linh' });
  });

  it('acknowledges an unchanged name without an unnecessary request', () => {
    forms().profile.submit(); expect(forms().profile.saved()).toBe(true); requests.expectNone('/accounts/csrf');
  });

  it('validates password length and matching confirmation with focused linked feedback', async () => {
    const { password } = forms(); password.submit(); fixture.detectChanges(); await fixture.whenStable();
    expect(element().querySelectorAll('minos-password-form .form-errors a')).toHaveLength(3);
    expect(document.activeElement).toBe(element().querySelector('minos-password-form .form-errors'));
    element().querySelector<HTMLAnchorElement>('a[href$="#change-new"]')!.click(); expect(document.activeElement?.id).toBe('change-new');
    password.form.setValue({ currentPassword: 'short', newPassword: 'a'.repeat(257), confirmation: 'different fixture' }); password.submit();
    expect(password.fieldError('currentPassword')).toContain('12'); expect(password.fieldError('newPassword')).toContain('256');
    expect(password.fieldError('confirmation')).toContain('must match'); requests.expectNone('/accounts/csrf');
  });

  it('exposes labeled independent visibility toggles, descriptions and autofill semantics', () => {
    const toggle = element().querySelector<HTMLButtonElement>('button[aria-label="Show new password"]')!;
    expect(element().querySelector('#change-new')?.getAttribute('autocomplete')).toBe('new-password');
    expect(element().querySelector('#change-current')?.getAttribute('autocomplete')).toBe('current-password');
    expect(element().querySelector('#change-new')?.getAttribute('aria-describedby')).toBe('change-new-hint');
    toggle.click(); fixture.detectChanges(); expect(toggle.getAttribute('aria-pressed')).toBe('true');
    expect(element().querySelector('#change-new')?.getAttribute('type')).toBe('text');
    expect(element().querySelector('#change-current')?.getAttribute('type')).toBe('password');
  });

  it('serializes account mutations and returns to popup sign-in after a password change', () => {
    validPassword(); const { password, profile, disable } = forms(); password.submit(); password.submit(); fixture.detectChanges();
    profile.form.controls.displayName.setValue('Linh'); profile.submit(); disable.review();
    expect(element().querySelector<HTMLInputElement>('#profile-name')!.disabled).toBe(true);
    expect(element().querySelector<HTMLButtonElement>('.sign-out-section button')!.disabled).toBe(true);
    protection(); const mutation = requests.expectOne('/accounts/password');
    expect(mutation.request.body).toEqual({ currentPassword: ' current fixture ', newPassword: ' new fixture pass ' });
    mutation.flush(null); expect(password.form.controls.newPassword.value).toBe('');
    expect(TestBed.inject(SessionService).account()).toBeNull();
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/account/login'], { queryParams: { notice: 'password-changed', returnUrl: '/account' }, replaceUrl: true });
  });

  it('keeps a wrong-password form and authenticated session after independent verification', async () => {
    validPassword(); const { password } = forms(); password.submit(); protection();
    requests.expectOne('/accounts/password').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(password.pending()).toBe(true); requests.expectOne('/accounts/me').flush(account);
    fixture.detectChanges(); await fixture.whenStable();
    expect(password.form.controls.currentPassword.value).toBe(' current fixture '); expect(password.pending()).toBe(false);
    expect(element().textContent).toContain('current password could not be confirmed');
    expect(TestBed.inject(SessionService).account()).toEqual(account); expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
  });

  it.each([400, 403, 409, 429, 500])('preserves password input and makes failure actionable (%s)', async (status) => {
    validPassword(); const { password } = forms(); password.submit(); protection();
    requests.expectOne('/accounts/password').flush({ detail: 'private error' }, { status, statusText: 'Failed' }); fixture.detectChanges(); await fixture.whenStable();
    expect(password.form.controls.newPassword.value).toBe(' new fixture pass '); expect(password.pending()).toBe(false);
    expect(element().textContent).not.toContain('private error'); expect(password.error()).toBeTruthy();
    if (status === 500) expect(password.error()).toContain('may have completed');
    requests.expectNone('/accounts/password');
  });

  it('requires valid proof, focuses Cancel, traps focus and clears proof on disable cancellation', async () => {
    const { disable } = forms(); disable.review(); fixture.detectChanges(); await fixture.whenStable();
    expect(disable.fieldError()).toContain('current password'); expect(disable.confirming()).toBe(false);
    disable.form.controls.password.setValue('fixture passphrase');
    const opener = element().querySelector<HTMLButtonElement>('minos-disable-form button[type="submit"]')!; opener.focus(); opener.click();
    fixture.detectChanges(); await fixture.whenStable(); const dialog = element().querySelector('dialog')!;
    const cancel = dialog.querySelector<HTMLButtonElement>('button')!;
    const confirm = dialog.querySelectorAll<HTMLButtonElement>('button')[1];
    expect(dialog.open).toBe(true); expect(document.activeElement).toBe(cancel);
    expect(document.documentElement.classList.contains('confirmation-open')).toBe(true); requests.expectNone('/accounts/csrf');
    cancel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true })); expect(document.activeElement).toBe(confirm);
    confirm.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })); expect(document.activeElement).toBe(cancel);
    dialog.dispatchEvent(new Event('cancel', { cancelable: true })); fixture.detectChanges();
    expect(element().querySelector('dialog')).toBeNull(); expect(document.activeElement).toBe(opener); expect(disable.form.controls.password.value).toBe('');
    expect(document.documentElement.classList.contains('confirmation-open')).toBe(false);
  });

  it('disables only after explicit confirmation, prevents duplicate confirmation and explains loss of access', async () => {
    const { disable } = forms(); disable.form.controls.password.setValue(' fixture password '); disable.review(); fixture.detectChanges(); await fixture.whenStable();
    disable.confirm(); disable.confirm(); fixture.detectChanges(); protection();
    const mutation = requests.expectOne('/accounts/disable'); expect(mutation.request.body).toEqual({ password: ' fixture password ' });
    element().querySelector('dialog')!.dispatchEvent(new Event('cancel', { cancelable: true })); expect(disable.confirming()).toBe(true);
    expect(element().querySelector<HTMLButtonElement>('dialog button')!.disabled).toBe(true);
    mutation.flush(null); expect(disable.form.controls.password.value).toBe('');
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/account/login'], { queryParams: { notice: 'account-disabled', returnUrl: '/account' }, replaceUrl: true });
  });

  it('closes a failed disable confirmation and focuses safe retry feedback', async () => {
    const { disable } = forms(); disable.form.controls.password.setValue('fixture passphrase'); disable.review(); fixture.detectChanges(); await fixture.whenStable();
    disable.confirm(); protection(); requests.expectOne('/accounts/disable').flush({}, { status: 500, statusText: 'Failed' });
    fixture.detectChanges(); await fixture.whenStable(); expect(element().querySelector('dialog')).toBeNull();
    expect(disable.form.controls.password.value).toBe('fixture passphrase'); expect(disable.error()).toContain('may have completed');
    expect(document.activeElement).toBe(element().querySelector('minos-disable-form .form-errors'));
    requests.expectNone('/accounts/disable');
  });

  it('removes private fields after expiration or identity changes', () => {
    validPassword(); const old = forms(); old.disable.form.controls.password.setValue('fixture passphrase');
    TestBed.inject(SessionService).signedIn({ ...account, id: 'another-account', displayName: 'Linh' }); fixture.detectChanges();
    expect(forms().password.form.controls.newPassword.value).toBe(''); expect(forms().disable.form.controls.password.value).toBe('');
    expect(old.password.form.controls.newPassword.value).toBe('');
    validPassword(); forms().password.submit(); protection(); requests.expectOne('/accounts/password').flush({}, { status: 401, statusText: 'Unauthorized' });
    requests.expectOne('/accounts/me').flush({}, { status: 401, statusText: 'Unauthorized' }); fixture.detectChanges();
    expect(element().querySelector('form')).toBeNull(); expect(element().textContent).toContain('Sign in to continue');
  });

  it('finishes an already sent password change without navigating from a destroyed page', () => {
    validPassword(); const { password } = forms(); password.submit(); protection(); const mutation = requests.expectOne('/accounts/password');
    fixture.destroy(); expect(password.form.controls.newPassword.value).toBe(''); expect(mutation.cancelled).toBe(false);
    mutation.flush(null); expect(TestBed.inject(SessionService).account()).toBeNull(); expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
  });
});

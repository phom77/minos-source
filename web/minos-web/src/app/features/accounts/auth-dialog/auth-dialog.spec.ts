import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthMode, AuthPrompt } from '../../../core/auth/auth-prompt.service';
import { SessionService } from '../../../core/auth/session.service';
import { API_CONFIGURATION } from '../../../core/config/api.config';
import { AuthDialog } from './auth-dialog';

describe('authentication modal', () => {
  const mode = signal<AuthMode | null>(null);
  const prompt = { mode, notice: signal(''), close: vi.fn(), complete: vi.fn(), open: vi.fn() };
  beforeEach(() => {
    mode.set(null); vi.clearAllMocks();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: API_CONFIGURATION, useValue: { apiBaseUrl: '', production: false } }, { provide: AuthPrompt, useValue: prompt },
    ] });
    TestBed.inject(SessionService).signedOut();
    HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
    HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('opens with context, cycles focus, dismisses with Escape and restores its opener', async () => {
    const opener = document.createElement('button'); document.body.append(opener); opener.focus();
    const fixture = TestBed.createComponent(AuthDialog);
    fixture.detectChanges(); mode.set('login'); fixture.detectChanges(); await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement; const dialog = element.querySelector('dialog')!;
    const first = element.querySelector<HTMLButtonElement>('.dialog-close')!;
    const last = element.querySelector<HTMLButtonElement>('.text-button')!;
    expect(dialog.open).toBe(true); expect(document.activeElement).toBe(element.querySelector('#auth-title'));
    first.focus(); first.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(last);
    last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })); expect(document.activeElement).toBe(first);
    const cancel = new Event('cancel', { cancelable: true }); dialog.dispatchEvent(cancel);
    expect(cancel.defaultPrevented).toBe(true); expect(prompt.close).toHaveBeenCalledOnce();
    mode.set(null); fixture.detectChanges(); await fixture.whenStable();
    expect(dialog.open).toBe(false); expect(document.activeElement).toBe(opener); opener.remove();
  });
  it('switches to a fresh registration form and exposes only the supported provider securely', async () => {
    const fixture = TestBed.createComponent(AuthDialog); mode.set('login'); fixture.detectChanges(); await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    const email = element.querySelector<HTMLInputElement>('#email')!; email.value = 'an@example.test'; email.dispatchEvent(new Event('input'));
    const password = element.querySelector<HTMLInputElement>('#password')!; password.value = 'test passphrase'; password.dispatchEvent(new Event('input'));
    element.querySelector<HTMLButtonElement>('.text-button')!.click(); expect(prompt.open).toHaveBeenCalledWith('register');
    mode.set('register'); fixture.detectChanges(); await fixture.whenStable();
    expect(element.querySelector<HTMLInputElement>('#email')!.value).toBe(''); expect(element.querySelector<HTMLInputElement>('#password')!.value).toBe('');
    expect(element.querySelector('#confirmation')).not.toBeNull();
    const provider = element.querySelector<HTMLAnchorElement>('.google-button')!;
    expect(provider.getAttribute('href')).toBe('/accounts/google/login'); expect(provider.target).toBe('_blank');
    expect(provider.rel).toBe('noopener noreferrer'); expect(element.textContent).not.toContain('Forgot Password');
  });
});

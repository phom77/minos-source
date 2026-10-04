import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, defer, map, of, shareReplay, switchMap, tap, throwError } from 'rxjs';

import { CurrentAccount, isCurrentAccount } from '../../../core/auth/session.models';
import { SessionService } from '../../../core/auth/session.service';
import { MINOS_API_REQUEST } from '../../../core/http/api-url.interceptor';
import { SESSION_REQUIRED } from '../../../core/http/session-error.interceptor';
import { ProtectionChangedError } from '../../../core/http/csrf.service';

export interface SignInRequest { readonly email: string; readonly password: string; }
export interface RegistrationRequest extends SignInRequest { readonly displayName: string; }
export interface ChangePasswordRequest { readonly currentPassword: string; readonly newPassword: string; }

@Injectable({ providedIn: 'root' })
export class AccountsApi {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);

  signIn(request: SignInRequest) {
    return this.http.post<CurrentAccount>('/accounts/login', request, {
      context: new HttpContext().set(MINOS_API_REQUEST, true), timeout: 15_000,
    }).pipe(tap((account) => this.session.signedIn(account, true)), shareReplay({ bufferSize: 1, refCount: false }));
  }

  register(request: RegistrationRequest) {
    return this.http.post<CurrentAccount>('/accounts/register', request, {
      context: new HttpContext().set(MINOS_API_REQUEST, true), timeout: 15_000,
    }).pipe(tap((account) => this.session.signedIn(account, true)), shareReplay({ bufferSize: 1, refCount: false }));
  }

  signOut() {
    return defer(() => {
      const identity = this.session.account();
      if (!identity) return throwError(() => new ProtectionChangedError());
      return this.http.post<void>('/accounts/logout', null, {
        context: new HttpContext().set(MINOS_API_REQUEST, true).set(SESSION_REQUIRED, true), timeout: 15_000,
      }).pipe(tap(() => { if (this.session.account() === identity) this.session.signedOut(); }));
    }).pipe(shareReplay({ bufferSize: 1, refCount: false }));
  }

  rename(displayName: string) {
    return defer(() => {
      const identity = this.session.account();
      if (!identity) return throwError(() => new ProtectionChangedError());
      return this.http.patch<unknown>('/accounts/me', { displayName }, {
        context: new HttpContext().set(MINOS_API_REQUEST, true).set(SESSION_REQUIRED, true), timeout: 15_000,
      }).pipe(map((account) => {
        if (!isCurrentAccount(account) || account.id !== identity.id) throw new Error('Invalid profile response.');
        // A response for an old identity must never replace a newer session.
        if (this.session.account() === identity) this.session.signedIn(account);
        return account;
      }));
    }).pipe(shareReplay({ bufferSize: 1, refCount: false }));
  }

  changePassword(request: ChangePasswordRequest) { return this.securityMutation('/accounts/password', request); }

  disable(password: string) { return this.securityMutation('/accounts/disable', { password }); }

  private securityMutation(url: '/accounts/password' | '/accounts/disable', body: ChangePasswordRequest | { password: string }) {
    return defer(() => {
      const identity = this.session.account();
      if (!identity) return throwError(() => new ProtectionChangedError());
      // These endpoints also return 401 for an incorrect current password. Do NOT
      // mark them SESSION_REQUIRED: only /me can distinguish that from expiration.
      return this.http.post<void>(url, body, {
        context: new HttpContext().set(MINOS_API_REQUEST, true), timeout: 15_000,
      }).pipe(
        tap(() => { if (this.session.account() === identity) this.session.signedOut(); }),
        catchError((error: unknown) => {
          if (!(error instanceof HttpErrorResponse) || error.status !== 401 || this.session.account() !== identity) {
            return throwError(() => error);
          }
          // Keep the form/session intact while checking; an outage is not logout.
          return this.http.get<unknown>('/accounts/me', {
            context: new HttpContext().set(MINOS_API_REQUEST, true), timeout: 15_000,
          }).pipe(tap((account) => {
            if (isCurrentAccount(account) && account.id !== identity.id && this.session.account() === identity) {
              this.session.signedIn(account);
            }
          }), catchError((check: unknown) => {
            if (check instanceof HttpErrorResponse && check.status === 401 && this.session.account() === identity) {
              this.session.signedOut('expired');
            }
            return of(null);
          }), switchMap(() => throwError(() => error)));
        }),
      );
    }).pipe(shareReplay({ bufferSize: 1, refCount: false }));
  }
}

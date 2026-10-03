import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { shareReplay, tap } from 'rxjs';

import { CurrentAccount } from '../../../core/auth/session.models';
import { SessionService } from '../../../core/auth/session.service';
import { MINOS_API_REQUEST } from '../../../core/http/api-url.interceptor';
import { SESSION_REQUIRED } from '../../../core/http/session-error.interceptor';

export interface SignInRequest { readonly email: string; readonly password: string; }
export interface RegistrationRequest extends SignInRequest { readonly displayName: string; }

@Injectable({ providedIn: 'root' })
export class AccountsApi {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);

  signIn(request: SignInRequest) {
    return this.http.post<CurrentAccount>('/accounts/login', request, {
      context: new HttpContext().set(MINOS_API_REQUEST, true), timeout: 15_000,
    }).pipe(tap((account) => this.session.signedIn(account)), shareReplay({ bufferSize: 1, refCount: false }));
  }

  register(request: RegistrationRequest) {
    return this.http.post<CurrentAccount>('/accounts/register', request, {
      context: new HttpContext().set(MINOS_API_REQUEST, true), timeout: 15_000,
    }).pipe(tap((account) => this.session.signedIn(account)), shareReplay({ bufferSize: 1, refCount: false }));
  }

  signOut() {
    return this.http.post<void>('/accounts/logout', null, {
      context: new HttpContext().set(MINOS_API_REQUEST, true).set(SESSION_REQUIRED, true), timeout: 15_000,
    }).pipe(tap(() => this.session.signedOut()), shareReplay({ bufferSize: 1, refCount: false }));
  }
}

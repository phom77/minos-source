import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { catchError, defer, finalize, map, Observable, of, shareReplay } from 'rxjs';

import { MINOS_API_REQUEST } from '../http/api-url.interceptor';
import { CsrfService } from '../http/csrf.service';
import { CurrentAccount, isCurrentAccount, SessionState } from './session.models';

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly stateValue = signal<SessionState>({ status: 'checking' });
  private pending: Observable<SessionState> | null = null;
  private generation = 0;
  readonly state = this.stateValue.asReadonly();
  readonly account = computed(() => {
    const state = this.state();
    return state.status === 'authenticated' ? state.account : null;
  });

  ensure(): Observable<SessionState> {
    return this.state().status === 'checking' ? this.refresh() : of(this.state());
  }

  refresh(): Observable<SessionState> {
    if (this.pending) return this.pending;
    const generation = this.generation;
    const previousAccount = this.account();
    this.stateValue.set({ status: 'checking' });
    const request = defer(() => this.http.get<unknown>('/accounts/me', {
      context: new HttpContext().set(MINOS_API_REQUEST, true), timeout: 15_000,
    })).pipe(
      map((response): SessionState => {
        if (!isCurrentAccount(response)) throw new Error('Invalid account response.');
        return { status: 'authenticated', account: response };
      }),
      catchError((error: unknown) => of<SessionState>(
        error instanceof HttpErrorResponse && error.status === 401
          ? { status: 'guest', reason: previousAccount ? 'expired' : 'signed-out' }
          : { status: 'unavailable' },
      )),
      map((state) => {
        // A slow pre-login request must not overwrite a newly authenticated identity.
        if (generation !== this.generation) return this.state();
        const nextId = state.status === 'authenticated' ? state.account.id : null;
        if ((previousAccount?.id ?? null) !== nextId) this.csrf.invalidate();
        this.stateValue.set(state);
        return state;
      }),
      finalize(() => { if (this.pending === request) this.pending = null; }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    this.pending = request;
    return request;
  }

  signedIn(account: CurrentAccount): void {
    if (!isCurrentAccount(account)) throw new Error('Invalid account response.');
    this.replace({ status: 'authenticated', account });
  }

  signedOut(reason: 'signed-out' | 'expired' = 'signed-out'): void {
    this.replace({ status: 'guest', reason });
  }

  private replace(state: SessionState): void {
    this.generation++;
    this.pending = null;
    this.csrf.invalidate();
    this.stateValue.set(state);
  }
}

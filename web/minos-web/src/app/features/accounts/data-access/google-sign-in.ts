import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { Subject, Subscription, timer, timeout } from 'rxjs';

import { SessionService } from '../../../core/auth/session.service';
import { isCurrentAccount } from '../../../core/auth/session.models';
import { API_CONFIGURATION } from '../../../core/config/api.config';
import { MINOS_API_REQUEST } from '../../../core/http/api-url.interceptor';

/** The backend owns OAuth/PKCE/cookies. This service only confirms the resulting session. */
@Injectable()
export class GoogleSignIn {
  private readonly document = inject(DOCUMENT);
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly config = inject(API_CONFIGURATION);
  private polling: Subscription | null = null;
  private deadline: Subscription | null = null;
  private request: Subscription | null = null;
  private generation = 0;
  readonly waiting = signal(false);
  readonly checking = signal(false);
  readonly error = signal<string | null>(null);
  readonly completed = new Subject<void>();
  readonly loginUrl = (this.config.apiBaseUrl.replace(/\/+$/, '') || '') + '/accounts/google/login';

  constructor() { inject(DestroyRef).onDestroy(() => { this.stop(); this.completed.complete(); }); }

  start(event: Event): void {
    this.error.set(null);
    const origin = this.document.location?.origin;
    const target = new URL(this.loginUrl, origin);
    if (this.document.location?.protocol !== 'https:' || target.origin !== origin) {
      event.preventDefault();
      this.error.set('Google sign-in needs the HTTPS app with a same-origin API and configured Google callback.');
      return;
    }
    this.stop();
    this.waiting.set(true);
    this.polling = timer(1500, 2500).subscribe(() => {
      if (this.document.visibilityState === 'visible') this.check();
    });
    this.deadline = timer(300_000).subscribe(() => {
      this.stop();
      this.error.set('Google sign-in was not confirmed. Try again, or sign in with your email.');
    });
  }

  check(manual = false): void {
    if (!this.waiting() || this.checking()) return;
    this.checking.set(true);
    this.error.set(null);
    const generation = this.generation;
    this.request = this.http.get<unknown>('/accounts/me', { context: new HttpContext().set(MINOS_API_REQUEST, true) })
      .pipe(timeout(15_000)).subscribe({
        next: (account) => {
          if (generation !== this.generation) return;
          this.stop();
          if (!isCurrentAccount(account)) {
            this.error.set('We could not confirm your account. Try signing in again.');
            return;
          }
          this.session.signedIn(account);
          this.completed.next();
        },
        error: (error: unknown) => {
          if (generation !== this.generation) return;
          this.checking.set(false);
          if (error instanceof HttpErrorResponse && error.status === 401) {
            if (manual) this.error.set('Not signed in yet. Finish Google sign-in in the new tab, then check again.');
            return;
          }
          this.stop();
          this.error.set('We could not check your session. Try again when the account service is available.');
        },
      });
  }

  stop(): void {
    this.generation++;
    this.polling?.unsubscribe(); this.deadline?.unsubscribe(); this.request?.unsubscribe();
    this.polling = this.deadline = this.request = null;
    this.waiting.set(false); this.checking.set(false);
  }
}

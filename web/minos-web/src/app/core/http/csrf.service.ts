import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { defer, finalize, map, Observable, of, shareReplay } from 'rxjs';

import { MINOS_API_REQUEST } from './api-url.interceptor';

export class ProtectionChangedError extends Error {
  constructor() { super('Session protection changed. Submit again after checking your session.'); }
}

@Injectable({ providedIn: 'root' })
export class CsrfService {
  private readonly http = inject(HttpClient);
  private token: string | null = null;
  private pending: Observable<string> | null = null;
  private generation = 0;

  getToken(): Observable<string> {
    if (this.token) return of(this.token);
    if (this.pending) return this.pending;
    const generation = this.generation;
    const request = defer(() => this.http.get<{ requestToken: string }>('/accounts/csrf', {
      context: new HttpContext().set(MINOS_API_REQUEST, true), timeout: 15_000,
    })).pipe(
      map((response) => {
        if (generation !== this.generation) throw new ProtectionChangedError();
        if (typeof response?.requestToken !== 'string' || !response.requestToken.trim()) {
          throw new Error('Invalid session protection response.');
        }
        this.token = response.requestToken;
        return this.token;
      }),
      finalize(() => { if (this.pending === request) this.pending = null; }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    this.pending = request;
    return request;
  }

  invalidate(): void {
    this.generation++;
    this.token = null;
    this.pending = null;
  }
}

import { effect, inject, Injectable } from '@angular/core';
import { SessionService } from '../../../core/auth/session.service';

/** One bounded, account-owned draft in memory. Never persist submission source in browser storage. */
@Injectable({ providedIn: 'root' })
export class SubmissionDraft {
  private readonly session = inject(SessionService);
  private value: { accountId: string; problemId: string; source: string; requestKey: string | null } | null = null;
  constructor() { effect(() => { if (this.value && this.session.account()?.id !== this.value.accountId) this.value = null; }); }
  read(problemId: string): string {
    return this.value?.accountId === this.session.account()?.id && this.value?.problemId === problemId ? this.value.source : '';
  }
  save(problemId: string, source: string): void {
    const accountId = this.session.account()?.id;
    if (accountId && new TextEncoder().encode(source).length <= 65_536) this.value = { accountId, problemId, source,
      requestKey: this.value?.accountId === accountId && this.value.problemId === problemId && this.value.source === source ? this.value.requestKey : null };
    else if (this.value?.problemId === problemId) this.value = null;
  }
  clear(problemId: string): void { if (this.value?.problemId === problemId) this.value = null; }
  requestKey(problemId: string, source: string): string {
    this.save(problemId, source);
    if (!this.value) throw new Error('An active account and bounded source are required.');
    return this.value.requestKey ??= crypto.randomUUID();
  }
  hasRequestKey(problemId: string, source: string): boolean {
    return this.value?.accountId === this.session.account()?.id && this.value?.problemId === problemId &&
      this.value?.source === source && !!this.value?.requestKey;
  }
}

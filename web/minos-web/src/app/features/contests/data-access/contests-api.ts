import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { MINOS_API_REQUEST } from '../../../core/http/api-url.interceptor';
import { SESSION_REQUIRED } from '../../../core/http/session-error.interceptor';
import { isProblemId, isPublishedProblem } from '../../problems/data-access/problems-api';

export type ContestState = 'Draft' | 'Scheduled' | 'Live' | 'Ended';
export interface ContestSummary {
  id: string; title: string; startsAtUtc: string; endsAtUtc: string; state: ContestState; problemCount: number;
}
export interface ContestProblem { id: string; problemId: string; versionId: string; order: number; title: string; }
export interface Contest extends ContestSummary {
  description: string; revision: number; canManage: boolean; isRegistered: boolean; canRegister: boolean;
  canSubmit: boolean; serverTimeUtc: string; problems: readonly ContestProblem[];
}
export interface ContestPage { items: readonly ContestSummary[]; page: number; pageSize: number; total: number; }
export interface SaveContest { title: string; description: string; startsAtUtc: string; endsAtUtc: string; versionIds: readonly string[]; revision?: number; }
export interface ContestSubmission { id: string; contestProblemId: string; status: string; verdict: string | null; createdAtUtc: string; }
export interface ContestSubmissionPage { items: readonly ContestSubmission[]; page: number; pageSize: number; total: number; }
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null;
const date = (v: unknown): v is string => typeof v === 'string' && Number.isFinite(Date.parse(v));
const id = (v: unknown): v is string => typeof v === 'string' && isProblemId(v);
const summary = (v: unknown): v is ContestSummary => record(v) && id(v['id']) && typeof v['title'] === 'string' &&
  date(v['startsAtUtc']) && date(v['endsAtUtc']) && ['Draft', 'Scheduled', 'Live', 'Ended'].includes(String(v['state'])) &&
  Number.isSafeInteger(v['problemCount']) && Number(v['problemCount']) >= 0 && Number(v['problemCount']) <= 26;
export const isContest = (v: unknown): v is Contest => summary(v) && record(v) && typeof v['description'] === 'string' &&
  Number.isSafeInteger(v['revision']) && date(v['serverTimeUtc']) &&
  ['canManage', 'isRegistered', 'canRegister', 'canSubmit'].every(k => typeof v[k] === 'boolean') &&
  Array.isArray(v['problems']) && v['problems'].length <= 26 && v['problems'].every(p => record(p) && id(p['id']) && id(p['problemId']) &&
    id(p['versionId']) && Number.isSafeInteger(p['order']) && Number(p['order']) > 0 && typeof p['title'] === 'string');
export function contestError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'The request could not be confirmed. Refresh before trying again.';
  if (error.status === 409) return 'This contest changed in another tab. Reload before editing again.';
  if (error.status === 403) return 'You do not have permission for this action. Check your registration and account.';
  if (error.status === 401) return 'Sign in again to continue.';
  if (error.status === 404) return 'This contest or problem is not available.';
  if (error.status === 429) return 'Too many active submissions. Wait a moment and try again.';
  if (error.status === 400 && record(error.error) && record(error.error['errors'])) {
    const messages = Object.values(error.error['errors']).flat().filter((x): x is string => typeof x === 'string');
    if (messages.length) return messages.join(' ');
  }
  return 'The request could not be confirmed. Refresh before trying again.';
}
@Injectable({ providedIn: 'root' })
export class ContestsApi {
  private readonly http = inject(HttpClient);
  private options(required = true) { return { context: new HttpContext().set(MINOS_API_REQUEST, true).set(SESSION_REQUIRED, required), timeout: 15_000 }; }
  list(mine: boolean, page: number) {
    return this.http.get<unknown>(`/api/contests${mine ? '/mine' : ''}`, { ...this.options(mine), params: { page, pageSize: 20 } }).pipe(map(v => {
      if (!record(v) || !Array.isArray(v['items']) || !v['items'].every(summary) || v['page'] !== page || v['pageSize'] !== 20 ||
        !Number.isSafeInteger(v['total']) || Number(v['total']) < 0) throw new Error('Invalid contest list.');
      return v as unknown as ContestPage;
    }));
  }
  detail(id: string) { return this.http.get<unknown>(`/api/contests/${id}`, this.options(false)).pipe(map(v => this.read(v, id))); }
  save(id: string | null, request: SaveContest) {
    return (id ? this.http.put<unknown>(`/api/contests/${id}`, request, this.options()) :
      this.http.post<unknown>('/api/contests', request, this.options())).pipe(map(v => this.read(v, id)));
  }
  publish(id: string, revision: number) { return this.http.post<unknown>(`/api/contests/${id}/publish`, { revision }, this.options()).pipe(map(v => this.read(v, id))); }
  register(id: string) { return this.http.put<unknown>(`/api/contests/${id}/registration`, null, this.options()).pipe(map(v => this.read(v, id))); }
  problem(contestId: string, problemId: string) {
    return this.http.get<unknown>(`/api/contests/${contestId}/problems/${problemId}`, this.options()).pipe(map(value => {
      if (!isPublishedProblem(value)) throw new Error('Invalid contest problem.');
      return value;
    }));
  }
  submit(contestId: string, problemId: string, source: string, requestKey?: string) {
    return this.http.post<unknown>(`/api/contests/${contestId}/problems/${problemId}/submissions`, { language: 'cpp20', source, ...(requestKey ? { requestKey } : {}) }, this.options()).pipe(map(v => {
      if (!record(v) || !id(v['id'])) throw new Error('Invalid submission response.');
      return { id: v['id'] };
    }));
  }
  submissions(contestId: string, page: number) {
    return this.http.get<unknown>(`/api/contests/${contestId}/submissions/mine`, { ...this.options(), params: { page, pageSize: 20 } }).pipe(map(v => {
      if (!record(v) || v['page'] !== page || v['pageSize'] !== 20 || !Number.isSafeInteger(v['total']) || Number(v['total']) < 0 ||
          !Array.isArray(v['items']) || v['items'].length > 20 || !v['items'].every(s => record(s) && id(s['id']) && id(s['contestProblemId']) &&
            ['Pending', 'Judging', 'Completed', 'SystemFailed'].includes(String(s['status'])) && (s['verdict'] === null || typeof s['verdict'] === 'string') && date(s['createdAtUtc']))) throw new Error('Invalid contest history.');
      return v as unknown as ContestSubmissionPage;
    }));
  }
  private read(value: unknown, expectedId: string | null): Contest {
    if (!isContest(value) || (expectedId !== null && value.id.toLowerCase() !== expectedId.toLowerCase())) throw new Error('Invalid contest response.');
    return value;
  }
}

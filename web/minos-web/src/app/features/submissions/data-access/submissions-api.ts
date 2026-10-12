import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, shareReplay } from 'rxjs';
import { MINOS_API_REQUEST } from '../../../core/http/api-url.interceptor';
import { SESSION_REQUIRED } from '../../../core/http/session-error.interceptor';
import { isSubmission, isSubmissionSummary, record, SubmissionPage } from './submission.models';

@Injectable({ providedIn: 'root' })
export class SubmissionsApi {
  private readonly http = inject(HttpClient);
  create(problemId: string, source: string, requestKey?: string) {
    return this.http.post<unknown>(`/api/problems/${problemId}/submissions`, { language: 'cpp20', source, ...(requestKey ? { requestKey } : {}) }, {
      context: this.context(), timeout: 15_000,
    }).pipe(map(value => {
      if (!isSubmission(value) || value.problemId.toLowerCase() !== problemId.toLowerCase()) throw new Error('Invalid submission response.');
      return value;
    }), shareReplay({ bufferSize: 1, refCount: false }));
  }
  detail(id: string) {
    return this.http.get<unknown>(`/api/submissions/${id}`, { context: this.context(), timeout: 15_000 }).pipe(map(value => {
      if (!isSubmission(value) || value.id.toLowerCase() !== id.toLowerCase()) throw new Error('Invalid submission response.');
      return value;
    }));
  }
  list(page: number, pageSize: number) {
    return this.http.get<unknown>('/api/me/submissions', { params: { page, pageSize }, context: this.context(), timeout: 15_000 }).pipe(map(value => {
      if (!record(value) || value['page'] !== page || value['pageSize'] !== pageSize || !Number.isSafeInteger(value['total']) ||
          Number(value['total']) < 0 || !Array.isArray(value['items']) || value['items'].length > pageSize || !value['items'].every(isSubmissionSummary)) {
        throw new Error('Invalid submission history response.');
      }
      return value as unknown as SubmissionPage;
    }));
  }
  private context() { return new HttpContext().set(MINOS_API_REQUEST, true).set(SESSION_REQUIRED, true); }
}

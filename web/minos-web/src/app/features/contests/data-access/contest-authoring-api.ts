import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { MINOS_API_REQUEST } from '../../../core/http/api-url.interceptor';
import { SESSION_REQUIRED } from '../../../core/http/session-error.interceptor';
import { PublishedProblem } from '../../problems/data-access/problems-api';
export interface AuthoringDraft {
  id: string; title: string; statement: string; timeLimitMilliseconds: number | null; memoryLimitMegabytes: number | null;
  samples: readonly { input: string; output: string }[]; testCases: readonly { id: string; order: number; inputSizeBytes: number; outputSizeBytes: number }[];
}
export interface DraftContent { title: string; statement: string; timeLimitMilliseconds: number; memoryLimitMegabytes: number; samples: readonly { input: string; output: string }[]; }
@Injectable({ providedIn: 'root' })
export class ContestAuthoringApi {
  private readonly http = inject(HttpClient);
  private options() { return { context: new HttpContext().set(MINOS_API_REQUEST, true).set(SESSION_REQUIRED, true), timeout: 15000 }; }
  create(title: string, statement: string) { return this.http.post<AuthoringDraft>('/api/authoring/problems', { title, statement }, this.options()); }
  read(id: string) { return this.http.get<AuthoringDraft>(`/api/authoring/problems/${id}/draft`, this.options()); }
  update(id: string, content: DraftContent) { return this.http.put<AuthoringDraft>(`/api/authoring/problems/${id}/draft`, { ...content, checker: 'Exact' }, this.options()); }
  upload(id: string, file: File) { return this.http.post<AuthoringDraft>(`/api/authoring/problems/${id}/draft/tests`, file, { ...this.options(), headers: { 'Content-Type': 'application/zip' }, timeout: 60000 }); }
  remove(id: string, testId: string) { return this.http.delete<AuthoringDraft>(`/api/authoring/problems/${id}/draft/tests/${testId}`, this.options()); }
  publish(id: string) { return this.http.post<PublishedProblem>(`/api/authoring/problems/${id}/publish`, { statementReviewed: true, samplesVerified: true, referenceSolutionChecked: true }, this.options()); }
}

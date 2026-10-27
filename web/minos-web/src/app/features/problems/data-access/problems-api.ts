import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';

import { MINOS_API_REQUEST } from '../../../core/http/api-url.interceptor';
import { CatalogQuery, Difficulty, SolveStatus } from '../catalog-query';

export interface PublishedProblemIdentity {
  readonly problemId: string; readonly versionId: string; readonly versionNumber: number;
  readonly title: string; readonly publishedAtUtc: string;
}
export interface ProblemTopic { readonly slug: string; readonly displayName: string; }
export interface ProblemTopicCount extends ProblemTopic { readonly problemCount: number; }
export interface ProblemClassification { readonly difficulty: Difficulty | null; readonly topics: readonly ProblemTopic[]; readonly needsReassessment: boolean; }
export interface PublishedProblemSummary extends PublishedProblemIdentity {
  readonly publicNumber: number; readonly classification: ProblemClassification; readonly solveStatus: SolveStatus | null;
}
export interface PublishedProblem extends PublishedProblemIdentity {
  readonly statement: string; readonly timeLimitMilliseconds: number; readonly memoryLimitMegabytes: number;
  readonly checker: 'Exact'; readonly privateTestCount: number;
  readonly samples: readonly { readonly input: string; readonly output: string }[];
}
export interface ProblemPage {
  readonly items: readonly PublishedProblemSummary[]; readonly page: number; readonly pageSize: number; readonly total: number;
  readonly progress?: { readonly solved: number; readonly attempted: number; readonly total: number } | null;
}
export const isProblemId = (value: string | null): value is string =>
  value !== null && /^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(value);
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;
const positive = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const identity = (value: unknown): value is PublishedProblemIdentity => record(value) &&
  typeof value['problemId'] === 'string' && isProblemId(value['problemId']) &&
  typeof value['versionId'] === 'string' && isProblemId(value['versionId']) && positive(value['versionNumber']) &&
  typeof value['title'] === 'string' && value['title'].trim().length > 0 &&
  typeof value['publishedAtUtc'] === 'string' && Number.isFinite(Date.parse(value['publishedAtUtc']));
const topic = (value: unknown): value is ProblemTopic => record(value) && typeof value['slug'] === 'string' &&
  value['slug'].length <= 48 && /^[a-z]+(?:-[a-z]+)*$/.test(value['slug']) && typeof value['displayName'] === 'string' && value['displayName'].trim().length > 0;
const classification = (value: unknown): value is ProblemClassification => record(value) &&
  (value['difficulty'] === null || ['Easy', 'Medium', 'Hard'].includes(String(value['difficulty']))) &&
  Array.isArray(value['topics']) && value['topics'].length <= 32 && value['topics'].every(topic) && typeof value['needsReassessment'] === 'boolean';
const summary = (value: unknown): value is PublishedProblemSummary => identity(value) && record(value) && positive(value['publicNumber']) &&
  classification(value['classification']) && (value['solveStatus'] === null || ['NotAttempted', 'Attempted', 'Solved'].includes(String(value['solveStatus'])));
const nonnegative = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const progress = (value: unknown): boolean => value === undefined || value === null || (record(value) &&
  nonnegative(value['solved']) && nonnegative(value['attempted']) && nonnegative(value['total']) && value['solved'] <= value['attempted'] && value['attempted'] <= value['total']);
export const isPublishedProblem = (value: unknown): value is PublishedProblem => identity(value) && record(value) &&
  typeof value['statement'] === 'string' && positive(value['timeLimitMilliseconds']) && positive(value['memoryLimitMegabytes']) &&
  value['checker'] === 'Exact' && typeof value['privateTestCount'] === 'number' && Number.isSafeInteger(value['privateTestCount']) &&
  value['privateTestCount'] >= 0 && Array.isArray(value['samples']) && value['samples'].every(sample =>
    record(sample) && typeof sample['input'] === 'string' && typeof sample['output'] === 'string');

@Injectable({ providedIn: 'root' })
export class ProblemsApi {
  private readonly http = inject(HttpClient);
  list(page: number, pageSize: number, filters: Omit<CatalogQuery, 'page' | 'pageSize'> = {}) {
    return this.http.get<unknown>('/api/problems', {
      params: { page, pageSize, ...filters }, context: this.context(), timeout: 15_000,
    }).pipe(map(value => {
      if (!record(value) || value['page'] !== page || value['pageSize'] !== pageSize ||
          typeof value['total'] !== 'number' || !Number.isSafeInteger(value['total']) || value['total'] < 0 ||
          !Array.isArray(value['items']) || value['items'].length > pageSize || !value['items'].every(summary) || !progress(value['progress'])) {
        throw new Error('Invalid problem catalog response.');
      }
      return value as unknown as ProblemPage;
    }));
  }
  topics() {
    return this.http.get<unknown>('/api/problems/topics', { context: this.context(), timeout: 15_000 }).pipe(map(value => {
      if (!Array.isArray(value) || !value.every(item => topic(item) && record(item) && nonnegative(item['problemCount']))) throw new Error('Invalid topic response.');
      return value as readonly ProblemTopicCount[];
    }));
  }
  latest(problemId: string) { return this.read(`/api/problems/${problemId}`, problemId, false); }
  version(versionId: string) { return this.read(`/api/problems/versions/${versionId}`, versionId, true); }
  private read(url: string, id: string, version: boolean) {
    return this.http.get<unknown>(url, { context: this.context(), timeout: 15_000 }).pipe(map(value => {
      if (!isPublishedProblem(value) || (version ? value.versionId : value.problemId).toLowerCase() !== id.toLowerCase()) {
        throw new Error('Invalid published problem response.');
      }
      return value;
    }));
  }
  private context() { return new HttpContext().set(MINOS_API_REQUEST, true); }
}

import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';

import { MINOS_API_REQUEST } from '../../../core/http/api-url.interceptor';

export interface PublishedProblemSummary {
  readonly problemId: string; readonly versionId: string; readonly versionNumber: number;
  readonly title: string; readonly publishedAtUtc: string;
}
export interface PublishedProblem extends PublishedProblemSummary {
  readonly statement: string; readonly timeLimitMilliseconds: number; readonly memoryLimitMegabytes: number;
  readonly checker: 'Exact'; readonly privateTestCount: number;
  readonly samples: readonly { readonly input: string; readonly output: string }[];
}
export interface ProblemPage {
  readonly items: readonly PublishedProblemSummary[]; readonly page: number; readonly pageSize: number; readonly total: number;
}
export const isProblemId = (value: string | null): value is string =>
  value !== null && /^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(value);
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;
const positive = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const summary = (value: unknown): value is PublishedProblemSummary => record(value) &&
  typeof value['problemId'] === 'string' && isProblemId(value['problemId']) &&
  typeof value['versionId'] === 'string' && isProblemId(value['versionId']) && positive(value['versionNumber']) &&
  typeof value['title'] === 'string' && value['title'].trim().length > 0 &&
  typeof value['publishedAtUtc'] === 'string' && Number.isFinite(Date.parse(value['publishedAtUtc']));
export const isPublishedProblem = (value: unknown): value is PublishedProblem => summary(value) && record(value) &&
  typeof value['statement'] === 'string' && positive(value['timeLimitMilliseconds']) && positive(value['memoryLimitMegabytes']) &&
  value['checker'] === 'Exact' && typeof value['privateTestCount'] === 'number' && Number.isSafeInteger(value['privateTestCount']) &&
  value['privateTestCount'] >= 0 && Array.isArray(value['samples']) && value['samples'].every(sample =>
    record(sample) && typeof sample['input'] === 'string' && typeof sample['output'] === 'string');

@Injectable({ providedIn: 'root' })
export class ProblemsApi {
  private readonly http = inject(HttpClient);
  list(page: number, pageSize: number) {
    return this.http.get<unknown>('/api/problems', {
      params: { page, pageSize }, context: this.context(), timeout: 15_000,
    }).pipe(map(value => {
      if (!record(value) || value['page'] !== page || value['pageSize'] !== pageSize ||
          typeof value['total'] !== 'number' || !Number.isSafeInteger(value['total']) || value['total'] < 0 ||
          !Array.isArray(value['items']) || value['items'].length > pageSize || !value['items'].every(summary)) {
        throw new Error('Invalid problem catalog response.');
      }
      return value as unknown as ProblemPage;
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

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MINOS_API_REQUEST } from '../../../core/http/api-url.interceptor';
import { ProblemsApi } from './problems-api';
import { publishedProblem as problem } from '../testing/problem-fixtures';

describe('public problem API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  it('uses public paginated GET requests and validates the response', () => {
    const result = vi.fn();
    TestBed.inject(ProblemsApi).list(2, 10).subscribe(result);
    const request = TestBed.inject(HttpTestingController).expectOne('/api/problems?page=2&pageSize=10');
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(MINOS_API_REQUEST)).toBe(true);
    expect(request.request.headers.has('X-CSRF-TOKEN')).toBe(false);
    const page = { items: [problem], page: 2, pageSize: 10, total: 11 };
    request.flush(page);
    expect(result).toHaveBeenCalledWith(page);
  });
  it.each([{ items: [], page: 1, pageSize: 20, total: -1 }, { items: [{}], page: 1, pageSize: 20, total: 1 }, { items: [], page: 2, pageSize: 20, total: 0 }])('rejects a malformed catalog response', response => {
    const error = vi.fn();
    TestBed.inject(ProblemsApi).list(1, 20).subscribe({ error });
    TestBed.inject(HttpTestingController).expectOne('/api/problems?page=1&pageSize=20').flush(response);
    expect(error).toHaveBeenCalled();
  });
  it('reads latest and exact immutable versions separately', () => {
    const result = vi.fn(); const api = TestBed.inject(ProblemsApi); const http = TestBed.inject(HttpTestingController);
    api.latest(problem.problemId).subscribe(result); http.expectOne('/api/problems/' + problem.problemId).flush(problem);
    api.version(problem.versionId).subscribe(result); http.expectOne('/api/problems/versions/' + problem.versionId).flush(problem);
    expect(result).toHaveBeenCalledTimes(2);
  });
  it.each([{ checker: 'Unsupported' }, { samples: [{ input: 1, output: '2' }] }, { timeLimitMilliseconds: 0 }, { versionId: '33333333-3333-3333-3333-333333333333' }])('rejects malformed/mismatched published versions', patch => {
    const error = vi.fn();
    TestBed.inject(ProblemsApi).version(problem.versionId).subscribe({ error });
    TestBed.inject(HttpTestingController).expectOne('/api/problems/versions/' + problem.versionId).flush({ ...problem, ...patch });
    expect(error).toHaveBeenCalled();
  });
});

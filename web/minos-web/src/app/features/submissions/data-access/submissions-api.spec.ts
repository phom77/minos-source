import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../../core/auth/session.service';
import { API_CONFIGURATION } from '../../../core/config/api.config';
import { MINOS_API_REQUEST } from '../../../core/http/api-url.interceptor';
import { csrfInterceptor } from '../../../core/http/csrf.interceptor';
import { SESSION_REQUIRED, sessionErrorInterceptor } from '../../../core/http/session-error.interceptor';
import { SubmissionsApi } from './submissions-api';
import { account, completed, history, submission } from '../testing/submission-fixtures';
import { isSubmission, outcomeLabel, sourceBytes, sourceError, verdicts } from './submission.models';

describe('submission API boundaries', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([csrfInterceptor, sessionErrorInterceptor])), provideHttpClientTesting(),
      { provide: API_CONFIGURATION, useValue: { apiBaseUrl: '', production: false } }] });
    TestBed.inject(SessionService).signedIn(account); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('sends the bounded source and fixed language through cookie/CSRF protection only once, even with multiple subscribers', () => {
    const api = TestBed.inject(SubmissionsApi), result = vi.fn(), request = api.create(submission.problemId, submission.source);
    request.subscribe(result); request.subscribe(result);
    http.expectOne('/accounts/csrf').flush({ requestToken: 'test-only-csrf' });
    const post = http.expectOne('/api/problems/' + submission.problemId + '/submissions');
    expect(post.request.body).toEqual({ language: 'cpp20', source: submission.source });
    expect(post.request.headers.get('X-CSRF-TOKEN')).toBe('test-only-csrf');
    expect(post.request.context.get(SESSION_REQUIRED)).toBe(true);
    post.flush(submission); expect(result).toHaveBeenCalledTimes(2);
  });
  it('finishes an admitted write after view unsubscribe and never automatically repeats failed writes', () => {
    const write = TestBed.inject(SubmissionsApi).create(submission.problemId, submission.source).subscribe({ error: () => {} });
    http.expectOne('/accounts/csrf').flush({ requestToken: 'csrf' });
    const post = http.expectOne('/api/problems/' + submission.problemId + '/submissions'); write.unsubscribe();
    expect(post.cancelled).toBe(false); post.flush({}, { status: 503, statusText: 'Unavailable' });
    http.expectNone(request => request.method === 'POST');
  });
  it('reads owner history without source and validates pagination', () => {
    const result = vi.fn(); TestBed.inject(SubmissionsApi).list(2, 10).subscribe(result);
    const get = http.expectOne('/api/me/submissions?page=2&pageSize=10');
    expect(get.request.context.get(MINOS_API_REQUEST)).toBe(true); expect(get.request.context.get(SESSION_REQUIRED)).toBe(true);
    expect(get.request.headers.has('X-CSRF-TOKEN')).toBe(false); get.flush(history(2, 10));
    expect(result.mock.calls[0][0].items[0].source).toBeUndefined();
  });
  it('rejects a different submission identity', () => {
    const error = vi.fn(); TestBed.inject(SubmissionsApi).detail(submission.id).subscribe({ error });
    http.expectOne('/api/submissions/' + submission.id).flush({ ...submission, id: submission.problemId }); expect(error).toHaveBeenCalled();
  });
  it('requests problem-scoped history and rejects rows belonging to another problem', () => {
    const result = vi.fn(), error = vi.fn(), api = TestBed.inject(SubmissionsApi);
    api.list(1, 20, submission.problemId).subscribe(result);
    http.expectOne('/api/me/submissions?page=1&pageSize=20&problemId=' + submission.problemId).flush(history());
    expect(result).toHaveBeenCalled();
    api.list(1, 20, submission.problemId).subscribe({ error });
    http.expectOne('/api/me/submissions?page=1&pageSize=20&problemId=' + submission.problemId).flush({ ...history(), items: [{ ...history().items[0], problemId: submission.id }] });
    expect(error).toHaveBeenCalled();
  });
  it.each([{ total: -1 }, { page: 2 }, { items: [{ ...history().items[0], verdict: 'Invented' }] }])('rejects malformed history %j', patch => {
    const error = vi.fn(); TestBed.inject(SubmissionsApi).list(1, 20).subscribe({ error });
    http.expectOne('/api/me/submissions?page=1&pageSize=20').flush({ ...history(), ...patch }); expect(error).toHaveBeenCalled();
  });
  it('expires a protected 401 but preserves session on permission and infrastructure failures', () => {
    const api = TestBed.inject(SubmissionsApi), session = TestBed.inject(SessionService);
    for (const status of [403, 503, 401]) {
      api.detail(submission.id).subscribe({ error: () => {} });
      http.expectOne('/api/submissions/' + submission.id).flush({}, { status, statusText: 'Error' });
      expect(session.state().status).toBe(status === 401 ? 'guest' : 'authenticated');
    }
  });
});
describe('source and judging semantics', () => {
  it('counts UTF-8 bytes, rejecting empty, null and excessive source without trimming submitted code', () => {
    expect(sourceBytes('é')).toBe(2); expect(sourceBytes('😀')).toBe(4);
    expect(sourceError(' \n')).toContain('Enter'); expect(sourceError('int\0')).toContain('null');
    expect(sourceError('é'.repeat(32768))).toBeNull(); expect(sourceError('é'.repeat(32769))).toContain('65,536');
  });
  it('accepts legacy completion without inventing an Accepted verdict', () => {
    expect(isSubmission({ ...completed, result: null, configuration: null })).toBe(true);
    expect(outcomeLabel('Completed', null)).toBe('Completed'); expect(outcomeLabel('SystemFailed', null)).toBe('Judging unavailable');
    for (const verdict of verdicts) expect(outcomeLabel('Completed', verdict)).toBeTruthy();
  });
  it.each([{ result: { ...completed.result!, tests: [{ ...completed.result!.tests[0], cpuTimeMilliseconds: -1 }] } },
    { source: 'x'.repeat(65537) }, { configuration: {} }, { result: { ...completed.result!, compilerDiagnostics: 'x'.repeat(32769) } }])('rejects malformed bounded evidence', patch => {
    expect(isSubmission({ ...completed, ...patch })).toBe(false);
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { SessionService } from '../../core/auth/session.service';
import { SubmissionDetailPage } from './submission-detail/submission-detail-page';
import { SubmissionHistoryPage } from './submission-history/submission-history-page';
import { ProblemDetailPage } from '../problems/problem-detail/problem-detail-page';
import { publishedProblem } from '../problems/testing/problem-fixtures';
import { account, completed, history, submission } from './testing/submission-fixtures';
import { SubmissionDraft } from './data-access/submission-draft';
import { verdicts } from './data-access/submission.models';

describe('owner submission journeys', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([
      { path: 'submissions/:submissionId', component: SubmissionDetailPage }, { path: 'submissions', component: SubmissionHistoryPage },
      { path: 'problems/:problemId', component: ProblemDetailPage },
    ])] });
    TestBed.inject(SessionService).signedIn(account); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.match(request => request.url.endsWith('/comments')).forEach(request => { if (!request.cancelled) request.flush({ items: [], nextCursor: null }); });
    http.verify({ ignoreCancelled: true });
  });
  async function render(harness: RouterTestingHarness) { harness.detectChanges(); await harness.fixture.whenStable(); harness.detectChanges(); }
  function label() { http.expectOne('/api/problems/versions/' + publishedProblem.versionId).flush(publishedProblem); }
  async function detail(data = completed) {
    const harness = await RouterTestingHarness.create('/submissions/' + data.id); await render(harness);
    http.expectOne('/api/submissions/' + data.id).flush(data); label(); await render(harness); return harness;
  }
  it('restores keyboard focus to the result after an asynchronous refresh', async () => {
    const harness = await detail();
    Array.from(harness.routeNativeElement!.querySelectorAll('button')).find(button => button.textContent === 'Refresh')!.click();
    await render(harness);
    http.expectOne('/api/submissions/' + completed.id).flush(completed);
    await render(harness);
    expect(document.activeElement?.id).toBe('result-heading');
  });
  it('restores keyboard focus to history after an asynchronous refresh', async () => {
    const harness = await RouterTestingHarness.create('/submissions'); await render(harness);
    http.expectOne('/api/me/submissions?page=1&pageSize=20').flush(history()); label(); await render(harness);
    Array.from(harness.routeNativeElement!.querySelectorAll('button')).find(button => button.textContent === 'Refresh')!.click();
    await render(harness);
    http.expectOne('/api/me/submissions?page=1&pageSize=20').flush(history()); await render(harness);
    expect(document.activeElement?.id).toBe('history-heading');
  });
  it.each(verdicts)('renders the real %s verdict without hidden artifacts, paths or identifiers', async verdict => {
    const harness = await detail({ ...completed, result: { ...completed.result!, verdict } }); const element = harness.routeNativeElement!;
    expect(element.querySelector('h1')?.textContent).toBe(publishedProblem.title);
    expect(element.textContent).not.toContain(submission.id); expect(element.textContent).not.toContain(submission.problemVersionId);
    expect(element.textContent).not.toMatch(/Version \d/);
    expect(element.textContent).not.toContain('gcc-cpp20-pinned');
    expect(element.querySelector('.result-panel')?.textContent).not.toMatch(/CPU|wall time|peak memory|MiB|Limits:|\d+ ms/i);
    expect(element.querySelector('.performance-panel') !== null).toBe(verdict !== 'CompilationError');
    expect(element.querySelector('[aria-label="Submitted C++20 source code"] code')?.textContent).toBe(submission.source);
    expect(element.querySelector('a.button')?.getAttribute('href')).toBe('/problems/versions/' + submission.problemVersionId);
    expect(element.querySelector('table')).toBeNull();
    expect(element.querySelector('.test-summary')?.textContent?.trim()).toBe(verdict === 'CompilationError' ? undefined : '2 / 2 tests passed');
    expect(element.querySelector('.submitted-source .hljs-type')?.textContent).toBe('int');
  });
  it('renders diagnostics as plaintext and opens compiler errors without implying tests ran', async () => {
    const harness = await detail({ ...completed, result: { ...completed.result!, verdict: 'CompilationError', compilerDiagnostics: '<script>private code</script>\nerror: missing ;',
      tests: [{ order: 1, status: 'NotExecuted', cpuTimeMilliseconds: null, wallTimeMilliseconds: null, memoryPeakBytes: null }],
      maximumCpuTimeMilliseconds: null, maximumWallTimeMilliseconds: null, maximumMemoryBytes: null } });
    const element = harness.routeNativeElement!; expect(element.querySelector('details')?.open).toBe(true); expect(element.querySelector('pre script')).toBeNull();
    expect(element.querySelector('pre code')?.textContent).toContain('<script>');
    expect(element.querySelector('.test-summary')).toBeNull(); expect(element.querySelector('table')).toBeNull();
    expect(element.textContent).not.toContain('0 ms');
  });
  it('keeps historical unexecuted tests distinct in the passed/total summary', async () => {
    const harness = await detail({ ...completed, result: { ...completed.result!, verdict: 'WrongAnswer',
      tests: [completed.result!.tests[0], { order: 2, status: 'WrongAnswer', cpuTimeMilliseconds: 2, wallTimeMilliseconds: 3, memoryPeakBytes: 1024 },
        { order: 3, status: 'NotExecuted', cpuTimeMilliseconds: null, wallTimeMilliseconds: null, memoryPeakBytes: null }] } });
    expect(harness.routeNativeElement!.querySelector('.test-summary')?.textContent).toContain('1 / 3 tests passed · 1 not run');
    expect(harness.routeNativeElement!.querySelector('table')).toBeNull();
  });
  it.each(['Completed', 'SystemFailed'] as const)('preserves %s without inventing a verdict', async status => {
    const harness = await detail({ ...submission, status });
    expect(harness.routeNativeElement?.textContent).not.toContain('Accepted');
    expect(harness.routeNativeElement?.textContent).toContain(status === 'Completed' ? 'no recorded verdict' : 'No verdict was assigned');
  });
  it('offers editing in the current problem with exact source held in account memory', async () => {
    const harness = await detail();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    Array.from(harness.routeNativeElement!.querySelectorAll('button')).find(button => button.textContent?.includes('Edit &'))!.click();
    expect(TestBed.inject(SubmissionDraft).read(submission.problemId)).toBe(submission.source);
    expect(navigate).toHaveBeenCalledWith(['/problems', submission.problemId], { fragment: 'code-heading' });
  });
  it('renders pending without an edit action and cancels private reads on navigation or identity change', async () => {
    const harness = await RouterTestingHarness.create('/submissions/' + submission.id); await render(harness);
    const pending = http.expectOne('/api/submissions/' + submission.id);
    await harness.navigateByUrl('/submissions'); await render(harness); expect(pending.cancelled).toBe(true);
    const historyRequest = http.expectOne('/api/me/submissions?page=1&pageSize=20');
    TestBed.inject(SessionService).signedIn({ ...account, id: 'other' }); await render(harness);
    expect(historyRequest.cancelled).toBe(true);
    http.expectOne('/api/me/submissions?page=1&pageSize=20').flush({ items: [], page: 1, pageSize: 20, total: 0 }); await render(harness);
    expect(harness.routeNativeElement?.textContent).not.toContain(submission.source);
  });
  it.each([['invalid', null], [submission.id, 404], [submission.id, 403], [submission.id, 503]] as const)('handles invalid/private/missing/offline detail %s %s', async (id, status) => {
    const harness = await RouterTestingHarness.create('/submissions/' + id); await render(harness);
    if (status) http.expectOne('/api/submissions/' + id).flush({ detail: 'sensitive trace' }, { status, statusText: 'Error' });
    else http.expectNone(request => request.url.startsWith('/api/submissions/'));
    await render(harness); expect(harness.routeNativeElement?.textContent).not.toContain('sensitive trace');
    expect(harness.routeNativeElement?.textContent).toContain(status === 404 ? 'not found' : status === 403 ? 'private' : status === 503 ? 'Could not load' : 'Invalid');
  });
  it('loads paginated source-free history, enriches repeated titles once and restores page size from the URL', async () => {
    const harness = await RouterTestingHarness.create('/submissions?page=2&pageSize=7'); await render(harness);
    const data = history(2, 7); http.expectOne('/api/me/submissions?page=2&pageSize=7').flush({ ...data, total: 15, items: [data.items[0], { ...data.items[0], id: '55555555-5555-5555-5555-555555555555' }] });
    label(); await render(harness); const element = harness.routeNativeElement!;
    expect(element.querySelector<HTMLSelectElement>('select')?.value).toBe('7'); expect(element.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(element.textContent).not.toContain(submission.source); expect(element.textContent).not.toContain(submission.problemVersionId);
    expect(element.textContent).not.toMatch(/Version \d/);
    expect(element.querySelector('tbody th a')?.textContent).toBe(publishedProblem.title);
    expect(element.querySelector('a[aria-label="Next page"]')?.getAttribute('href')).toBe('/submissions?page=3&pageSize=7');
    const select = element.querySelector('select')!; select.value = '50'; select.dispatchEvent(new Event('change')); await render(harness);
    expect(TestBed.inject(Router).url).toBe('/submissions?page=1&pageSize=50');
    http.expectOne('/api/me/submissions?page=1&pageSize=50').flush({ items: [], page: 1, pageSize: 50, total: 0 }); await render(harness);
    expect(element.textContent).toContain('No submissions yet');
  });
  it('restores the page-size control after its asynchronous page request', async () => {
    const harness = await RouterTestingHarness.create('/submissions'); await render(harness);
    http.expectOne('/api/me/submissions?page=1&pageSize=20').flush(history()); label(); await render(harness);
    const select = harness.routeNativeElement!.querySelector('select')!; select.focus(); select.value = '10'; select.dispatchEvent(new Event('change'));
    await render(harness); http.expectOne('/api/me/submissions?page=1&pageSize=10').flush(history(1, 10)); await render(harness);
    expect(document.activeElement).toBe(harness.routeNativeElement!.querySelector('select'));
  });
  it('restores exact source and focuses the editor after the published problem finishes loading', async () => {
    const harness = await detail();
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('.page-heading button')!.click(); await render(harness);
    http.expectOne('/api/problems/' + submission.problemId).flush(publishedProblem); await render(harness);
    const source = harness.routeNativeElement!.querySelector<HTMLTextAreaElement>('#submission-source')!;
    expect(source.value).toBe(submission.source); expect(document.activeElement).toBe(source);
  });
  it('expands and restores the editing workspace without losing source or editor focus', async () => {
    const harness = await RouterTestingHarness.create('/problems/' + submission.problemId); await render(harness);
    http.expectOne('/api/problems/' + submission.problemId).flush(publishedProblem); await render(harness);
    const source = harness.routeNativeElement!.querySelector<HTMLTextAreaElement>('#submission-source')!;
    source.value = submission.source; source.dispatchEvent(new Event('input')); await render(harness);
    harness.routeNativeElement!.querySelector<HTMLAnchorElement>('.code-jump')!.click(); await render(harness);
    expect(document.activeElement).toBe(source); expect(TestBed.inject(Router).url).toBe('/problems/' + submission.problemId);
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('[aria-label="Expand editor"]')!.click(); await render(harness);
    expect(harness.routeNativeElement!.querySelector('.solve-layout--expanded')).not.toBeNull();
    expect(harness.routeNativeElement!.querySelector('#submission-source')).toBe(source); expect(source.value).toBe(submission.source); expect(document.activeElement).toBe(source);
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('[aria-label="Restore split view"]')!.click(); await render(harness);
    expect(harness.routeNativeElement!.querySelector('.solve-layout--expanded')).toBeNull(); expect(source.value).toBe(submission.source);
    http.expectNone(request => request.method === 'POST');
  });
  it('preserves terminal history across auth query changes and uses a safe fallback if a problem is archived', async () => {
    const harness = await RouterTestingHarness.create('/submissions'); await render(harness);
    http.expectOne('/api/me/submissions?page=1&pageSize=20').flush(history());
    http.expectOne('/api/problems/versions/' + submission.problemVersionId).flush({}, { status: 404, statusText: 'Archived' }); await render(harness);
    expect(harness.routeNativeElement?.textContent).toContain('Unavailable problem');
    await harness.navigateByUrl('/submissions?auth=login'); await render(harness);
    http.expectNone(request => request.url === '/api/me/submissions');
    TestBed.inject(SessionService).signedOut('expired'); await render(harness);
    expect(harness.routeNativeElement?.textContent).not.toContain('Unavailable problem'); expect(harness.routeNativeElement?.textContent).toContain('Sign in');
  });
  it.each(['/submissions?page=0', '/submissions?page=1&page=2', '/submissions?pageSize=101'])('rejects malformed history URL %s before HTTP', async url => {
    const harness = await RouterTestingHarness.create(url); await render(harness); expect(harness.routeNativeElement?.textContent).toContain('Invalid history');
    http.expectNone(request => request.url === '/api/me/submissions');
  });
  it('distinguishes empty and beyond-last-page history with a useful recovery link', async () => {
    const harness = await RouterTestingHarness.create('/submissions?page=3'); await render(harness);
    http.expectOne('/api/me/submissions?page=3&pageSize=20').flush({ page: 3, pageSize: 20, total: 1, items: [] }); await render(harness);
    expect(harness.routeNativeElement?.textContent).toContain('No submissions on this page');
    expect(harness.routeNativeElement?.querySelector('minos-state-panel a')?.getAttribute('href')).toBe('/submissions?page=1&pageSize=20');
  });
});

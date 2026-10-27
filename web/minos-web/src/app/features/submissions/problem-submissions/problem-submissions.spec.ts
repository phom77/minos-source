import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { SessionService } from '../../../core/auth/session.service';
import { ProblemDetailPage } from '../../problems/problem-detail/problem-detail-page';
import { publishedProblem } from '../../problems/testing/problem-fixtures';
import { account, completed, history, submission } from '../testing/submission-fixtures';
import { ProblemSubmissions } from './problem-submissions';

describe('submissions inside the problem workspace', () => {
  let http: HttpTestingController;
  const path = '/problems/' + publishedProblem.problemId;
  const listUrl = (page = 1) => `/api/me/submissions?page=${page}&pageSize=20&problemId=${publishedProblem.problemId}`;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([
      { path: 'problems/:problemId', component: ProblemDetailPage },
    ])] });
    TestBed.inject(SessionService).signedIn(account); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.match(request => request.url.endsWith('/comments')).forEach(request => { if (!request.cancelled) request.flush({ items: [], nextCursor: null }); });
    http.verify({ ignoreCancelled: true });
  });
  async function render(harness: RouterTestingHarness) {
    harness.detectChanges(); await harness.fixture.whenStable(); harness.detectChanges();
  }
  async function open(query = '') {
    const harness = await RouterTestingHarness.create(path + query); await render(harness);
    http.expectOne('/api/problems/' + publishedProblem.problemId).flush(publishedProblem); await render(harness); return harness;
  }
  const component = (harness: RouterTestingHarness) => harness.routeDebugElement!.query(By.directive(ProblemSubmissions)).componentInstance as ProblemSubmissions;

  it('keeps the editor mounted while opening a filtered list and highlighted owner detail in the problem', async () => {
    const harness = await open(), element = harness.routeNativeElement!;
    const source = element.querySelector<HTMLTextAreaElement>('#submission-source')!; source.value = '// unsent draft'; source.dispatchEvent(new Event('input')); await render(harness);
    element.querySelector<HTMLAnchorElement>('#problem-tab-submissions')!.click(); await render(harness);
    expect(TestBed.inject(Router).url).toBe(path + '?tab=submissions');
    http.expectOne(listUrl()).flush(history()); await render(harness);
    expect(element.querySelector('#problem-description-panel')?.getAttribute('hidden')).not.toBeNull();
    expect(element.querySelector('#submission-source')).toBe(source); expect(source.value).toBe('// unsent draft');
    expect(element.querySelector('.submission-list')?.textContent).toContain('Accepted');
    element.querySelector<HTMLButtonElement>('.submission-select')!.click(); await render(harness);
    http.expectOne('/api/submissions/' + submission.id).flush(completed); await render(harness);
    expect(TestBed.inject(Router).url).toContain('tab=submissions&submission=' + submission.id);
    expect(element.querySelector('.source-view code')?.textContent).toBe(submission.source);
    expect(element.querySelector('.source-view .hljs-type')?.textContent).toBe('int');
    expect(source.value).toBe('// unsent draft');
    expect(document.activeElement?.id).toBe('problem-submission-heading');
    element.querySelector<HTMLButtonElement>('.back-to-list')!.click(); await render(harness);
    expect(element.querySelector('.submission-list')).not.toBeNull();
    expect(document.activeElement?.id).toBe('problem-submissions-heading');
    element.querySelector<HTMLAnchorElement>('#problem-tab-description')!.click(); await render(harness);
    expect(TestBed.inject(Router).url).toBe(path);
    expect(element.querySelector('#submission-source')).toBe(source); expect(source.value).toBe('// unsent draft');
    http.expectNone(request => request.url === '/api/submissions' || request.url === '/api/problems/' + publishedProblem.problemId);
  });

  it('uses server-filtered totals and pagination without navigating away or fetching other problem titles', async () => {
    const harness = await open('?tab=submissions');
    http.expectOne(listUrl()).flush({ ...history(), total: 21 }); await render(harness);
    const buttons = () => Array.from(harness.routeNativeElement!.querySelectorAll<HTMLButtonElement>('.pagination button'));
    buttons().find(button => button.textContent === 'Next')!.click(); await render(harness);
    http.expectOne(listUrl(2)).flush({ ...history(2, 20), total: 21 }); await render(harness);
    expect(TestBed.inject(Router).url).toContain('submissionPage=2');
    expect(harness.routeNativeElement!.querySelector('.pagination')?.textContent).toContain('Page 2 of 2');
    expect(buttons().find(button => button.textContent === 'Next')!.disabled).toBe(true);
  });

  it('supports keyboard tabs and stops private reads when leaving the submissions pane', async () => {
    const harness = await open();
    harness.routeNativeElement!.querySelector('#problem-tab-description')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    await render(harness); const pending = http.expectOne(listUrl());
    expect(document.activeElement?.id).toBe('problem-tab-submissions');
    harness.routeNativeElement!.querySelector('#problem-tab-submissions')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
    await render(harness); expect(pending.cancelled).toBe(true);
    expect(document.activeElement?.id).toBe('problem-tab-description');
  });

  it('synchronously hides private source on identity changes and cancels the prior requests', async () => {
    const harness = await open('?tab=submissions&submission=' + submission.id);
    http.expectOne(listUrl()).flush(history());
    http.expectOne('/api/submissions/' + submission.id).flush(completed); await render(harness);
    const pane = component(harness); expect(pane.detail().status).toBe('ready');
    pane.refreshDetail(); await render(harness); const pending = http.expectOne('/api/submissions/' + submission.id);
    TestBed.inject(SessionService).signedIn({ ...account, id: 'another-owner' });
    expect(pane.detail().status).toBe('loading');
    await render(harness); expect(pending.cancelled).toBe(true);
    http.expectOne(listUrl()).flush({ items: [], page: 1, pageSize: 20, total: 0 });
    http.expectOne('/api/submissions/' + submission.id).flush({}, { status: 403, statusText: 'Denied' }); await render(harness);
    expect(harness.routeNativeElement!.textContent).not.toContain(submission.source);
    expect(harness.routeNativeElement!.textContent).toContain('This submission is private');
  });

  it('does not present another problem’s detail even when it belongs to the same account', async () => {
    const harness = await open('?tab=submissions&submission=' + submission.id);
    http.expectOne(listUrl()).flush(history());
    http.expectOne('/api/submissions/' + submission.id).flush({ ...completed, problemId: '99999999-9999-4999-8999-999999999999' }); await render(harness);
    expect(harness.routeNativeElement!.textContent).toContain('Submission not found for this problem');
    expect(harness.routeNativeElement!.querySelector('.source-view')).toBeNull();
  });

  it('offers guest sign-in without reading private history and retains the problem URL', async () => {
    TestBed.inject(SessionService).signedOut();
    const harness = await open('?tab=submissions');
    expect(harness.routeNativeElement!.textContent).toContain('Sign in to view your submissions');
    http.expectNone(request => request.url === '/api/me/submissions');
    expect(TestBed.inject(Router).url).toBe(path + '?tab=submissions');
  });

  it('shows empty history and rejects invalid page URLs before a private history request', async () => {
    const harness = await open('?tab=submissions');
    http.expectOne(listUrl()).flush({ items: [], page: 1, pageSize: 20, total: 0 }); await render(harness);
    expect(harness.routeNativeElement!.textContent).toContain('No submissions for this problem yet');
    await harness.navigateByUrl(path + '?tab=submissions&submissionPage=0'); await render(harness);
    expect(harness.routeNativeElement!.textContent).toContain('Invalid submission page');
    http.expectNone(request => request.url === '/api/me/submissions');
  });
});

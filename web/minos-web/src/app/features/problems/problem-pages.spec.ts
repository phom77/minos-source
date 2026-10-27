import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Title } from '@angular/platform-browser';

import { ProblemCatalogPage } from './problem-catalog-page';
import { ProblemDetailPage } from './problem-detail/problem-detail-page';
import { publishedProblem as problem } from './testing/problem-fixtures';
import { SessionService } from '../../core/auth/session.service';

describe('public problem pages', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([
      { path: 'problems/versions/:versionId', component: ProblemDetailPage, data: { immutable: true } },
      { path: 'problems/:problemId', component: ProblemDetailPage },
      { path: 'problems', component: ProblemCatalogPage },
    ])] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.match('/api/problems/topics').forEach(request => request.flush([{ slug: 'math', displayName: 'Math', problemCount: 1 }]));
    http.match(request => request.url.endsWith('/comments')).forEach(request => { if (!request.cancelled) request.flush({ items: [], nextCursor: null }); });
    http.verify();
  });

  it.each([20, 7])('reflects the initial page size %s after asynchronously rendering the catalog', async (pageSize) => {
    const harness = await RouterTestingHarness.create('/problems?pageSize=' + pageSize);
    http.expectOne('/api/problems?page=1&pageSize=' + pageSize).flush({ items: [problem], page: 1, pageSize, total: 1 });
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector<HTMLSelectElement>('select[aria-label="Per page"]')?.value).toBe(String(pageSize));
  });

  it('renders loading, real fields, accessible pagination and page-size URL changes', async () => {
    const harness = await RouterTestingHarness.create('/problems?page=2&pageSize=10');
    expect(harness.routeNativeElement?.textContent).toContain('Loading problems');
    http.expectOne('/api/problems?page=2&pageSize=10').flush({ items: [problem], page: 2, pageSize: 10, total: 31 });
    harness.detectChanges();
    const element = harness.routeNativeElement!;
    expect(element.querySelector('tbody th a')?.getAttribute('href')).toContain('/problems/' + problem.problemId + '?library=');
    expect(element.querySelectorAll('thead th')).toHaveLength(2);
    expect(element.textContent).not.toMatch(/published|version/i);
    expect(element.querySelector('.problem-number')?.textContent).toBe('42.');
    expect(element.querySelector('nav[aria-label="Pagination"]')?.textContent).toContain('Page 2 of 4');
    expect(element.querySelector('a[aria-label="Next page"]')?.getAttribute('href')).toBe('/problems?page=3&pageSize=10');
    expect(element.textContent).toContain('Easy');
    const select = element.querySelector<HTMLSelectElement>('select[aria-label="Per page"]')!;
    expect(select.value).toBe('10');
    select.value = '50'; select.dispatchEvent(new Event('change', { bubbles: true }));
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/problems?page=1&pageSize=50');
    http.expectOne('/api/problems?page=1&pageSize=50').flush({ items: [problem], page: 1, pageSize: 50, total: 1 });
    harness.detectChanges();
    expect(select.value).toBe('50');
    expect(element.querySelectorAll('nav[aria-label="Pagination"] button[disabled]')).toHaveLength(2);
  });

  it('does not refetch for auth query changes and cancels stale page reads', async () => {
    const harness = await RouterTestingHarness.create('/problems');
    const old = http.expectOne('/api/problems?page=1&pageSize=20');
    await harness.navigateByUrl('/problems?auth=login');
    expect(old.cancelled).toBe(false);
    http.expectNone('/api/problems?page=1&pageSize=20');
    await harness.navigateByUrl('/problems?page=2');
    expect(old.cancelled).toBe(true);
    http.expectOne('/api/problems?page=2&pageSize=20').flush({ items: [], page: 2, pageSize: 20, total: 1 });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('There are no problems on this page');
    expect(harness.routeNativeElement?.querySelector('minos-state-panel a')?.getAttribute('href')).toBe('/problems?page=1&pageSize=20');
    await harness.navigateByUrl('/problems');
    http.expectOne('/api/problems?page=1&pageSize=20').flush({ items: [], page: 1, pageSize: 20, total: 0 });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('No problems yet');
  });

  it('rejects invalid catalog links without an HTTP call', async () => {
    const harness = await RouterTestingHarness.create('/problems?page=0');
    expect(harness.routeNativeElement?.textContent).toContain('Check this catalog link');
    http.expectNone(request => request.url === '/api/problems');
  });

  it('offers manual retry and never displays server diagnostics', async () => {
    const harness = await RouterTestingHarness.create('/problems');
    http.expectOne('/api/problems?page=1&pageSize=20').flush({ detail: 'secret database trace' }, { status: 500, statusText: 'Error' });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('library could not be reached');
    expect(harness.routeNativeElement?.textContent).not.toContain('secret database trace');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('minos-state-panel button')!.click();
    http.expectOne('/api/problems?page=1&pageSize=20').flush({ items: [], page: 1, pageSize: 20, total: 0 });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('No problems yet');
  });

  it('reads safe statements and preserves historical access and sample whitespace without internal metadata', async () => {
    const harness = await RouterTestingHarness.create('/problems/versions/' + problem.versionId);
    http.expectOne('/api/problems/versions/' + problem.versionId).flush(problem);
    harness.detectChanges();
    const element = harness.routeNativeElement!;
    expect(element.querySelector('h1')?.textContent).toBe(problem.title);
    expect(element.querySelector('.statement')?.textContent).toContain('<script>alert("untrusted")</script>');
    expect(element.querySelector('.statement script')).toBeNull();
    expect(element.querySelector('pre code')?.textContent).toBe('  1 2\n');
    expect(element.querySelector('pre')?.getAttribute('tabindex')).toBe('0');
    expect(element.textContent).toContain('1000 ms');
    expect(element.textContent).toContain('128 MB');
    expect(element.querySelector('.problem-limits a')?.getAttribute('href')).toBe('/problems/' + problem.problemId);
    expect(element.textContent).not.toMatch(/published|version/i);
    expect(TestBed.inject(Title).getTitle()).toBe(problem.title + ' — Minos');
  });

  it('applies live filter changes, exclusion and match mode while preserving search in the URL', async () => {
    const harness = await RouterTestingHarness.create('/problems?page=3&pageSize=10&q=%2342');
    http.expectOne('/api/problems/topics').flush([{ slug: 'math', displayName: 'Math', problemCount: 1 }]);
    http.expectOne('/api/problems?page=3&pageSize=10&q=%2342').flush({ items: [problem], page: 3, pageSize: 10, total: 31 });
    harness.detectChanges();
    const component = harness.routeDebugElement!.componentInstance as ProblemCatalogPage;
    component.updateFilter('difficulty', 'Easy'); await harness.fixture.whenStable();
    http.expectOne('/api/problems?page=1&pageSize=10&q=%2342&difficulty=Easy').flush({ items: [], page: 1, pageSize: 10, total: 0 });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('No problems match your filters');
    component.updateOperator('difficulty', 'true'); await harness.fixture.whenStable();
    http.expectOne('/api/problems?page=1&pageSize=10&q=%2342&difficulty=Easy&difficultyIsNot=true').flush({ items: [], page: 1, pageSize: 10, total: 0 });
    component.updateMatch('Any'); await harness.fixture.whenStable();
    http.expectOne('/api/problems?page=1&pageSize=10&q=%2342&difficulty=Easy&match=Any&difficultyIsNot=true').flush({ items: [], page: 1, pageSize: 10, total: 0 });
    component.resetFilters(); await harness.fixture.whenStable();
    http.expectOne('/api/problems?page=1&pageSize=10&q=%2342').flush({ items: [problem], page: 1, pageSize: 10, total: 1 });
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector<HTMLInputElement>('[name="q"]')?.value).toBe('#42');
    component.clearFilters(); await harness.fixture.whenStable();
    http.expectOne('/api/problems?page=1&pageSize=10').flush({ items: [problem], page: 1, pageSize: 10, total: 1 });
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector<HTMLInputElement>('[name="q"]')?.value).toBe('');
  });

  it('keeps guest progress unknown and requests sign-in for a personal status filter', async () => {
    const harness = await RouterTestingHarness.create('/problems?status=Solved');
    expect(harness.routeNativeElement?.textContent).toContain('Sign in to filter your progress');
    http.expectNone(request => request.url === '/api/problems');
    expect(harness.routeNativeElement?.querySelector('progress')).toBeNull();
  });

  it('cancels account-owned reads and synchronously hides old progress when identity changes', async () => {
    const session = TestBed.inject(SessionService);
    const account = { id: '33333333-3333-3333-3333-333333333333', displayName: 'First', role: 'Solver' as const, createdAtUtc: problem.publishedAtUtc };
    session.signedIn(account);
    const harness = await RouterTestingHarness.create('/problems');
    http.expectOne('/api/problems?page=1&pageSize=20').flush({ items: [{ ...problem, solveStatus: 'Solved' }], page: 1, pageSize: 20, total: 1,
      progress: { solved: 1, attempted: 1, total: 1 } });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('1 / 1 Solved');
    const component = harness.routeDebugElement!.componentInstance as ProblemCatalogPage;
    session.signedIn({ ...account, id: '44444444-4444-4444-4444-444444444444', displayName: 'Second' });
    expect(component.state().status).toBe('loading');
    harness.detectChanges(); await harness.fixture.whenStable();
    const second = http.expectOne('/api/problems?page=1&pageSize=20');
    session.signedOut(); harness.detectChanges(); await harness.fixture.whenStable();
    expect(second.cancelled).toBe(true);
    http.expectOne('/api/problems?page=1&pageSize=20').flush({ items: [problem], page: 1, pageSize: 20, total: 1, progress: null });
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('progress')).toBeNull();
    expect(harness.routeNativeElement?.querySelector('.status-column')).toBeNull();
  });

  it('returns from solving to the filtered library and rejects external return locations', async () => {
    const library = '/problems?page=2&pageSize=10&difficulty=Hard&topic=math';
    const harness = await RouterTestingHarness.create('/problems/' + problem.problemId + '?library=' + encodeURIComponent(library));
    http.expectOne('/api/problems/' + problem.problemId).flush(problem); harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('.back-link')?.getAttribute('href')).toBe(library);
    await harness.navigateByUrl('/problems/' + problem.problemId + '?library=' + encodeURIComponent('https://other.test'));
    http.expectNone('/api/problems/' + problem.problemId); harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('.back-link')?.getAttribute('href')).toBe('/problems');
  });

  it('reads the current problem without version metadata and handles empty samples', async () => {
    const harness = await RouterTestingHarness.create('/problems/' + problem.problemId);
    http.expectOne('/api/problems/' + problem.problemId).flush({ ...problem, samples: [] }); harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('No public examples');
    expect(harness.routeNativeElement?.querySelector('.problem-limits a')).toBeNull();
    expect(harness.routeNativeElement?.textContent).not.toMatch(/published|version/i);
  });

  it('rejects malformed IDs without HTTP and distinguishes missing from retryable failures', async () => {
    const harness = await RouterTestingHarness.create('/problems/invalid-id');
    expect(harness.routeNativeElement?.textContent).toContain('problem link is invalid');
    expect(harness.routeNativeElement?.querySelector('minos-state-panel h2')?.textContent).toBe('This problem link is invalid.');
    http.expectNone(request => request.url.startsWith('/api/problems'));
    await harness.navigateByUrl('/problems/' + problem.problemId);
    http.expectOne('/api/problems/' + problem.problemId).flush({}, { status: 404, statusText: 'Not found' }); harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('problem is not available');
    await harness.navigateByUrl('/problems/versions/' + problem.versionId);
    http.expectOne('/api/problems/versions/' + problem.versionId).flush({}, { status: 503, statusText: 'Unavailable' }); harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('problem could not be reached');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('button')!.click();
    http.expectOne('/api/problems/versions/' + problem.versionId).flush(problem); harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe(problem.title);
  });
});

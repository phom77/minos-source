import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Title } from '@angular/platform-browser';

import { ProblemCatalogPage } from './problem-catalog-page';
import { ProblemDetailPage } from './problem-detail/problem-detail-page';
import { publishedProblem as problem } from './testing/problem-fixtures';

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
  afterEach(() => http.verify());

  it('renders loading, real fields, accessible pagination and page-size URL changes', async () => {
    const harness = await RouterTestingHarness.create('/problems?page=2&pageSize=10');
    expect(harness.routeNativeElement?.textContent).toContain('Loading problems');
    http.expectOne('/api/problems?page=2&pageSize=10').flush({ items: [problem], page: 2, pageSize: 10, total: 31 });
    harness.detectChanges();
    const element = harness.routeNativeElement!;
    expect(element.querySelector('tbody th a')?.getAttribute('href')).toBe('/problems/' + problem.problemId);
    expect(element.querySelector('.version-link')?.getAttribute('href')).toBe('/problems/versions/' + problem.versionId);
    expect(element.querySelector('tbody td')?.textContent).toBe('11');
    expect(element.querySelector('nav[aria-label="Pagination"]')?.textContent).toContain('Page 2 of 4');
    expect(element.querySelector('a[aria-label="Next page"]')?.getAttribute('href')).toBe('/problems?page=3&pageSize=10');
    expect(element.textContent).not.toContain('Difficulty');
    const select = element.querySelector('select')!;
    select.value = '50'; select.dispatchEvent(new Event('change', { bubbles: true }));
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/problems?page=1&pageSize=50');
    http.expectOne('/api/problems?page=1&pageSize=50').flush({ items: [problem], page: 1, pageSize: 50, total: 1 });
    harness.detectChanges();
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
    expect(harness.routeNativeElement?.querySelector('#problem-library a')?.getAttribute('href')).toBe('/problems?page=1&pageSize=20');
    await harness.navigateByUrl('/problems');
    http.expectOne('/api/problems?page=1&pageSize=20').flush({ items: [], page: 1, pageSize: 20, total: 0 });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('waiting for its first problem');
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
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('#problem-library button')!.click();
    http.expectOne('/api/problems?page=1&pageSize=20').flush({ items: [], page: 1, pageSize: 20, total: 0 });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('waiting for its first problem');
  });

  it('reads safe plaintext statements and preserves sample whitespace in the immutable route', async () => {
    const harness = await RouterTestingHarness.create('/problems/versions/' + problem.versionId);
    http.expectOne('/api/problems/versions/' + problem.versionId).flush(problem);
    harness.detectChanges();
    const element = harness.routeNativeElement!;
    expect(element.querySelector('h1')?.textContent).toBe(problem.title);
    expect(element.querySelector('.statement')?.textContent).toBe(problem.statement);
    expect(element.querySelector('.statement script')).toBeNull();
    expect(element.querySelector('pre code')?.textContent).toBe('  1 2\n');
    expect(element.querySelector('pre')?.getAttribute('tabindex')).toBe('0');
    expect(element.textContent).toContain('1000 ms');
    expect(element.textContent).toContain('128 MB');
    expect(element.querySelector('.version-navigation a')?.getAttribute('href')).toBe('/problems/' + problem.problemId);
    expect(TestBed.inject(Title).getTitle()).toBe(problem.title + ' — Minos');
  });

  it('reads latest versions, handles empty samples, and offers immutable links', async () => {
    const harness = await RouterTestingHarness.create('/problems/' + problem.problemId);
    http.expectOne('/api/problems/' + problem.problemId).flush({ ...problem, samples: [] }); harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('No samples were supplied');
    expect(harness.routeNativeElement?.querySelector('.version-navigation a')?.getAttribute('href')).toBe('/problems/versions/' + problem.versionId);
  });

  it('rejects malformed IDs without HTTP and distinguishes missing from retryable failures', async () => {
    const harness = await RouterTestingHarness.create('/problems/invalid-id');
    expect(harness.routeNativeElement?.textContent).toContain('problem link is invalid');
    expect(harness.routeNativeElement?.querySelector('minos-state-panel h2')?.textContent).toBe('This problem link is invalid.');
    http.expectNone(request => request.url.startsWith('/api/problems'));
    await harness.navigateByUrl('/problems/' + problem.problemId);
    http.expectOne('/api/problems/' + problem.problemId).flush({}, { status: 404, statusText: 'Not found' }); harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('published problem is not available');
    await harness.navigateByUrl('/problems/versions/' + problem.versionId);
    http.expectOne('/api/problems/versions/' + problem.versionId).flush({}, { status: 503, statusText: 'Unavailable' }); harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('problem could not be reached');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('button')!.click();
    http.expectOne('/api/problems/versions/' + problem.versionId).flush(problem); harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe(problem.title);
  });
});

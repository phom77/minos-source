import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../../core/auth/session.service';
import { account, completed } from '../testing/submission-fixtures';
import { isSubmissionPerformance, SubmissionPerformance } from '../data-access/submission-performance.models';
import { SubmissionPerformanceView } from './submission-performance';

export const readyPerformance: SubmissionPerformance = {
  submissionId: completed.id, status: 'Ready', sampleCount: 20, minimumSampleCount: 20,
  runtime: { value: 12, beatsPercentage: 50, equalCount: 5, rangeMaximum: 101,
    buckets: Array.from({ length: 20 }, (_, index) => ({ index, count: index === 1 ? 5 : index === 2 ? 5 : index === 19 ? 10 : 0 })) },
  memory: { value: 2097152, beatsPercentage: 0, equalCount: 20, rangeMaximum: 2097153,
    buckets: Array.from({ length: 20 }, (_, index) => ({ index, count: index === 19 ? 20 : 0 })) },
};

describe('submission performance', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).signedIn(account); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify({ ignoreCancelled: true }));
  async function render(eligible = true) {
    const fixture = TestBed.createComponent(SubmissionPerformanceView);
    fixture.componentRef.setInput('submission', { ...completed, result: { ...completed.result!, comparisonEligible: eligible } });
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); return fixture;
  }
  it('reads both distributions once and supports keyboard switching without extra HTTP', async () => {
    const fixture = await render();
    http.expectOne(`/api/submissions/${completed.id}/performance`).flush(readyPerformance);
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Beats 50.00%');
    expect(fixture.nativeElement.textContent).toContain('Beats 0.00%');
    expect(fixture.nativeElement.querySelectorAll('.chart-bar')).toHaveLength(20);
    const runtime = fixture.nativeElement.querySelector('[data-metric="runtime"]') as HTMLButtonElement;
    runtime.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true })); fixture.detectChanges();
    expect(fixture.componentInstance.metric()).toBe('memory');
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('[data-metric="memory"]'));
    expect(fixture.nativeElement.querySelector('.distribution figcaption').textContent).toContain('20 equal');
    fixture.componentRef.setInput('submission', { ...completed, result: { ...completed.result!, comparisonEligible: true } });
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    http.expectNone(request => request.url.endsWith('/performance'));
  });
  it('keeps historical measurements visible without requesting an ineligible comparison', async () => {
    const fixture = await render(false);
    expect(fixture.nativeElement.textContent).toContain('12 ms');
    expect(fixture.nativeElement.textContent).toContain('2.00 MiB');
    expect(fixture.nativeElement.textContent).toContain('historical measurement');
    expect(fixture.nativeElement.querySelector('.chart-plot')).toBeNull();
    http.expectNone(request => request.url.endsWith('/performance'));
  });
  it('does not fabricate percentiles for a small sample and keeps real metrics when comparison fails', async () => {
    const fixture = await render();
    http.expectOne(`/api/submissions/${completed.id}/performance`).flush({ ...readyPerformance, status: 'InsufficientSamples', sampleCount: 3,
      runtime: { value: 12, beatsPercentage: null, equalCount: null, rangeMaximum: 0, buckets: [] },
      memory: { value: 2097152, beatsPercentage: null, equalCount: null, rangeMaximum: 0, buckets: [] } });
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('3 / 20 other accounts');
    expect(fixture.nativeElement.querySelector('.metric-beats, .chart-plot')).toBeNull();
    fixture.componentInstance.refresh(); await fixture.whenStable();
    http.expectOne(`/api/submissions/${completed.id}/performance`).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Comparison unavailable');
    expect(fixture.nativeElement.textContent).toContain('12 ms');
  });
  it('hides the old account comparison synchronously and cancels reads on identity change', async () => {
    const fixture = await render();
    const first = http.expectOne(`/api/submissions/${completed.id}/performance`);
    TestBed.inject(SessionService).signedIn({ ...account, id: 'second-owner' });
    expect(fixture.componentInstance.comparison()).toBeNull();
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(first.cancelled).toBe(true);
    http.expectOne(`/api/submissions/${completed.id}/performance`).flush(readyPerformance);
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.componentInstance.comparison()).not.toBeNull();
    TestBed.inject(SessionService).signedOut();
    expect(fixture.componentInstance.comparison()).toBeNull();
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.metric-beats, .chart-plot')).toBeNull();
  });
  it('does not show runtime/memory for compilation errors even if a malformed fixture supplies numbers', async () => {
    const fixture = await render(false);
    fixture.componentRef.setInput('submission', { ...completed, result: { ...completed.result!, verdict: 'CompilationError' } });
    fixture.detectChanges(); expect(fixture.nativeElement.querySelector('.performance-panel')).toBeNull();
  });
});

describe('performance response validation', () => {
  it('validates identity, threshold, bins and totals before displaying a percentile', () => {
    expect(isSubmissionPerformance(readyPerformance, completed.id)).toBe(true);
    for (const invalid of [ { ...readyPerformance, submissionId: 'different' }, { ...readyPerformance, sampleCount: 0 },
      { ...readyPerformance, runtime: { ...readyPerformance.runtime!, beatsPercentage: 101 } },
      { ...readyPerformance, runtime: { ...readyPerformance.runtime!, equalCount: 21 } },
      { ...readyPerformance, runtime: { ...readyPerformance.runtime!, buckets: [{ index: 0, count: -1 }] } },
      { ...readyPerformance, runtime: { ...readyPerformance.runtime!, buckets: readyPerformance.runtime!.buckets.map(bucket => ({ ...bucket, count: 0 })) } },
      { ...readyPerformance, runtime: { ...readyPerformance.runtime!, rangeMaximum: 0 } } ])
      expect(isSubmissionPerformance(invalid, completed.id)).toBe(false);
  });
});

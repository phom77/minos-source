import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { catchError, combineLatest, distinctUntilChanged, map, of, startWith, Subject, switchMap } from 'rxjs';
import { SessionService } from '../../../core/auth/session.service';
import { SubmissionsApi } from '../data-access/submissions-api';
import { Submission } from '../data-access/submission.models';
import { PerformanceMetric, SubmissionPerformance } from '../data-access/submission-performance.models';

type ComparisonState = { readonly status: 'idle' | 'loading' | 'error' } |
  { readonly status: 'ready'; readonly owner: string; readonly id: string; readonly data: SubmissionPerformance };

@Component({ selector: 'minos-submission-performance', imports: [DecimalPipe], changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './submission-performance.html', styleUrl: './submission-performance.css' })
export class SubmissionPerformanceView {
  readonly submission = input.required<Submission>();
  readonly metric = signal<'runtime' | 'memory'>('runtime');
  private readonly api = inject(SubmissionsApi);
  private readonly session = inject(SessionService);
  private readonly reload = new Subject<void>();
  readonly measurements = computed(() => {
    const data = this.submission();
    return data.status === 'Completed' && data.result?.verdict && data.result.verdict !== 'CompilationError' && data.result.maximumCpuTimeMilliseconds !== null && data.result.maximumMemoryBytes !== null
      ? { runtime: data.result.maximumCpuTimeMilliseconds, memory: data.result.maximumMemoryBytes } : null;
  });
  private readonly target = computed(() => {
    const data = this.submission(), owner = this.session.account()?.id;
    return owner && data.status === 'Completed' && data.result?.verdict === 'Accepted' && data.result.comparisonEligible
      ? { id: data.id, owner } : null;
  });
  private readonly readState = toSignal(combineLatest([toObservable(this.target).pipe(distinctUntilChanged((a, b) => a?.id === b?.id && a?.owner === b?.owner)),
    this.reload.pipe(startWith(undefined))]).pipe(switchMap(([target]) => !target ? of<ComparisonState>({ status: 'idle' }) :
      this.api.performance(target.id).pipe(map((data): ComparisonState => ({ status: 'ready', ...target, data })),
        catchError(() => of<ComparisonState>({ status: 'error' })), startWith<ComparisonState>({ status: 'loading' })))),
    { initialValue: { status: 'idle' } as ComparisonState });
  readonly comparison = computed(() => {
    const state = this.readState(), target = this.target();
    return state.status === 'ready' && target && state.owner === target.owner && state.id === target.id ? state.data : null;
  });
  readonly loading = computed(() => this.target() !== null && this.readState().status === 'loading');
  readonly failed = computed(() => this.target() !== null && this.readState().status === 'error');
  readonly chart = computed(() => {
    const data = this.comparison(); return data?.status === 'Ready' ? data[this.metric()] : null;
  });
  readonly maximumCount = computed(() => Math.max(1, ...this.chart()?.buckets.map(bucket => bucket.count) ?? []));
  readonly marker = computed(() => {
    const chart = this.chart(); return chart ? Math.min(99, Math.max(1, chart.value / chart.rangeMaximum * 100)) : 0;
  });
  refresh(): void { this.reload.next(); }
  tabId(metric: 'runtime' | 'memory'): string { return 'performance-' + this.submission().id + '-' + metric; }
  panelId(): string { return 'performance-' + this.submission().id + '-panel'; }
  percentage(metric: 'runtime' | 'memory'): number | null { return this.comparison()?.[metric]?.beatsPercentage ?? null; }
  label(value: number, metric = this.metric()): string {
    return metric === 'runtime' ? `${value.toLocaleString('en-US', { maximumFractionDigits: 2 })} ms`
      : `${(value / 1048576).toLocaleString('en-US', { maximumFractionDigits: 2 })} MiB`;
  }
  bucketLabel(bucket: { index: number; count: number }, chart: PerformanceMetric): string {
    return `${this.label(bucket.index * chart.rangeMaximum / 20)} – ${this.label((bucket.index + 1) * chart.rangeMaximum / 20)}: ${bucket.count} accounts`;
  }
  metricKey(event: KeyboardEvent): void {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    this.metric.set(event.key === 'Home' ? 'runtime' : event.key === 'End' ? 'memory' : this.metric() === 'runtime' ? 'memory' : 'runtime');
    const target = event.currentTarget as HTMLElement;
    target.querySelector<HTMLButtonElement>(`[data-metric="${this.metric()}"]`)?.focus();
  }
}

import { DatePipe, DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, inject, Injector, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { combineLatest, distinctUntilChanged, map, of, scan, startWith, Subject, switchMap } from 'rxjs';
import { SessionService } from '../../../core/auth/session.service';
import { AuthPrompt } from '../../../core/auth/auth-prompt.service';
import { CppHighlight } from '../../../shared/ui/cpp-highlight';
import { StatePanel } from '../../../shared/ui/state-panel';
import { StatusBadge } from '../../../shared/ui/status-badge';
import { isProblemId } from '../../problems/data-access/problems-api';
import { SubmissionsApi } from '../data-access/submissions-api';
import { activeSubmission, outcomeLabel, outcomeTone, Submission, SubmissionPage } from '../data-access/submission.models';
import { pollSubmission } from '../data-access/submission-polling';
import { SubmissionResultView } from '../submission-result/submission-result';
import { SubmissionPerformanceView } from '../submission-performance/submission-performance';

type Unavailable = { readonly status: 'loading' | 'invalid' | 'error' | 'guest' | 'unavailable' | 'denied' | 'missing' };
type Ready<T> = { readonly status: 'ready'; readonly data: T; readonly owner: string; readonly problem: string; readonly interrupted: boolean };
type HistoryState = Unavailable | Ready<SubmissionPage>;
type DetailState = Unavailable | Ready<Submission>;

@Component({
  selector: 'minos-problem-submissions', imports: [DatePipe, CppHighlight, StatePanel, StatusBadge, SubmissionResultView, SubmissionPerformanceView],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './problem-submissions.html', styleUrl: './problem-submissions.css',
})
export class ProblemSubmissions {
  readonly problemId = input.required<string>();
  readonly problemTitle = input.required<string>();
  readonly revision = input(0);
  readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(SubmissionsApi);
  private readonly auth = inject(AuthPrompt);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly reloadHistory = new Subject<void>();
  private readonly reloadDetail = new Subject<void>();
  readonly copied = signal('');
  readonly outcomeLabel = outcomeLabel;
  readonly outcomeTone = outcomeTone;
  private readonly identity = toObservable(computed(() => this.session.account()?.id ?? this.session.state().status));
  private readonly pageQuery = this.route.queryParamMap.pipe(map(params => {
    const raw = params.get('submissionPage') ?? '1';
    const page = /^[1-9]\d{0,8}$/.test(raw) ? Number(raw) : 0;
    return page && (page - 1) * 20 <= 2147483647 ? page : null;
  }), distinctUntilChanged());
  private readonly selectionQuery = this.route.queryParamMap.pipe(map(params => params.get('submission')), distinctUntilChanged());
  readonly selectedId = toSignal(this.selectionQuery, { initialValue: this.route.snapshot.queryParamMap.get('submission') });
  private readonly historyState = toSignal(combineLatest([toObservable(this.problemId), this.identity, this.pageQuery,
    toObservable(this.revision), this.reloadHistory.pipe(startWith(undefined))]).pipe(
    switchMap(([problem, owner, page]) => {
      if (!this.session.account()) return of<HistoryState>({ status: 'loading' });
      if (page === null) return of<HistoryState>({ status: 'invalid' });
      return pollSubmission(() => this.api.list(page, 20, problem), data => data.items.some(item => activeSubmission(item.status)), this.document).pipe(
        scan((previous: HistoryState, read): HistoryState => {
          if (read.data) return { status: 'ready', data: read.data, owner, problem, interrupted: false };
          const status = (read.error as { status?: number })?.status;
          if (status === 401 || status === 403) return { status: status === 401 ? 'guest' : 'denied' };
          return previous.status === 'ready' ? { ...previous, interrupted: true } : { status: 'error' };
        }, { status: 'loading' } as HistoryState), startWith<HistoryState>({ status: 'loading' }),
      );
    }),
  ), { initialValue: { status: 'loading' } as HistoryState });
  private readonly detailState = toSignal(combineLatest([toObservable(this.problemId), this.identity, this.selectionQuery,
    this.reloadDetail.pipe(startWith(undefined))]).pipe(
    switchMap(([problem, owner, id]) => {
      this.copied.set('');
      if (!id || !this.session.account()) return of<DetailState>({ status: 'loading' });
      if (!isProblemId(id)) return of<DetailState>({ status: 'invalid' });
      return pollSubmission(() => this.api.detail(id), data => activeSubmission(data.status), this.document).pipe(
        scan((previous: DetailState, read): DetailState => {
          if (read.data) return read.data.problemId.toLowerCase() === problem.toLowerCase()
            ? { status: 'ready', data: read.data, owner, problem, interrupted: false } : { status: 'missing' };
          const status = (read.error as { status?: number })?.status;
          if (status === 401 || status === 403 || status === 404) return { status: status === 401 ? 'guest' : status === 403 ? 'denied' : 'missing' };
          return previous.status === 'ready' ? { ...previous, interrupted: true } : { status: 'error' };
        }, { status: 'loading' } as DetailState), startWith<DetailState>({ status: 'loading' }),
      );
    }),
  ), { initialValue: { status: 'loading' } as DetailState });
  readonly history = computed(() => this.visibleState(this.historyState()));
  readonly detail = computed(() => this.visibleState(this.detailState()));
  private visibleState<T>(state: Unavailable | Ready<T>): Unavailable | Ready<T> {
    const session = this.session.state();
    if (session.status !== 'authenticated') return { status: session.status === 'checking' ? 'loading' : session.status };
    if (state.status === 'ready' && (state.owner !== session.account.id || state.problem !== this.problemId())) return { status: 'loading' };
    return state;
  }
  pages(data: SubmissionPage): number { return Math.max(1, Math.ceil(data.total / data.pageSize)); }
  refreshHistory(): void { this.reloadHistory.next(); }
  refreshDetail(): void { this.reloadDetail.next(); }
  signIn(): void { this.auth.open(); }
  reconnect(): void { this.session.refresh().subscribe(); }
  backToList(): void { this.navigate({ submission: null }, 'problem-submissions-heading'); }
  select(id: string): void { this.navigate({ submission: id }, 'problem-submission-heading'); }
  changePage(page: number): void { this.navigate({ submissionPage: page, submission: null }, 'problem-submissions-heading'); }
  private navigate(queryParams: Record<string, string | number | null>, focus: string): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { tab: 'submissions', ...queryParams }, queryParamsHandling: 'merge' })
      .then(() => afterNextRender(() => this.document.getElementById(focus)?.focus(), { injector: this.injector }));
  }
  async copy(source: string): Promise<void> {
    const owner = this.session.account()?.id, selected = this.selectedId();
    try {
      const clipboard = this.document.defaultView?.navigator.clipboard;
      if (!clipboard) throw new Error('Clipboard unavailable');
      await clipboard.writeText(source);
      if (this.session.account()?.id === owner && this.selectedId() === selected) this.copied.set('Copied');
    } catch {
      if (this.session.account()?.id === owner && this.selectedId() === selected) this.copied.set('Select the code to copy it.');
    }
  }
}

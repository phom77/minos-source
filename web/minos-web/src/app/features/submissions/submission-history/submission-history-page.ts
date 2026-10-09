import { DatePipe, DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, effect, inject, Injector } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { combineLatest, distinctUntilChanged, map, of, scan, startWith, Subject, switchMap } from 'rxjs';
import { SessionService } from '../../../core/auth/session.service';
import { AuthPrompt } from '../../../core/auth/auth-prompt.service';
import { StatePanel } from '../../../shared/ui/state-panel';
import { StatusBadge } from '../../../shared/ui/status-badge';
import { Pagination } from '../../../shared/ui/pagination';
import { readCatalogQuery } from '../../problems/catalog-query';
import { SubmissionsApi } from '../data-access/submissions-api';
import { activeSubmission, outcomeLabel, outcomeTone, SubmissionPage } from '../data-access/submission.models';
import { pollSubmission } from '../data-access/submission-polling';
import { ProblemLabel, ProblemLabels } from '../data-access/problem-labels';

type HistoryState = { readonly status: 'loading' | 'invalid' | 'error' | 'denied' | 'guest' | 'unavailable' }
  | { readonly status: 'ready'; readonly data: SubmissionPage; readonly labels: Record<string, ProblemLabel>; readonly interrupted: boolean };
@Component({ selector: 'minos-submission-history-page', imports: [DatePipe, RouterLink, StatePanel, StatusBadge, Pagination],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './submission-history-page.html', styleUrl: '../submission-pages.css' })
export class SubmissionHistoryPage {
  readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(SubmissionsApi);
  private readonly labels = inject(ProblemLabels);
  private readonly document = inject(DOCUMENT);
  private readonly auth = inject(AuthPrompt);
  private readonly injector = inject(Injector);
  private readonly reload = new Subject<void>();
  private focusPageSize = false;
  private focusRefresh = false;
  private renderedPage: number | null = null;
  readonly pageSizes = [10, 20, 50, 100];
  readonly outcomeLabel = outcomeLabel;
  readonly outcomeTone = outcomeTone;
  private readonly query = this.route.queryParamMap.pipe(map(readCatalogQuery), distinctUntilChanged((a, b) => a?.page === b?.page && a?.pageSize === b?.pageSize));
  private readonly identity = toObservable(this.session.state).pipe(map(state => state.status === 'authenticated' ? state.account.id : state.status), distinctUntilChanged());
  readonly state = toSignal(combineLatest([this.query, this.identity, this.reload.pipe(startWith(undefined))]).pipe(
    switchMap(([query]) => {
      const session = this.session.state();
      if (session.status !== 'authenticated') return of<HistoryState>({ status: session.status === 'checking' ? 'loading' : session.status });
      if (!query) return of<HistoryState>({ status: 'invalid' });
      return pollSubmission(() => this.api.list(query.page, query.pageSize), page => page.items.some(item => activeSubmission(item.status)), this.document).pipe(
        scan((previous: HistoryState, result): HistoryState => {
          if (result.data) return { status: 'ready', data: result.data, labels: previous.status === 'ready' ? previous.labels : {}, interrupted: false };
          const status = (result.error as { status?: number })?.status;
          if (status === 401) return { status: 'guest' };
          if (status === 403) return { status: 'denied' };
          return previous.status === 'ready' ? { ...previous, interrupted: true } : { status: 'error' };
        }, { status: 'loading' } as HistoryState),
        switchMap(state => state.status === 'ready' ? this.labels.read(state.data.items.map(item => item.problemVersionId)).pipe(map(labels => ({ ...state, labels: { ...state.labels, ...labels } }))) : of(state)),
        startWith<HistoryState>({ status: 'loading' }),
      );
    }),
  ), { initialValue: { status: 'loading' } as HistoryState });
  constructor() {
    effect(() => {
      const state = this.state();
      if (state.status !== 'ready') return;
      const target = this.focusPageSize ? 'history-page-size' : this.focusRefresh || (this.renderedPage !== null && this.renderedPage !== state.data.page) ? 'history-heading' : null;
      this.focusPageSize = false; this.focusRefresh = false; this.renderedPage = state.data.page;
      if (target) afterNextRender(() => this.document.getElementById(target)?.focus(), { injector: this.injector });
    });
  }
  refresh(): void { this.focusRefresh = true; this.reload.next(); }
  reconnect(): void { this.session.refresh().subscribe(); }
  signIn(): void { void this.router.navigateByUrl(this.auth.entryTree('login', this.router.url, 'expired')); }
  changePageSize(event: Event): void {
    const pageSize = Number((event.target as HTMLSelectElement).value);
    this.focusPageSize = true;
    void this.router.navigate([], { relativeTo: this.route, queryParams: { page: 1, pageSize }, queryParamsHandling: 'merge' });
  }
}

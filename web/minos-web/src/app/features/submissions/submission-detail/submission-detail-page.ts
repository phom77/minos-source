import { DatePipe, DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, effect, inject, Injector, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { combineLatest, distinctUntilChanged, map, of, scan, startWith, Subject, switchMap, tap } from 'rxjs';
import { SessionService } from '../../../core/auth/session.service';
import { AuthPrompt } from '../../../core/auth/auth-prompt.service';
import { StatePanel } from '../../../shared/ui/state-panel';
import { CppHighlight } from '../../../shared/ui/cpp-highlight';
import { SubmissionPerformanceView } from '../submission-performance/submission-performance';
import { isProblemId } from '../../problems/data-access/problems-api';
import { SubmissionsApi } from '../data-access/submissions-api';
import { SubmissionDraft } from '../data-access/submission-draft';
import { activeSubmission, outcomeLabel, Submission } from '../data-access/submission.models';
import { SubmissionResultView } from '../submission-result/submission-result';
import { pollSubmission } from '../data-access/submission-polling';
import { ProblemLabel, ProblemLabels } from '../data-access/problem-labels';

type DetailState = { readonly status: 'loading' | 'invalid' | 'missing' | 'denied' | 'error' | 'guest' | 'unavailable' }
  | { readonly status: 'ready'; readonly data: Submission; readonly label: ProblemLabel | null; readonly interrupted: boolean };
@Component({ selector: 'minos-submission-detail-page', imports: [DatePipe, RouterLink, StatePanel, SubmissionResultView, CppHighlight, SubmissionPerformanceView],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './submission-detail-page.html', styleUrl: '../submission-pages.css' })
export class SubmissionDetailPage {
  readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(SubmissionsApi);
  private readonly labels = inject(ProblemLabels);
  private readonly draft = inject(SubmissionDraft);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly title = inject(Title);
  private readonly auth = inject(AuthPrompt);
  private readonly reload = new Subject<void>();
  private focusRefresh = false;
  readonly copied = signal('');
  readonly outcomeLabel = outcomeLabel;
  readonly active = activeSubmission;
  private readonly identity = toObservable(this.session.state).pipe(map(state => state.status === 'authenticated' ? state.account.id : state.status), distinctUntilChanged());
  readonly state = toSignal(combineLatest([this.route.paramMap, this.identity, this.reload.pipe(startWith(undefined))]).pipe(
    switchMap(([params]) => {
      this.copied.set('');
      const session = this.session.state();
      if (session.status !== 'authenticated') return of<DetailState>({ status: session.status === 'checking' ? 'loading' : session.status });
      const id = params.get('submissionId');
      if (!isProblemId(id)) return of<DetailState>({ status: 'invalid' });
      return pollSubmission(() => this.api.detail(id), data => activeSubmission(data.status), this.document).pipe(
        scan((previous: DetailState, result): DetailState => {
          if (result.data) return { status: 'ready', data: result.data, label: previous.status === 'ready' ? previous.label : null, interrupted: false };
          const status = (result.error as { status?: number })?.status;
          if (status === 401) return { status: 'guest' };
          if (status === 403) return { status: 'denied' };
          if (status === 404) return { status: 'missing' };
          return previous.status === 'ready' ? { ...previous, interrupted: true } : { status: 'error' };
        }, { status: 'loading' } as DetailState),
        switchMap(state => state.status === 'ready' ? this.labels.read([state.data.problemVersionId]).pipe(map(labels => ({ ...state, label: labels[state.data.problemVersionId] ?? state.label }))) : of(state)),
        startWith<DetailState>({ status: 'loading' }),
      );
    }), tap(state => this.title.setTitle(state.status === 'ready' ? `${outcomeLabel(state.data.status, state.data.result?.verdict ?? null)} — Minos` : 'Submission — Minos')),
  ), { initialValue: { status: 'loading' } as DetailState });
  constructor() {
    effect(() => {
      if (this.state().status !== 'ready' || !this.focusRefresh) return;
      this.focusRefresh = false;
      afterNextRender(() => this.document.getElementById('result-heading')?.focus(), { injector: this.injector });
    });
  }
  refresh(): void { this.focusRefresh = true; this.reload.next(); }
  reconnect(): void { this.session.refresh().subscribe(); }
  signIn(): void { void this.router.navigateByUrl(this.auth.entryTree('login', this.router.url, 'expired')); }
  edit(data: Submission): void {
    this.draft.save(data.problemId, data.source);
    void this.router.navigate(['/problems', data.problemId], { fragment: 'code-heading' });
  }
  async copy(source: string): Promise<void> {
    const identity = this.session.account()?.id;
    try {
      const clipboard = this.document.defaultView?.navigator.clipboard;
      if (!clipboard) throw new Error('Clipboard unavailable');
      await clipboard.writeText(source); if (this.session.account()?.id === identity) this.copied.set('Copied');
    }
    catch { if (this.session.account()?.id === identity) this.copied.set('Select the code to copy it.'); }
  }
}

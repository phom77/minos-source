import { DOCUMENT, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, combineLatest, exhaustMap, finalize, forkJoin, map, merge, of, startWith, Subject, switchMap, timer } from 'rxjs';
import { SessionService } from '../../core/auth/session.service';
import { StatePanel } from '../../shared/ui/state-panel';
import { FormFeedback } from '../accounts/form-feedback';
import { CodeEditor } from '../submissions/code-editor/code-editor';
import { ProblemStatement } from '../../shared/ui/problem-statement';
import { ProblemComments } from '../comments/problem-comments';
import { sourceError } from '../submissions/data-access/submission.models';
import { SubmissionDraft } from '../submissions/data-access/submission-draft';
import { isProblemId, PublishedProblem } from '../problems/data-access/problems-api';
import { Contest, contestError, ContestsApi } from './data-access/contests-api';
type SolveState = { kind: string; data: { contest: Contest; problem: PublishedProblem; contestProblemId: string } | null };
@Component({ selector: 'minos-contest-solve', imports: [DatePipe, ReactiveFormsModule, RouterLink, StatePanel, CodeEditor, ProblemStatement, ProblemComments],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './contest-solve.html', styleUrl: './contests.css' })
export class ContestSolve {
  private readonly api = inject(ContestsApi); private readonly route = inject(ActivatedRoute); private readonly session = inject(SessionService);
  private readonly draft = inject(SubmissionDraft); private draftKey = '';
  private readonly destroy = inject(DestroyRef); private readonly document = inject(DOCUMENT); private readonly injector = inject(Injector);
  private readonly summary = viewChild<ElementRef<HTMLElement>>('summary'); private readonly reload = new Subject<void>();
  readonly source = new FormControl('', { nonNullable: true }); readonly pending = signal(false); readonly submitted = signal(false);
  readonly error = signal<string | null>(null); readonly submissionId = signal<string | null>(null); readonly feedback = new FormFeedback<'source'>();
  readonly state = toSignal(combineLatest([this.route.paramMap, toObservable(this.session.account)]).pipe(switchMap(([params, account]) => {
    const id = params.get('contestId'), problemId = params.get('contestProblemId');
    this.draftKey = `contest:${id}:${problemId}`;
    this.source.reset(this.draft.read(this.draftKey), { emitEvent: false }); this.feedback.reset(); this.submitted.set(false); this.error.set(null); this.submissionId.set(null);
    if (!account || !isProblemId(id) || !isProblemId(problemId)) return of<SolveState>({ kind: 'error', data: null });
    return merge(timer(0, 15000), this.reload).pipe(exhaustMap(() => forkJoin({ contest: this.api.detail(id), problem: this.api.problem(id, problemId) }).pipe(
      map(data => {
        const selected = data.contest.problems.find(p => p.id === problemId);
        if (!selected || selected.versionId !== data.problem.versionId) throw new Error('Contest problem version mismatch.');
        return { kind: 'ready', data: { ...data, contestProblemId: problemId } };
      }),
      catchError(() => of<SolveState>({ kind: 'error', data: null })))), startWith<SolveState>({ kind: 'loading', data: null }));
  })), { initialValue: { kind: 'loading', data: null } as SolveState });
  constructor() {
    this.source.valueChanges.pipe(takeUntilDestroyed()).subscribe(source => this.draft.save(this.draftKey, source));
    const warn = (event: BeforeUnloadEvent) => { if (this.pending() || this.source.dirty) event.preventDefault(); };
    this.document.defaultView?.addEventListener('beforeunload', warn);
    this.destroy.onDestroy(() => { this.document.defaultView?.removeEventListener('beforeunload', warn); this.source.reset('', { emitEvent: false }); });
  }
  retry(): void { this.reload.next(); }
  fieldError(): string | null { return this.feedback.visible('source', this.submitted()) ? sourceError(this.source.value) : null; }
  blur(): void { this.feedback.blur('source', sourceError(this.source.value) !== null); }
  canRetry(): boolean { return this.draft.hasRequestKey(this.draftKey, this.source.value); }
  submit(): void {
    const data = this.state().data, owner = this.session.account()?.id;
    if (!data || !owner || this.pending()) return;
    const key = this.draftKey;
    this.submitted.set(true); this.error.set(null);
    if (sourceError(this.source.value)) { this.focus(); return; }
    this.pending.set(true); this.source.disable({ emitEvent: false });
    this.api.submit(data.contest.id, data.contestProblemId, this.source.value, this.draft.requestKey(key, this.source.value)).pipe(finalize(() => { this.pending.set(false); this.source.enable({ emitEvent: false }); }),
      takeUntilDestroyed(this.destroy)).subscribe({
      next: result => { if (this.session.account()?.id !== owner || this.draftKey !== key) return; this.draft.clear(key); this.submissionId.set(result.id); this.source.markAsPristine(); this.feedback.reset(); this.submitted.set(false); },
      error: error => {
        if (this.session.account()?.id !== owner || this.draftKey !== key) return;
        this.error.set(!(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500
          ? 'Submission not confirmed. Retry to check the same submission. After reloading or editing your code, check contest history before submitting again.'
          : error.status === 409 ? 'This submission request changed. Check contest history before submitting again.' : contestError(error));
        this.focus(); this.retry();
      },
    });
  }
  private focus(): void { afterNextRender(() => this.summary()?.nativeElement.focus(), { injector: this.injector }); }
}

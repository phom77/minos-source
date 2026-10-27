import { DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { combineLatest, finalize, of, scan, startWith, switchMap } from 'rxjs';
import { SessionService } from '../../../core/auth/session.service';
import { AuthPrompt } from '../../../core/auth/auth-prompt.service';
import { FormFeedback } from '../../accounts/form-feedback';
import { SubmissionsApi } from '../data-access/submissions-api';
import { SubmissionDraft } from '../data-access/submission-draft';
import { activeSubmission, sourceBytes, sourceError, Submission } from '../data-access/submission.models';
import { PublishedProblem } from '../../problems/data-access/problems-api';
import { pollSubmission } from '../data-access/submission-polling';
import { SubmissionResultView } from '../submission-result/submission-result';
import { CodeEditor } from '../code-editor/code-editor';
import { UiIcon } from '../../../shared/ui/ui-icon';

type InlineResult = { readonly data: Submission | null; readonly interrupted: boolean; readonly message: string | null };

@Component({ selector: 'minos-submission-form', imports: [ReactiveFormsModule, RouterLink, SubmissionResultView, CodeEditor, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './submission-form.html', styleUrl: './submission-form.css' })
export class SubmissionForm {
  readonly problemId = input.required<string>();
  readonly inlineResult = input(false);
  readonly samples = input<PublishedProblem['samples']>([]);
  readonly expanded = input(false);
  readonly expand = output<void>();
  readonly admitted = output<void>();
  readonly viewDetails = output<void>();
  readonly session = inject(SessionService);
  private readonly api = inject(SubmissionsApi);
  private readonly draft = inject(SubmissionDraft);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthPrompt);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private readonly summary = viewChild<ElementRef<HTMLElement>>('summary');
  readonly source = new FormControl('', { nonNullable: true });
  readonly feedback = new FormFeedback<'source'>();
  readonly pending = signal(false);
  readonly submitted = signal(false);
  readonly error = signal<string | null>(null);
  readonly uncertain = signal(false);
  readonly bytes = signal(0);
  readonly consoleTab = signal<'examples' | 'result'>('examples');
  readonly exampleIndex = signal(0);
  private readonly selection = signal<{ readonly owner: string; readonly data: Submission } | null>(null);
  readonly result = toSignal(combineLatest([toObservable(this.selection), toObservable(computed(() => this.session.account()?.id ?? this.session.state().status))]).pipe(
    switchMap(([selected, identity]) => {
      if (!selected || identity !== selected.owner) return of<InlineResult>({ data: null, interrupted: false, message: null });
      return pollSubmission(() => this.api.detail(selected.data.id), data => activeSubmission(data.status), this.document).pipe(
        scan((previous: InlineResult, read): InlineResult => {
          if (read.data) return { data: read.data, interrupted: false, message: null };
          const status = (read.error as { status?: number })?.status;
          if (status === 401 || status === 403 || status === 404) return { data: null, interrupted: false, message: status === 404 ? 'Submission not found.' : 'Sign in to view your own submissions.' };
          return { ...previous, interrupted: true };
        }, { data: selected.data, interrupted: false, message: null }),
        startWith<InlineResult>({ data: selected.data, interrupted: false, message: null }),
      );
    }),
  ), { initialValue: { data: null, interrupted: false, message: null } as InlineResult });
  private acknowledgedSource = '';
  private owner: string | null | undefined;
  private problem: string | undefined;
  constructor() {
    effect(() => {
      const id = this.problemId(), owner = this.session.account()?.id ?? null;
      if (owner === this.owner && id === this.problem) return;
      // Guest composition survives sign-in; an existing account's source never crosses identities.
      const guestSource = this.owner === null && owner !== null && id === this.problem ? this.source.value : '';
      this.owner = owner; this.problem = id;
      this.source.reset(this.draft.read(id) || guestSource); this.bytes.set(sourceBytes(this.source.value));
      this.feedback.reset(); this.submitted.set(false); this.error.set(null); this.uncertain.set(false);
      this.selection.set(null); this.consoleTab.set('examples'); this.exampleIndex.set(0); this.acknowledgedSource = '';
      if (this.router.parseUrl(this.router.url).fragment === 'code-heading' && !this.auth.mode()) {
        afterNextRender(() => this.document.getElementById('submission-source')?.focus(), { injector: this.injector });
      }
    });
    this.source.valueChanges.pipe(takeUntilDestroyed()).subscribe(source => {
      this.bytes.set(sourceBytes(source)); this.draft.save(this.problemId(), source);
      if (!this.draft.hasRequestKey(this.problemId(), source)) this.uncertain.set(false);
    });
    const warnOnReload = (event: BeforeUnloadEvent) => { if (this.pending() || (this.source.value.trim() && this.source.value !== this.acknowledgedSource)) event.preventDefault(); };
    this.document.defaultView?.addEventListener('beforeunload', warnOnReload);
    this.destroyRef.onDestroy(() => this.document.defaultView?.removeEventListener('beforeunload', warnOnReload));
    this.destroyRef.onDestroy(() => this.source.reset(''));
  }
  fieldError(): string | null { return this.feedback.visible('source', this.submitted()) ? sourceError(this.source.value) : null; }
  edit(): void { this.feedback.edit('source'); }
  blur(): void { this.feedback.blur('source', sourceError(this.source.value) !== null); }
  signIn(): void { this.auth.open(); }
  retrySession(): void { this.session.refresh().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(); }
  focusSource(event: Event): void { event.preventDefault(); this.document.getElementById('submission-source')?.focus(); }
  fieldLink(): string { return this.router.url.split('#')[0] + '#submission-source'; }
  selectConsole(tab: 'examples' | 'result'): void { this.consoleTab.set(tab); }
  consoleKey(event: KeyboardEvent): void {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const tab = event.key === 'Home' ? 'examples' : event.key === 'End' ? 'result' : this.consoleTab() === 'examples' ? 'result' : 'examples';
    this.consoleTab.set(tab); this.document.getElementById('console-tab-' + tab)?.focus();
  }
  exampleKey(event: KeyboardEvent): void {
    const count = this.samples().length;
    if (!count || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const index = event.key === 'Home' ? 0 : event.key === 'End' ? count - 1
      : (this.exampleIndex() + (event.key === 'ArrowLeft' ? -1 : 1) + count) % count;
    this.exampleIndex.set(index); this.document.getElementById('example-tab-' + index)?.focus();
  }
  refreshResult(): void {
    const selected = this.selection(), data = this.result().data;
    if (selected) this.selection.set({ ...selected, data: data ?? selected.data });
  }
  submit(): void {
    if (this.pending()) return;
    this.submitted.set(true); this.error.set(null);
    if (sourceError(this.source.value)) { this.focusError(); return; }
    const owner = this.session.account()?.id;
    if (!owner) { if (this.session.state().status === 'guest') this.signIn(); return; }
    this.pending.set(true); this.source.disable({ emitEvent: false });
    const id = this.problemId();
    const sentSource = this.source.value;
    this.api.create(id, sentSource, this.draft.requestKey(id, sentSource)).pipe(finalize(() => { this.pending.set(false); this.source.enable({ emitEvent: false }); }), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: submission => {
        if (this.session.account()?.id !== owner) return;
        this.draft.clear(id); this.feedback.reset(); this.submitted.set(false); this.uncertain.set(false);
        this.admitted.emit();
        if (this.inlineResult()) {
          this.acknowledgedSource = sentSource; this.selection.set({ owner, data: submission }); this.consoleTab.set('result');
          afterNextRender(() => this.document.getElementById('console-tab-result')?.focus(), { injector: this.injector });
        } else { this.source.reset(''); void this.router.navigate(['/submissions', submission.id]); }
      },
      error: (error: unknown) => {
        if (this.session.account()?.id !== owner) return;
        const status = error instanceof HttpErrorResponse ? error.status : null;
        this.uncertain.set(status === null || status === 0 || (status !== null && status >= 500));
        const retry = error instanceof HttpErrorResponse ? Number(error.headers.get('Retry-After')) : 0;
        this.error.set(status === 429 ? (Number.isSafeInteger(retry) && retry > 0 && retry <= 3600 ? `Too many active submissions. Try again in ${retry} seconds.` : 'Too many active submissions. Wait a moment, then try again.') : status === 404 ? 'This problem is no longer available for submission.'
          : status === 403 ? 'You cannot submit to this problem.' : status === 400 ? 'Your submission was rejected. Refresh the page and try again.'
          : status === 413 ? 'The submission is too large. Reduce your code and try again.'
          : status === 409 ? 'This submission request changed. Check your history before submitting again.'
          : this.uncertain() ? 'Submission not confirmed. Retry to check the same submission, or check your history. After reloading or editing your code, check history before submitting again.' : 'Your session has expired. Sign in to submit.');
        this.focusError();
      },
    });
  }
  private focusError(): void { afterNextRender(() => this.summary()?.nativeElement.focus(), { injector: this.injector }); }
}

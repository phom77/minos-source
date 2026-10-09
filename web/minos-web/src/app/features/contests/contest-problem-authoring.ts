import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, of, switchMap, tap } from 'rxjs';
import { SessionService } from '../../core/auth/session.service';
import { FormFeedback } from '../accounts/form-feedback';
import { FieldFeedback } from '../../shared/ui/field-feedback';
import { isProblemId, PublishedProblem } from '../problems/data-access/problems-api';
import { contestError, ContestsApi } from './data-access/contests-api';
import { AuthoringDraft, ContestAuthoringApi } from './data-access/contest-authoring-api';
type Field = 'title' | 'statement' | 'timeLimitMilliseconds' | 'memoryLimitMegabytes';
@Component({ selector: 'minos-contest-problem-authoring', imports: [ReactiveFormsModule, RouterLink, FieldFeedback],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './contest-problem-authoring.html', styleUrl: './contests.css' })
export class ContestProblemAuthoring {
  private readonly api = inject(ContestAuthoringApi); private readonly contests = inject(ContestsApi); private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router); readonly session = inject(SessionService); private readonly destroy = inject(DestroyRef); private readonly injector = inject(Injector);
  private readonly summary = viewChild<ElementRef<HTMLElement>>('summary'); readonly contestId = this.route.snapshot.paramMap.get('contestId')!;
  readonly draft = signal<AuthoringDraft | null>(null); readonly published = signal<PublishedProblem | null>(null); readonly pending = signal(false);
  readonly submitted = signal(false); readonly error = signal<string | null>(null); readonly saved = signal(false); readonly feedback = new FormFeedback<Field>();
  readonly targetReady = signal(false); readonly checkingTarget = signal(true);
  readonly form = new FormGroup({ title: new FormControl('', { nonNullable: true }), statement: new FormControl('', { nonNullable: true }),
    timeLimitMilliseconds: new FormControl(1000, { nonNullable: true }), memoryLimitMegabytes: new FormControl(64, { nonNullable: true }),
    sampleInput: new FormControl('', { nonNullable: true }), sampleOutput: new FormControl('', { nonNullable: true }),
    statementReviewed: new FormControl(false, { nonNullable: true }), samplesVerified: new FormControl(false, { nonNullable: true }), referenceSolutionChecked: new FormControl(false, { nonNullable: true }) });
  private readonly owner = this.session.account()?.id;
  constructor() {
    this.contests.detail(this.contestId).pipe(finalize(() => this.checkingTarget.set(false)), takeUntilDestroyed()).subscribe({
      next: contest => {
        if (!this.sameOwner()) return;
        this.targetReady.set(contest.canManage && contest.state === 'Draft');
        if (!this.targetReady()) this.error.set('Open an editable contest that you organize before creating a problem.');
      }, error: e => this.fail(e),
    });
    const id = this.route.snapshot.queryParamMap.get('draft');
    if (isProblemId(id)) {
      this.pending.set(true);
      this.api.read(id).pipe(finalize(() => this.pending.set(false)), takeUntilDestroyed()).subscribe({ next: draft => {
        if (!this.sameOwner()) return;
        this.draft.set(draft); this.form.patchValue({ title: draft.title, statement: draft.statement, timeLimitMilliseconds: draft.timeLimitMilliseconds ?? 1000,
          memoryLimitMegabytes: draft.memoryLimitMegabytes ?? 64, sampleInput: draft.samples[0]?.input ?? '', sampleOutput: draft.samples[0]?.output ?? '' }); this.saved.set(true);
      }, error: e => this.fail(e) });
    }
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.saved.set(false));
  }
  sameOwner(): boolean { return this.session.account()?.id === this.owner; }
  allowed(): boolean { return this.targetReady() && this.sameOwner() && ['Setter', 'Administrator'].includes(this.session.account()?.role ?? ''); }
  validation(field: Field): string | null {
    const v = this.form.getRawValue();
    if (field === 'title') return !v.title.trim() || v.title.trim().length > 120 ? 'Enter a title of 1–120 characters.' : null;
    if (field === 'statement') return !v.statement.trim() || v.statement.length > 65536 ? 'Enter a statement of up to 65,536 characters.' : null;
    if (field === 'timeLimitMilliseconds') return !Number.isInteger(v.timeLimitMilliseconds) || v.timeLimitMilliseconds < 100 || v.timeLimitMilliseconds > 10000 ? 'Use 100–10,000 milliseconds.' : null;
    return !Number.isInteger(v.memoryLimitMegabytes) || v.memoryLimitMegabytes < 16 || v.memoryLimitMegabytes > 1024 ? 'Use 16–1,024 MiB.' : null;
  }
  fieldError(field: Field): string | null { return this.feedback.visible(field, this.submitted()) ? this.validation(field) : null; }
  blur(field: Field): void { this.feedback.blur(field, this.validation(field) !== null); }
  save(): void {
    if (this.pending() || this.published() || !this.allowed()) return;
    this.submitted.set(true); this.error.set(null);
    if ((['title', 'statement', 'timeLimitMilliseconds', 'memoryLimitMegabytes'] as Field[]).some(f => this.validation(f))) { this.error.set('Check the highlighted fields.'); this.focus(); return; }
    const v = this.form.getRawValue(); this.pending.set(true);
    (this.draft() ? of(this.draft()!) : this.api.create(v.title, v.statement)).pipe(tap(d => {
      if (!this.sameOwner()) return; this.draft.set(d); void this.router.navigate([], { relativeTo: this.route, queryParams: { draft: d.id }, replaceUrl: true });
    }), switchMap(d => this.sameOwner() ? this.api.update(d.id, { ...v, samples: [{ input: v.sampleInput, output: v.sampleOutput }] }) : of(null)),
      finalize(() => this.pending.set(false)), takeUntilDestroyed(this.destroy)).subscribe({ next: d => {
        if (!d || !this.sameOwner()) return; this.draft.set(d); this.saved.set(true); this.submitted.set(false); this.feedback.reset();
      }, error: e => this.fail(e) });
  }
  upload(event: Event): void {
    const input = event.target as HTMLInputElement, file = input.files?.[0], draft = this.draft();
    if (!file || !draft || this.pending() || !this.allowed()) return;
    this.pending.set(true); this.error.set(null);
    this.api.upload(draft.id, file).pipe(finalize(() => { this.pending.set(false); input.value = ''; }), takeUntilDestroyed(this.destroy))
      .subscribe({ next: d => { if (this.sameOwner()) this.draft.set(d); }, error: e => this.fail(e) });
  }
  remove(testId: string): void {
    const draft = this.draft(); if (!draft || this.pending() || !this.allowed()) return;
    this.pending.set(true); this.error.set(null);
    this.api.remove(draft.id, testId).pipe(finalize(() => this.pending.set(false)), takeUntilDestroyed(this.destroy))
      .subscribe({ next: d => { if (this.sameOwner()) this.draft.set(d); }, error: e => this.fail(e) });
  }
  publish(): void {
    const draft = this.draft(), v = this.form.getRawValue(); if (!draft || this.pending() || !this.allowed()) return;
    this.error.set(null);
    if (!this.published() && (!v.statementReviewed || !v.samplesVerified || !v.referenceSolutionChecked)) { this.error.set('Complete all three publication checks.'); this.focus(); return; }
    // Always save the visible content before publication; checklist edits do not bypass this.
    this.pending.set(true);
    const publication = this.published() ? of(this.published()!) : this.api.update(draft.id, { ...v, samples: [{ input: v.sampleInput, output: v.sampleOutput }] }).pipe(switchMap(() => this.api.publish(draft.id)));
    publication.pipe(tap(p => { if (this.sameOwner()) this.published.set(p); }), switchMap(p => this.sameOwner() ?
      this.contests.detail(this.contestId).pipe(switchMap(c => c.problems.some(item => item.problemId === p.problemId) ? of(c) : this.contests.save(c.id, {
        title: c.title, description: c.description, startsAtUtc: c.startsAtUtc, endsAtUtc: c.endsAtUtc, revision: c.revision,
        versionIds: [...c.problems.map(item => item.versionId), p.versionId],
      }))) : of(null)), finalize(() => this.pending.set(false)), takeUntilDestroyed(this.destroy)).subscribe({
      next: () => { if (this.sameOwner()) void this.router.navigate(['/contests', this.contestId, 'edit']); }, error: e => this.fail(e),
    });
  }
  private fail(error: unknown): void { if (this.sameOwner()) { this.error.set(contestError(error)); this.focus(); } }
  private focus(): void { afterNextRender(() => this.summary()?.nativeElement.focus(), { injector: this.injector }); }
}

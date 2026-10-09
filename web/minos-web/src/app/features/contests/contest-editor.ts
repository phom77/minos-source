import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, finalize, of, Subject, startWith, switchMap } from 'rxjs';
import { SessionService } from '../../core/auth/session.service';
import { FieldFeedback } from '../../shared/ui/field-feedback';
import { StatePanel } from '../../shared/ui/state-panel';
import { FormFeedback } from '../accounts/form-feedback';
import { isProblemId } from '../problems/data-access/problems-api';
import { Contest, contestError, ContestsApi } from './data-access/contests-api';
import { ContestProblemPicker, SelectedProblem } from './contest-problem-picker';

type Field = 'title' | 'description' | 'start' | 'end';
export function localDateTime(utc: string): string { const d = new Date(utc); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16); }
@Component({ selector: 'minos-contest-editor', imports: [ReactiveFormsModule, RouterLink, FieldFeedback, StatePanel, ContestProblemPicker],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './contest-editor.html', styleUrl: './contests.css' })
export class ContestEditor {
  readonly session = inject(SessionService); readonly organizer = computed(() => ['Setter', 'Administrator'].includes(this.session.account()?.role ?? ''));
  private readonly api = inject(ContestsApi); private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef); private readonly injector = inject(Injector); private readonly summary = viewChild<ElementRef<HTMLElement>>('summary');
  private readonly reload = new Subject<void>();
  readonly contest = signal<Contest | null>(null); readonly selected = signal<readonly SelectedProblem[]>([]);
  readonly loading = signal(false); readonly loadError = signal(false); readonly pending = signal(false); readonly submitted = signal(false);
  readonly error = signal<string | null>(null); readonly confirmPublish = signal(false); readonly feedback = new FormFeedback<Field>();
  readonly timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  readonly form = new FormGroup({ title: new FormControl('', { nonNullable: true }), description: new FormControl('', { nonNullable: true }),
    start: new FormControl('', { nonNullable: true }), end: new FormControl('', { nonNullable: true }) });
  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      if (this.error() === 'Check the highlighted fields.' && !(['title', 'description', 'start', 'end'] as Field[]).some(f => this.validation(f))) this.error.set(null);
    });
    combineLatest([this.route.paramMap, toObservable(this.session.account), this.reload.pipe(startWith(undefined))]).pipe(switchMap(([params, account]) => {
      this.form.reset(); this.contest.set(null); this.selected.set([]); this.feedback.reset(); this.submitted.set(false); this.error.set(null); this.confirmPublish.set(false);
      const id = params.get('contestId'); this.loadError.set(false); this.loading.set(false);
      if (!account || !['Setter', 'Administrator'].includes(account.role) || id === null) return of(null);
      if (!isProblemId(id)) { this.loadError.set(true); return of(null); }
      this.loading.set(true);
      return this.api.detail(id).pipe(catchError(() => { this.loadError.set(true); return of(null); }), finalize(() => this.loading.set(false)));
    }), takeUntilDestroyed()).subscribe(contest => {
      if (!contest) return;
      if (!contest.canManage || contest.state !== 'Draft') { this.loadError.set(true); return; }
      this.contest.set(contest); this.selected.set(contest.problems);
      this.form.setValue({ title: contest.title, description: contest.description, start: localDateTime(contest.startsAtUtc), end: localDateTime(contest.endsAtUtc) });
    });
  }
  retry(): void { this.reload.next(); }
  validation(field: Field): string | null {
    const values = this.form.getRawValue();
    if (field === 'title') return !values.title.trim() || values.title.trim().length > 120 ? 'Enter a title of 1–120 characters.' : null;
    if (field === 'description') return values.description.length > 8000 ? 'Description must be at most 8,000 characters.' : null;
    const start = Date.parse(values.start), end = Date.parse(values.end);
    if (field === 'start') return !Number.isFinite(start) || start <= Date.now() ? 'Choose a future start time.' : null;
    return !Number.isFinite(end) || !Number.isFinite(start) || end - start < 60000 || end - start > 7 * 86400000 ? 'Contest duration must be between 1 minute and 7 days.' : null;
  }
  fieldError(field: Field): string | null { return this.feedback.visible(field, this.submitted()) ? this.validation(field) : null; }
  blur(field: Field): void { this.feedback.blur(field, this.validation(field) !== null); }
  save(): void {
    if (this.pending() || !this.organizer()) return;
    this.submitted.set(true); this.error.set(null);
    if ((['title', 'description', 'start', 'end'] as Field[]).some(f => this.validation(f))) { this.error.set('Check the highlighted fields.'); this.focusError(); return; }
    const values = this.form.getRawValue(), owner = this.session.account()?.id, existing = this.contest();
    this.pending.set(true);
    this.api.save(existing?.id ?? null, { title: values.title.trim(), description: values.description, startsAtUtc: new Date(values.start).toISOString(),
      endsAtUtc: new Date(values.end).toISOString(), versionIds: this.selected().map(p => p.versionId), revision: existing?.revision }).pipe(
      finalize(() => this.pending.set(false)), takeUntilDestroyed(this.destroy)).subscribe({
      next: contest => { if (this.session.account()?.id !== owner) return; this.contest.set(contest); this.form.markAsPristine(); this.feedback.reset(); this.submitted.set(false); void this.router.navigate(['/contests', contest.id]); },
      error: error => { if (this.session.account()?.id === owner) { this.error.set(contestError(error)); this.focusError(); } },
    });
  }
  publish(): void {
    const contest = this.contest(), owner = this.session.account()?.id;
    if (!contest || this.pending()) return;
    this.pending.set(true); this.error.set(null);
    this.api.publish(contest.id, contest.revision).pipe(finalize(() => this.pending.set(false)), takeUntilDestroyed(this.destroy)).subscribe({
      next: () => { if (this.session.account()?.id === owner) void this.router.navigate(['/contests', contest.id]); },
      error: error => { if (this.session.account()?.id === owner) { this.error.set(contestError(error)); this.focusError(); } },
    });
  }
  changeProblems(problems: readonly SelectedProblem[]): void { this.selected.set(problems); this.form.markAsDirty(); }
  private focusError(): void { afterNextRender(() => this.summary()?.nativeElement.focus(), { injector: this.injector }); }
}

import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, combineLatest, exhaustMap, finalize, map, merge, of, startWith, Subject, switchMap, timer } from 'rxjs';
import { SessionService } from '../../core/auth/session.service';
import { AuthPrompt } from '../../core/auth/auth-prompt.service';
import { StatePanel } from '../../shared/ui/state-panel';
import { isProblemId } from '../problems/data-access/problems-api';
import { Contest, contestError, ContestsApi, ContestSubmissionPage } from './data-access/contests-api';

@Component({ selector: 'minos-contest-detail', imports: [RouterLink, DatePipe, StatePanel], changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './contest-detail.html', styleUrl: './contests.css' })
export class ContestDetail {
  readonly session = inject(SessionService); private readonly auth = inject(AuthPrompt);
  private readonly route = inject(ActivatedRoute); private readonly api = inject(ContestsApi); private readonly destroy = inject(DestroyRef);
  private readonly reload = new Subject<void>(); readonly pending = signal(false); readonly error = signal<string | null>(null);
  readonly history = signal<ContestSubmissionPage | null>(null); readonly historyError = signal(false); readonly historyPending = signal(false);
  readonly state = toSignal(combineLatest([this.route.paramMap, toObservable(this.session.account)]).pipe(
    switchMap(([params]) => {
      this.history.set(null); this.error.set(null);
      const id = params.get('contestId');
      if (!isProblemId(id)) return of({ kind: 'error' as const, data: null });
      return merge(timer(0, 15000), this.reload).pipe(exhaustMap(() => this.api.detail(id).pipe(
        map(data => ({ kind: 'ready' as const, data })), catchError(() => of({ kind: 'error' as const, data: null })))), startWith({ kind: 'loading' as const, data: null }));
    }),
  ), { initialValue: { kind: 'loading' as const, data: null } });
  retry(): void { this.reload.next(); }
  signIn(): void { this.auth.open(); }
  join(): void {
    const contest = this.state().data, owner = this.session.account()?.id;
    if (!contest || !owner || this.pending()) return;
    this.pending.set(true); this.error.set(null);
    this.api.register(contest.id).pipe(finalize(() => this.pending.set(false)), takeUntilDestroyed(this.destroy)).subscribe({
      next: () => { if (this.session.account()?.id === owner) this.retry(); },
      error: error => { if (this.session.account()?.id === owner) this.error.set(contestError(error)); },
    });
  }
  loadHistory(page = 1): void {
    const contest = this.state().data, owner = this.session.account()?.id;
    if (!contest || !owner || this.historyPending()) return;
    this.historyError.set(false); this.historyPending.set(true);
    this.api.submissions(contest.id, page).pipe(finalize(() => this.historyPending.set(false)), takeUntilDestroyed(this.destroy)).subscribe({
      next: history => { if (this.session.account()?.id === owner) this.history.set(history); },
      error: () => { if (this.session.account()?.id === owner) this.historyError.set(true); },
    });
  }
  label(order: number): string { return String.fromCharCode(64 + order); }
  problemTitle(id: string): string { return this.state().data?.problems.find(p => p.id === id)?.title ?? 'Contest problem'; }
}

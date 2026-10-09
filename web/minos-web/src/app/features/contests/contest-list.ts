import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, combineLatest, map, of, startWith, Subject, switchMap } from 'rxjs';
import { SessionService } from '../../core/auth/session.service';
import { StatePanel } from '../../shared/ui/state-panel';
import { ContestPage, ContestsApi } from './data-access/contests-api';

@Component({ selector: 'minos-contest-list', imports: [RouterLink, DatePipe, StatePanel], changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './contest-list.html', styleUrl: './contests.css' })
export class ContestList {
  readonly session = inject(SessionService);
  readonly organizer = computed(() => ['Setter', 'Administrator'].includes(this.session.account()?.role ?? ''));
  private readonly route = inject(ActivatedRoute); private readonly api = inject(ContestsApi); private readonly reload = new Subject<void>();
  readonly query = toSignal(this.route.queryParamMap.pipe(map(p => ({ mine: p.get('view') === 'mine', page: Math.max(1, Math.min(100000, Number(p.get('page')) || 1)) }))), { initialValue: { mine: false, page: 1 } });
  readonly state = toSignal(combineLatest([toObservable(this.query), toObservable(this.organizer), this.reload.pipe(startWith(undefined))]).pipe(
    switchMap(([query, organizer]) => query.mine && !organizer ? of({ kind: 'denied' as const, data: null }) : this.api.list(query.mine, query.page).pipe(
      map(data => ({ kind: 'ready' as const, data })), catchError(() => of({ kind: 'error' as const, data: null })), startWith({ kind: 'loading' as const, data: null }))),
  ), { initialValue: { kind: 'loading' as const, data: null } });
  retry(): void { this.reload.next(); }
}

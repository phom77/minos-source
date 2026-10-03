import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, combineLatest, map, of, startWith, Subject, switchMap, tap } from 'rxjs';
import { Title } from '@angular/platform-browser';

import { StatePanel } from '../../../shared/ui/state-panel';
import { UiIcon } from '../../../shared/ui/ui-icon';
import { isProblemId, PublishedProblem, ProblemsApi } from '../data-access/problems-api';

type DetailState = { readonly status: 'loading' | 'invalid' | 'missing' | 'error' } | { readonly status: 'ready'; readonly data: PublishedProblem };
@Component({
  selector: 'minos-problem-detail-page', imports: [DatePipe, RouterLink, StatePanel, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './problem-detail-page.html', styleUrl: './problem-detail-page.css',
})
export class ProblemDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ProblemsApi);
  private readonly title = inject(Title);
  private readonly reload = new Subject<void>();
  readonly immutable = this.route.snapshot.data['immutable'] === true;
  readonly state = toSignal(combineLatest([this.route.paramMap, this.reload.pipe(startWith(undefined))]).pipe(
    switchMap(([params]) => {
      const id = params.get(this.immutable ? 'versionId' : 'problemId');
      if (!isProblemId(id)) return of<DetailState>({ status: 'invalid' });
      return (this.immutable ? this.api.version(id) : this.api.latest(id)).pipe(
        map((data): DetailState => ({ status: 'ready', data })),
        catchError((error: { status?: number }) => of<DetailState>({ status: error.status === 404 ? 'missing' : 'error' })),
        startWith<DetailState>({ status: 'loading' }),
      );
    }),
    tap(state => this.title.setTitle(state.status === 'ready' ? `${state.data.title} — Minos` : 'Problem — Minos')),
  ), { initialValue: { status: 'loading' } as DetailState });
  retry(): void { this.reload.next(); }
}

import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, distinctUntilChanged, map, of, startWith, Subject, switchMap } from 'rxjs';

import { SectionHeading } from '../../shared/ui/section-heading';
import { ForestLandscape } from '../../shared/ui/forest-landscape';
import { StatePanel } from '../../shared/ui/state-panel';
import { StatusBadge } from '../../shared/ui/status-badge';
import { Pagination } from '../../shared/ui/pagination';
import { ProblemPage, ProblemsApi } from './data-access/problems-api';
import { readCatalogQuery } from './catalog-query';

type CatalogState = { readonly status: 'loading' | 'invalid' | 'error' } | { readonly status: 'ready'; readonly data: ProblemPage };

@Component({
  selector: 'minos-problem-catalog-page',
  imports: [DatePipe, RouterLink, SectionHeading, ForestLandscape, StatePanel, StatusBadge, Pagination],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './problem-catalog-page.html',
  styleUrl: './problem-catalog-page.css',
})
export class ProblemCatalogPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ProblemsApi);
  private readonly reload = new Subject<void>();
  private readonly query = this.route.queryParamMap.pipe(map(readCatalogQuery), distinctUntilChanged((a, b) => a?.page === b?.page && a?.pageSize === b?.pageSize));
  readonly state = toSignal(combineLatest([this.query, this.reload.pipe(startWith(undefined))]).pipe(
    switchMap(([query]) => query === null ? of<CatalogState>({ status: 'invalid' }) : this.api.list(query.page, query.pageSize).pipe(
      map((data): CatalogState => ({ status: 'ready', data })),
      catchError(() => of<CatalogState>({ status: 'error' })),
      startWith<CatalogState>({ status: 'loading' }),
    )),
  ), { initialValue: { status: 'loading' } as CatalogState });
  readonly pageSizes = [10, 20, 50, 100];
  retry(): void { this.reload.next(); }
  changePageSize(event: Event): void {
    const pageSize = Number((event.target as HTMLSelectElement).value);
    void this.router.navigate([], { relativeTo: this.route, queryParams: { page: 1, pageSize }, queryParamsHandling: 'merge' });
  }
}

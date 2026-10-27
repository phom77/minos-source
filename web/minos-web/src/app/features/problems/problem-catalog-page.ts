import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, distinctUntilChanged, map, of, startWith, Subject, switchMap } from 'rxjs';

import { ProblemTopicBrowser } from './problem-topic-browser';
import { UiIcon } from '../../shared/ui/ui-icon';
import { StatePanel } from '../../shared/ui/state-panel';
import { Pagination } from '../../shared/ui/pagination';
import { ProblemPage, ProblemsApi } from './data-access/problems-api';
import { CatalogSort, readCatalogQuery } from './catalog-query';
import { SessionService } from '../../core/auth/session.service';
import { AuthPrompt } from '../../core/auth/auth-prompt.service';

type FilterField = 'difficulty' | 'topic' | 'status';
type CatalogState = { readonly status: 'loading' | 'invalid' | 'error' | 'sign-in' } | { readonly status: 'ready'; readonly data: ProblemPage; readonly accountId: string | null };

@Component({
  selector: 'minos-problem-catalog-page',
  imports: [RouterLink, UiIcon, ProblemTopicBrowser, StatePanel, Pagination],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:resize)': 'closePanels(false)' },
  templateUrl: './problem-catalog-page.html',
  styleUrl: './problem-catalog-page.css',
})
export class ProblemCatalogPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly api = inject(ProblemsApi);
  readonly session = inject(SessionService);
  readonly auth = inject(AuthPrompt);
  readonly showTopics = signal(false);
  readonly filterOpen = signal(false);
  readonly sortOpen = signal(false);
  readonly visibleFilters = signal<readonly FilterField[]>(['status', 'difficulty', 'topic']);
  readonly fields = [
    { key: 'status' as const, label: 'Status', icon: 'check' as const },
    { key: 'difficulty' as const, label: 'Difficulty', icon: 'difficulty' as const },
    { key: 'topic' as const, label: 'Topics', icon: 'tag' as const },
  ];
  readonly sorts: readonly { value: CatalogSort; label: string }[] = [
    { value: 'Number', label: 'Problem number' }, { value: 'Newest', label: 'Newest' },
    { value: 'DifficultyAscending', label: 'Difficulty: easy first' },
    { value: 'DifficultyDescending', label: 'Difficulty: hard first' }, { value: 'Title', label: 'Title' },
  ];
  private readonly filterPanel = viewChild.required<ElementRef<HTMLElement>>('filterPanel');
  private readonly sortPanel = viewChild.required<ElementRef<HTMLElement>>('sortPanel');
  private opener: HTMLElement | null = null;
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  constructor() {
    inject(DestroyRef).onDestroy(() => { clearTimeout(this.searchTimer); this.closePanels(false); });
  }
  private readonly reload = new Subject<void>();
  private readonly topicReload = new Subject<void>();
  readonly query = toSignal(this.route.queryParamMap.pipe(map(readCatalogQuery), distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b))),
    { initialValue: readCatalogQuery(this.route.snapshot.queryParamMap) });
  readonly hasFilters = computed(() => { const query = this.query(); return !!(query?.q || query?.difficulty || query?.topic || query?.status || (query?.sort && query.sort !== 'Number')); });
  readonly topics = toSignal(this.topicReload.pipe(startWith(undefined), switchMap(() => this.api.topics().pipe(
    map(items => ({ items, error: false })), catchError(() => of({ items: [], error: true })),
  ))), { initialValue: { items: [], error: false } });
  readonly rankedTopics = computed(() => [...this.topics().items].sort((a, b) => b.problemCount - a.problemCount || a.displayName.localeCompare(b.displayName)));
  readonly activeFilters = computed(() => this.fields.flatMap(field => {
    const query = this.query(), value = query?.[field.key];
    if (!value) return [];
    const label = field.key === 'topic' ? this.topics().items.find(topic => topic.slug === value)?.displayName ?? value
      : value === 'NotAttempted' ? 'Not attempted' : value;
    return [{ field: field.key, label: `${field.label} ${this.isNot(field.key) ? 'is not' : 'is'} ${label}` }];
  }));
  readonly missingSelectedTopic = computed(() => !!this.query()?.topic && !this.topics().items.some(topic => topic.slug === this.query()?.topic));
  private readonly loadedState = toSignal(combineLatest([toObservable(this.query), toObservable(this.session.account), this.reload.pipe(startWith(undefined))]).pipe(
    switchMap(([query, account]) => query === null ? of<CatalogState>({ status: 'invalid' }) : query.status && !account
      ? of<CatalogState>({ status: 'sign-in' }) : this.api.list(query.page, query.pageSize, this.filters(query)).pipe(
      map((data): CatalogState => ({ status: 'ready', data, accountId: account?.id ?? null })),
      catchError((error: { status?: number }) => { if (error.status === 401) this.session.signedOut('expired'); return of<CatalogState>({ status: 'error' }); }),
      startWith<CatalogState>({ status: 'loading' }),
    )),
  ), { initialValue: { status: 'loading' } as CatalogState });
  readonly state = computed<CatalogState>(() => {
    const value = this.loadedState();
    return value.status === 'ready' && value.accountId !== (this.session.account()?.id ?? null) ? { status: 'loading' } : value;
  });
  readonly pageSizes = [10, 20, 50, 100];
  readonly maximumProgress = (total: number) => Math.max(1, total);
  retry(): void { this.reload.next(); }
  retryTopics(): void { this.topicReload.next(); }
  searchChanged(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.commit({ q: value.trim() || null }), 300);
  }
  submitSearch(event: Event, value: string): void {
    event.preventDefault(); clearTimeout(this.searchTimer); this.commit({ q: value.trim() || null });
  }
  updateFilter(field: FilterField, value: string): void {
    this.commit({ [field]: value || null, ...(!value ? { [field + 'IsNot']: null } : {}) });
  }
  isNot(field: FilterField): boolean { return this.query()?.[`${field}IsNot`] === 'true'; }
  updateOperator(field: FilterField, value: string): void { this.commit({ [field + 'IsNot']: value === 'true' ? 'true' : null }); }
  updateMatch(value: string): void { this.commit({ match: value === 'Any' ? 'Any' : null }); }
  removeFilter(field: FilterField): void {
    this.visibleFilters.update(fields => fields.filter(key => key !== field)); this.updateFilter(field, '');
    afterNextRender(() => this.filterPanel().nativeElement.querySelector<HTMLElement>(`[data-add-filter="${field}"]`)?.focus(), { injector: this.injector });
  }
  addFilter(field: FilterField): void {
    this.visibleFilters.update(fields => [...fields, field]);
    afterNextRender(() => this.filterPanel().nativeElement.querySelector<HTMLElement>(`#filter-${field}`)?.focus(), { injector: this.injector });
  }
  resetFilters(): void {
    this.visibleFilters.set(['status', 'difficulty', 'topic']);
    this.commit({ difficulty: null, topic: null, status: null, match: null, difficultyIsNot: null, topicIsNot: null, statusIsNot: null });
  }
  clearFilters(): void {
    clearTimeout(this.searchTimer);
    this.commit({ q: null, difficulty: null, topic: null, status: null, sort: null, match: null, difficultyIsNot: null, topicIsNot: null, statusIsNot: null });
  }
  selectTopic(topic: string): void { this.commit({ topic: this.query()?.topic === topic && !this.isNot('topic') ? null : topic, topicIsNot: null }); }
  selectSort(sort: CatalogSort): void { this.commit({ sort: sort === 'Number' ? null : sort }); this.closePanels(); }
  private commit(queryParams: Record<string, string | number | null>): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { page: 1, ...queryParams }, queryParamsHandling: 'merge' });
  }
  openPanel(kind: 'filter' | 'sort', event: Event): void {
    const wasOpen = kind === 'filter' ? this.filterOpen() : this.sortOpen();
    this.closePanels(false); if (wasOpen) return;
    this.opener = event.currentTarget as HTMLElement;
    const panel = (kind === 'filter' ? this.filterPanel : this.sortPanel)().nativeElement;
    const rect = this.opener.getBoundingClientRect();
    panel.showPopover({ source: this.opener });
    panel.style.left = Math.max(16, Math.min(rect.left, window.innerWidth - panel.offsetWidth - 16)) + 'px';
    panel.style.top = Math.max(16, Math.min(rect.bottom + 10, window.innerHeight - panel.offsetHeight - 16)) + 'px';
    (kind === 'filter' ? this.filterOpen : this.sortOpen).set(true);
    panel.querySelector<HTMLElement>('select, button')?.focus();
  }
  panelToggled(kind: 'filter' | 'sort', event: Event): void {
    (kind === 'filter' ? this.filterOpen : this.sortOpen).set((event as ToggleEvent).newState === 'open');
  }
  dismissPanel(event: Event): void { event.preventDefault(); this.closePanels(); }
  closePanels(restoreFocus = true): void {
    for (const panel of [this.filterPanel(), this.sortPanel()]) if (typeof panel.nativeElement.hidePopover === 'function' && panel.nativeElement.matches(':popover-open')) panel.nativeElement.hidePopover();
    this.filterOpen.set(false); this.sortOpen.set(false);
    if (restoreFocus && this.opener?.isConnected) this.opener.focus({ preventScroll: true });
  }
  libraryReturnUrl(): string { return this.router.createUrlTree(['/problems'], { queryParams: this.query() ?? {} }).toString(); }
  private filters(query: NonNullable<ReturnType<typeof readCatalogQuery>>) {
    const { page: _page, pageSize: _pageSize, ...filters } = query;
    return filters;
  }
  changePageSize(event: Event): void {
    const pageSize = Number((event.target as HTMLSelectElement).value);
    void this.router.navigate([], { relativeTo: this.route, queryParams: { page: 1, pageSize }, queryParamsHandling: 'merge' });
  }
}

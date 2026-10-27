import { afterNextRender, ChangeDetectionStrategy, Component, computed, effect, inject, Injector, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink, UrlTree } from '@angular/router';
import { catchError, combineLatest, map, of, startWith, Subject, switchMap, tap } from 'rxjs';
import { Title } from '@angular/platform-browser';

import { StatePanel } from '../../../shared/ui/state-panel';
import { UiIcon } from '../../../shared/ui/ui-icon';
import { ProblemStatement } from '../../../shared/ui/problem-statement';
import { ProblemComments } from '../../comments/problem-comments';
import { isProblemId, PublishedProblem, ProblemsApi } from '../data-access/problems-api';
import { SubmissionForm } from '../../submissions/submission-form/submission-form';
import { ProblemSubmissions } from '../../submissions/problem-submissions/problem-submissions';
import { readCatalogQuery } from '../catalog-query';

type DetailState = { readonly status: 'loading' | 'invalid' | 'missing' | 'error' } | { readonly status: 'ready'; readonly data: PublishedProblem };
@Component({
  selector: 'minos-problem-detail-page', imports: [RouterLink, StatePanel, UiIcon, SubmissionForm, ProblemStatement, ProblemSubmissions, ProblemComments],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './problem-detail-page.html', styleUrl: './problem-detail-page.css',
})
export class ProblemDetailPage {
  readonly editorExpanded = signal(false);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly workspaceQuery = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });
  readonly workspaceTab = computed(() => this.workspaceQuery().get('tab') === 'submissions' ? 'submissions' : 'description');
  readonly admissionRevision = signal(0);
  readonly libraryReturn = toSignal(this.route.queryParamMap.pipe(map(params => this.readLibraryReturn(params.get('library')))),
    { initialValue: this.router.parseUrl('/problems') });
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
  constructor() {
    effect(() => { if (this.workspaceQuery().get('tab') === 'submissions') this.editorExpanded.set(false); });
  }
  retry(): void { this.reload.next(); }
  recordedSubmission(): void { this.admissionRevision.update(value => value + 1); }
  workspaceKey(event: KeyboardEvent): void {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const tab = event.key === 'Home' ? 'description' : event.key === 'End' ? 'submissions'
      : this.workspaceTab() === 'description' ? 'submissions' : 'description';
    void this.router.navigate([], { relativeTo: this.route, queryParams: { tab: tab === 'description' ? null : tab, submission: null }, queryParamsHandling: 'merge' })
      .then(() => this.document.getElementById('problem-tab-' + tab)?.focus());
  }
  private readLibraryReturn(value: string | null): UrlTree {
    if (value && value.length <= 1024 && value.startsWith('/problems?') && !/[\\\u0000-\u001f]/.test(value)) {
      try {
        const tree = this.router.parseUrl(value);
        const segments = tree.root.children['primary']?.segments;
        if (segments?.length === 1 && segments[0].path === 'problems' && !Object.keys(segments[0].parameters).length &&
          Object.keys(tree.root.children).length === 1 && readCatalogQuery(tree.queryParamMap)) {
          const query = readCatalogQuery(tree.queryParamMap)!;
          return this.router.createUrlTree(['/problems'], { queryParams: { ...query } });
        }
      } catch { /* Invalid return URLs fall back to the public library. */ }
    }
    return this.router.parseUrl('/problems');
  }
  expandEditor(): void {
    this.editorExpanded.update(value => !value);
    afterNextRender(() => this.document.getElementById('submission-source')?.focus(), { injector: this.injector });
  }
  focusCode(event: Event): void { event.preventDefault(); this.document.getElementById('submission-source')?.focus(); }
}

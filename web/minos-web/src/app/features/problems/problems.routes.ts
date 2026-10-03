import { Routes } from '@angular/router';

export const problemsRoutes: Routes = [
  { path: 'versions/:versionId', title: 'Problem — Minos', data: { immutable: true }, loadComponent: () => import('./problem-detail/problem-detail-page').then(({ ProblemDetailPage }) => ProblemDetailPage) },
  { path: ':problemId', title: 'Problem — Minos', loadComponent: () => import('./problem-detail/problem-detail-page').then(({ ProblemDetailPage }) => ProblemDetailPage) },
  {
    path: '',
    pathMatch: 'full',
    title: 'Problems — Minos',
    loadComponent: () =>
      import('./problem-catalog-page').then(({ ProblemCatalogPage }) => ProblemCatalogPage),
  },
];

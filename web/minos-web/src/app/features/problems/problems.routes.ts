import { Routes } from '@angular/router';

export const problemsRoutes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Problems — Minos',
    loadComponent: () =>
      import('./problem-catalog-page').then(({ ProblemCatalogPage }) => ProblemCatalogPage),
  },
];

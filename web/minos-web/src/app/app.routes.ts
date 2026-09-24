import { Routes } from '@angular/router';

import { AppShell } from './layouts/app-shell';

export const routes: Routes = [
  {
    path: '',
    component: AppShell,
    children: [
      {
        path: '',
        pathMatch: 'full',
        redirectTo: 'problems',
      },
      {
        path: 'problems',
        title: 'Problems — Minos',
        loadComponent: () =>
          import('./features/problems/problem-catalog-page').then(
            ({ ProblemCatalogPage }) => ProblemCatalogPage,
          ),
      },
      {
        path: 'account',
        title: 'Account — Minos',
        loadComponent: () =>
          import('./features/accounts/account-entry-page').then(
            ({ AccountEntryPage }) => AccountEntryPage,
          ),
      },
      {
        path: '**',
        redirectTo: 'problems',
      },
    ],
  },
];

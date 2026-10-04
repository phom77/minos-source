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
        loadChildren: () =>
          import('./features/problems/problems.routes').then(
            ({ problemsRoutes }) => problemsRoutes,
          ),
      },
      {
        path: 'settings',
        loadChildren: () => import('./features/accounts/accounts.routes').then(({ settingsRoutes }) => settingsRoutes),
      },
      {
        path: 'account',
        loadChildren: () =>
          import('./features/accounts/accounts.routes').then(
            ({ accountsRoutes }) => accountsRoutes,
          ),
      },
      {
        path: '**',
        title: 'Page not found — Minos',
        loadComponent: () =>
          import('./layouts/not-found-page').then(({ NotFoundPage }) => NotFoundPage),
      },
    ],
  },
];

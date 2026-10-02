import { Routes } from '@angular/router';

export const accountsRoutes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Account — Minos',
    loadComponent: () =>
      import('./account-entry-page').then(({ AccountEntryPage }) => AccountEntryPage),
  },
];

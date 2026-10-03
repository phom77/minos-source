import { Routes } from '@angular/router';
import { inject } from '@angular/core';
import { authenticatedGuard } from '../../core/auth/auth.guard';
import { AuthPrompt } from '../../core/auth/auth-prompt.service';

export const accountsRoutes: Routes = [
  {
    path: 'login', pathMatch: 'full',
    redirectTo: ({ queryParams }) => inject(AuthPrompt).entryTree('login', queryParams['returnUrl'] ?? '/account',
      queryParams['expired'] === '1' ? 'expired' : queryParams['signedOut'] === '1' ? 'signed-out' : ''),
  },
  {
    path: 'register', pathMatch: 'full',
    redirectTo: ({ queryParams }) => inject(AuthPrompt).entryTree('register', queryParams['returnUrl'] ?? '/account'),
  },
  {
    path: '',
    pathMatch: 'full',
    title: 'Account — Minos',
    canActivate: [authenticatedGuard],
    loadComponent: () =>
      import('./account-entry-page').then(({ AccountEntryPage }) => AccountEntryPage),
  },
];

import { Routes } from '@angular/router';
import { inject } from '@angular/core';
import { authenticatedGuard } from '../../core/auth/auth.guard';
import { AuthPrompt } from '../../core/auth/auth-prompt.service';

export const accountsRoutes: Routes = [
  {
    path: 'login', pathMatch: 'full',
    redirectTo: ({ queryParams }) => inject(AuthPrompt).entryTree('login', queryParams['returnUrl'] ?? '/account',
      queryParams['notice'] === 'password-changed' ? 'password-changed' : queryParams['notice'] === 'account-disabled' ? 'account-disabled'
        : queryParams['expired'] === '1' ? 'expired' : queryParams['signedOut'] === '1' ? 'signed-out' : ''),
  },
  {
    path: 'register', pathMatch: 'full',
    redirectTo: ({ queryParams }) => inject(AuthPrompt).entryTree('register', queryParams['returnUrl'] ?? '/account'),
  },
  {
    path: '',
    pathMatch: 'full',
    redirectTo: '/settings/profile',
  },
];

export const settingsRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'profile' },
  ...(['profile', 'security', 'account'] as const).map((section) => ({
    path: section,
    title: 'Settings — Minos',
    data: { section },
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./account-entry-page').then(({ AccountEntryPage }) => AccountEntryPage),
  })),
];

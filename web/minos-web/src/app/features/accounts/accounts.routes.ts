import { Routes } from '@angular/router';
import { authenticatedGuard } from '../../core/auth/auth.guard';

export const accountsRoutes: Routes = [
  {
    path: 'login', title: 'Sign in — Minos', data: { mode: 'login' },
    loadComponent: () => import('./auth-page/auth-page').then(({ AuthPage }) => AuthPage),
  },
  {
    path: 'register', title: 'Create account — Minos', data: { mode: 'register' },
    loadComponent: () => import('./auth-page/auth-page').then(({ AuthPage }) => AuthPage),
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

import { Routes } from '@angular/router';
import { authenticatedGuard } from '../../core/auth/auth.guard';

export const contestsRoutes: Routes = [
  { path: ':contestId/problems/new', canActivate: [authenticatedGuard], title: 'Create problem — Minos', loadComponent: () => import('./contest-problem-authoring').then(m => m.ContestProblemAuthoring) },
  { path: '', title: 'Contests — Minos', loadComponent: () => import('./contest-list').then(m => m.ContestList) },
  { path: 'new', canActivate: [authenticatedGuard], title: 'Create contest — Minos', loadComponent: () => import('./contest-editor').then(m => m.ContestEditor) },
  { path: ':contestId/edit', canActivate: [authenticatedGuard], title: 'Edit contest — Minos', loadComponent: () => import('./contest-editor').then(m => m.ContestEditor) },
  { path: ':contestId/problems/:contestProblemId', canActivate: [authenticatedGuard], title: 'Contest problem — Minos', loadComponent: () => import('./contest-solve').then(m => m.ContestSolve) },
  { path: ':contestId', title: 'Contest — Minos', loadComponent: () => import('./contest-detail').then(m => m.ContestDetail) },
];

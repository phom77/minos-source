import { Routes } from '@angular/router';
import { authenticatedGuard } from '../../core/auth/auth.guard';

export const submissionsRoutes: Routes = [
  { path: ':submissionId', title: 'Submission — Minos', canActivate: [authenticatedGuard], loadComponent: () => import('./submission-detail/submission-detail-page').then(m => m.SubmissionDetailPage) },
  { path: '', pathMatch: 'full', title: 'Submissions — Minos', canActivate: [authenticatedGuard], loadComponent: () => import('./submission-history/submission-history-page').then(m => m.SubmissionHistoryPage) },
];

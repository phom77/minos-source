import { DatePipe } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

import { SessionService } from '../../core/auth/session.service';
import { SectionHeading } from '../../shared/ui/section-heading';
import { StatePanel } from '../../shared/ui/state-panel';
import { StatusBadge } from '../../shared/ui/status-badge';
import { UiIcon } from '../../shared/ui/ui-icon';
import { AccountsApi } from './data-access/accounts-api';
import { authErrorMessage } from './account-error-message';

@Component({
  selector: 'minos-account-entry-page',
  imports: [DatePipe, RouterLink, SectionHeading, StatePanel, StatusBadge, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './account-entry-page.html',
  styleUrl: './account-entry-page.css',
})
export class AccountEntryPage {
  readonly session = inject(SessionService);
  private readonly api = inject(AccountsApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly resultFocus = viewChild<ElementRef<HTMLElement>>('resultFocus');
  readonly pending = signal(false);
  readonly error = signal<string | null>(null);

  retry(): void {
    this.session.refresh().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      afterNextRender(() => this.resultFocus()?.nativeElement.focus(), { injector: this.injector });
    });
  }

  signOut(): void {
    if (this.pending()) return;
    this.pending.set(true);
    this.error.set(null);
    this.api.signOut().pipe(
      finalize(() => this.pending.set(false)), takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => { void this.router.navigate(['/account/login'], { queryParams: { signedOut: '1' } }); },
      error: (error: unknown) => {
        if (this.session.state().status === 'guest') {
          void this.router.navigate(['/account/login'], { queryParams: { expired: '1', returnUrl: '/account' } });
        } else this.error.set(authErrorMessage(error, 'logout'));
      },
    });
  }
}

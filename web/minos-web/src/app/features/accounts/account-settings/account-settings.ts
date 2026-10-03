import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { CurrentAccount } from '../../../core/auth/session.models';
import { SessionService } from '../../../core/auth/session.service';
import { StatusBadge } from '../../../shared/ui/status-badge';
import { UiIcon } from '../../../shared/ui/ui-icon';
import { AccountsApi } from '../data-access/accounts-api';
import { authErrorMessage } from '../account-error-message';
import { ProfileForm } from '../profile-form/profile-form';
import { PasswordForm } from '../password-form/password-form';
import { DisableForm } from '../disable-form/disable-form';

type Operation = 'profile' | 'password' | 'disable' | 'logout';

@Component({
  selector: 'minos-account-settings', imports: [DatePipe, RouterLink, StatusBadge, UiIcon, ProfileForm, PasswordForm, DisableForm],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './account-settings.html', styleUrl: './account-settings.css',
})
export class AccountSettings {
  private readonly api = inject(AccountsApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly account = input.required<CurrentAccount>();
  readonly busy = signal<Operation | null>(null);
  readonly logoutError = signal<string | null>(null);

  blocked(operation: Operation): boolean { return this.busy() !== null && this.busy() !== operation; }
  setPending(operation: Operation, pending: boolean): void {
    if (pending) this.busy.set(operation);
    else if (this.busy() === operation) this.busy.set(null);
  }

  securityCompleted(operation: 'password' | 'disable'): void {
    void this.router.navigate(['/account/login'], { queryParams: {
      notice: operation === 'password' ? 'password-changed' : 'account-disabled', returnUrl: '/account',
    }, replaceUrl: true });
  }

  signOut(): void {
    if (this.busy()) return;
    this.busy.set('logout'); this.logoutError.set(null);
    this.api.signOut().pipe(finalize(() => this.busy.set(null)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { void this.router.navigate(['/account/login'], { queryParams: { signedOut: '1' } }); },
      error: (error: unknown) => {
        if (this.session.state().status === 'guest') {
          void this.router.navigate(['/account/login'], { queryParams: { expired: '1', returnUrl: '/account' } });
        } else this.logoutError.set(authErrorMessage(error, 'logout'));
      },
    });
  }
}

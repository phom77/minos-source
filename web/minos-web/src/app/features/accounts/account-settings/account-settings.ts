import { DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, inject, Injector, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { CurrentAccount } from '../../../core/auth/session.models';
import { SessionService } from '../../../core/auth/session.service';
import { AccountsApi } from '../data-access/accounts-api';
import { authErrorMessage } from '../account-error-message';
import { ProfileForm } from '../profile-form/profile-form';
import { PasswordForm } from '../password-form/password-form';
import { DisableForm } from '../disable-form/disable-form';

type Editor = 'profile' | 'password' | 'disable';
type Operation = Editor | 'logout';

@Component({
  selector: 'minos-account-settings', imports: [RouterLink, RouterLinkActive, ProfileForm, PasswordForm, DisableForm],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './account-settings.html', styleUrl: './account-settings.css',
})
export class AccountSettings {
  private readonly api = inject(AccountsApi);
  readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  readonly account = input.required<CurrentAccount>();
  readonly section = input<'profile' | 'security' | 'account'>('profile');
  readonly editor = signal<Editor | null>(null);
  readonly saved = signal(false);
  readonly busy = signal<Operation | null>(null);
  readonly logoutError = signal<string | null>(null);
  private opener: HTMLElement | null = null;

  openEditor(editor: Editor, opener: HTMLElement): void {
    if (this.busy() || (editor !== 'profile' && !this.session.passwordConfirmed())) return;
    this.opener = opener; this.saved.set(false); this.editor.set(editor);
    afterNextRender(() => this.document.getElementById(editor === 'profile' ? 'profile-name' : editor === 'password' ? 'change-current' : 'disable-password')?.focus(), { injector: this.injector });
  }

  closeEditor(): void {
    this.editor.set(null);
    afterNextRender(() => this.opener?.isConnected && this.opener.focus(), { injector: this.injector });
  }
  profileSaved(): void { this.saved.set(true); this.closeEditor(); }
  blocked(operation: Operation): boolean { return this.busy() !== null && this.busy() !== operation; }
  setPending(operation: Operation, pending: boolean): void {
    if (pending) this.busy.set(operation);
    else if (this.busy() === operation) this.busy.set(null);
  }

  securityCompleted(operation: 'password' | 'disable'): void {
    void this.router.navigate(['/account/login'], { queryParams: {
      notice: operation === 'password' ? 'password-changed' : 'account-disabled', returnUrl: '/settings/security',
    }, replaceUrl: true });
  }

  signOut(): void {
    if (this.busy()) return;
    this.busy.set('logout'); this.logoutError.set(null);
    this.api.signOut().pipe(finalize(() => this.busy.set(null)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { void this.router.navigate(['/account/login'], { queryParams: { signedOut: '1' } }); },
      error: (error: unknown) => {
        if (this.session.state().status === 'guest') {
          void this.router.navigate(['/account/login'], { queryParams: { expired: '1', returnUrl: '/settings/security' } });
        } else this.logoutError.set(authErrorMessage(error, 'logout'));
      },
    });
  }
}

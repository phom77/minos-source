import { Component, inject, signal } from '@angular/core';
import { SessionService } from '../../../core/auth/session.service';
import { AccountSettings } from '../account-settings/account-settings';

/** Test-only composition mirrors the identity-keyed settings page. */
@Component({
  imports: [AccountSettings],
  template: `@if (session.account(); as account) {
    @for (identity of [account]; track identity.id) {
      <minos-account-settings [account]="identity" [section]="section()" />
    }
  }`,
})
export class SettingsHost {
  readonly session = inject(SessionService);
  readonly section = signal<'profile' | 'security' | 'account'>('profile');
}

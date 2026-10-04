import { DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, Injector, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { SessionService } from '../../core/auth/session.service';
import { StatePanel } from '../../shared/ui/state-panel';
import { AccountSettings } from './account-settings/account-settings';

@Component({
  selector: 'minos-account-entry-page',
  imports: [RouterLink, StatePanel, AccountSettings],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './account-entry-page.html',
  styleUrl: './account-entry-page.css',
})
export class AccountEntryPage {
  readonly session = inject(SessionService);
  readonly section = inject(ActivatedRoute).snapshot.data['section'] ?? 'profile';
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly resultFocus = viewChild<ElementRef<HTMLElement>>('resultFocus');

  retry(): void {
    this.session.refresh().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      afterNextRender(() => (this.resultFocus()?.nativeElement ?? this.document.getElementById('settings-heading'))?.focus(), { injector: this.injector });
    });
  }

  private readonly document = inject(DOCUMENT);
}

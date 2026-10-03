import { DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, effect, ElementRef, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { ConfirmationDialog } from '../../../shared/ui/confirmation-dialog';
import { PasswordField } from '../../../shared/ui/password-field';
import { AccountsApi } from '../data-access/accounts-api';
import { accountSettingsError } from '../account-error-message';
import { passwordFieldError, passwordValidators } from '../account-validators';

@Component({
  selector: 'minos-disable-form', imports: [ReactiveFormsModule, PasswordField, ConfirmationDialog],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './disable-form.html', styleUrl: '../settings-form.css',
})
export class DisableForm {
  private readonly api = inject(AccountsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);
  private readonly summary = viewChild<ElementRef<HTMLElement>>('summary');
  readonly blocked = input(false);
  readonly pendingChange = output<boolean>();
  readonly completed = output<void>();
  readonly pending = signal(false);
  readonly submitted = signal(false);
  readonly confirming = signal(false);
  readonly error = signal<string | null>(null);
  readonly form = new FormGroup({ password: new FormControl('', { nonNullable: true, validators: passwordValidators }) });

  constructor() {
    effect(() => { if (this.pending() || this.blocked()) this.form.disable(); else this.form.enable(); });
    this.destroyRef.onDestroy(() => this.form.reset());
  }
  fieldError(): string | null { return passwordFieldError(this.form.controls.password, this.submitted(), 'current password'); }
  focusField(event: Event): void { event.preventDefault(); this.document.getElementById('disable-password')?.focus(); }
  fieldLink(): string { return this.router.url.split('#')[0] + '#disable-password'; }

  review(): void {
    if (this.pending() || this.blocked() || this.confirming()) return;
    this.submitted.set(true); this.error.set(null); this.form.markAllAsTouched();
    if (this.form.invalid) { this.focusSummary(); return; }
    this.confirming.set(true);
  }

  cancel(): void { if (!this.pending()) { this.confirming.set(false); this.form.reset(); this.submitted.set(false); } }

  confirm(): void {
    if (!this.confirming() || this.pending() || this.blocked() || this.form.invalid) return;
    const password = this.form.controls.password.value;
    this.pending.set(true); this.pendingChange.emit(true); this.form.disable();
    this.api.disable(password).pipe(finalize(() => { this.pending.set(false); this.pendingChange.emit(false); this.form.enable(); }),
      takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.form.reset(); this.confirming.set(false); this.submitted.set(false); this.completed.emit(); },
      error: (error: unknown) => { this.confirming.set(false); this.error.set(accountSettingsError(error, 'disable')); this.focusSummary(); },
    });
  }

  private focusSummary(): void { afterNextRender(() => this.summary()?.nativeElement.focus(), { injector: this.injector }); }
}

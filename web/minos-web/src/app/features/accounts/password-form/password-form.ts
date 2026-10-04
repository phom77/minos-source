import { DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, effect, ElementRef, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { PasswordField } from '../../../shared/ui/password-field';
import { AccountsApi } from '../data-access/accounts-api';
import { accountSettingsError } from '../account-error-message';
import { matchingNewPasswords, passwordFieldError, passwordValidators } from '../account-validators';
import { FormFeedback } from '../form-feedback';

type Field = 'currentPassword' | 'newPassword' | 'confirmation';
const fieldIds: Record<Field, string> = { currentPassword: 'change-current', newPassword: 'change-new', confirmation: 'change-confirm' };

@Component({
  selector: 'minos-password-form', imports: [ReactiveFormsModule, PasswordField],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './password-form.html', styleUrl: '../settings-form.css',
})
export class PasswordForm {
  private readonly api = inject(AccountsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);
  private readonly summary = viewChild<ElementRef<HTMLElement>>('summary');
  readonly blocked = input(false);
  readonly pendingChange = output<boolean>();
  readonly completed = output<void>();
  readonly cancelled = output<void>();
  readonly feedback = new FormFeedback<Field>();
  readonly pending = signal(false);
  readonly submitted = signal(false);
  readonly error = signal<string | null>(null);
  readonly fields: readonly Field[] = ['currentPassword', 'newPassword', 'confirmation'];
  readonly form = new FormGroup({
    currentPassword: new FormControl('', { nonNullable: true, validators: passwordValidators }),
    newPassword: new FormControl('', { nonNullable: true, validators: passwordValidators }),
    confirmation: new FormControl('', { nonNullable: true, validators: passwordValidators }),
  }, { validators: matchingNewPasswords });

  constructor() {
    effect(() => { if (this.pending() || this.blocked()) this.form.disable(); else this.form.enable(); });
    this.destroyRef.onDestroy(() => this.form.reset());
  }

  fieldError(field: Field): string | null {
    const control = this.form.controls[field];
    const visible = this.feedback.visible(field, this.submitted());
    if (field === 'confirmation' && visible && this.form.hasError('passwordMismatch')) return 'New passwords must match.';
    return passwordFieldError(control, visible, field === 'currentPassword' ? 'current password' : field === 'newPassword' ? 'new password' : 'new password confirmation');
  }
  blur(field: Field): void { this.feedback.blur(field, this.form.controls[field].invalid || (field === 'confirmation' && this.form.hasError('passwordMismatch'))); }
  cancel(): void {
    if (this.pending() || this.blocked()) return;
    this.form.reset(); this.feedback.reset(); this.submitted.set(false); this.error.set(null); this.cancelled.emit();
  }
  invalidFields(): readonly Field[] { return this.fields.filter((field) => this.fieldError(field)); }
  fieldId(field: Field): string { return fieldIds[field]; }
  fieldLink(field: Field): string { return this.router.url.split('#')[0] + '#' + this.fieldId(field); }
  focusField(event: Event, field: Field): void { event.preventDefault(); this.document.getElementById(this.fieldId(field))?.focus(); }

  submit(): void {
    if (this.pending() || this.blocked()) return;
    this.submitted.set(true); this.error.set(null); this.form.markAllAsTouched();
    if (this.form.invalid) { this.focusSummary(); return; }
    const { currentPassword, newPassword } = this.form.getRawValue();
    this.pending.set(true); this.pendingChange.emit(true); this.form.disable();
    this.api.changePassword({ currentPassword, newPassword }).pipe(
      finalize(() => { this.pending.set(false); this.pendingChange.emit(false); this.form.enable(); }), takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => { this.form.reset(); this.feedback.reset(); this.submitted.set(false); this.completed.emit(); },
      error: (error: unknown) => { this.error.set(accountSettingsError(error, 'password')); this.focusSummary(); },
    });
  }

  private focusSummary(): void { afterNextRender(() => this.summary()?.nativeElement.focus(), { injector: this.injector }); }
}

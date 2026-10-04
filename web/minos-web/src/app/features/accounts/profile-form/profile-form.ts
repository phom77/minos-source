import { DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, effect, ElementRef, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { CurrentAccount } from '../../../core/auth/session.models';
import { FieldFeedback } from '../../../shared/ui/field-feedback';
import { AccountsApi } from '../data-access/accounts-api';
import { accountSettingsError } from '../account-error-message';
import { displayNameValidator } from '../account-validators';
import { FormFeedback } from '../form-feedback';

@Component({
  selector: 'minos-profile-form', imports: [ReactiveFormsModule, FieldFeedback],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './profile-form.html',
  styleUrl: '../settings-form.css',
})
export class ProfileForm {
  private readonly api = inject(AccountsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);
  private readonly summary = viewChild<ElementRef<HTMLElement>>('summary');
  readonly account = input.required<CurrentAccount>();
  readonly blocked = input(false);
  readonly pendingChange = output<boolean>();
  readonly cancelled = output<void>();
  readonly completed = output<void>();
  readonly feedback = new FormFeedback<'displayName'>();
  readonly pending = signal(false);
  readonly submitted = signal(false);
  readonly error = signal<string | null>(null);
  readonly form = new FormGroup({ displayName: new FormControl('', { nonNullable: true, validators: displayNameValidator }) });

  constructor() {
    effect(() => { const name = this.account().displayName; if (this.form.pristine) this.form.reset({ displayName: name }); });
    effect(() => { if (this.blocked() || this.pending()) this.form.disable(); else this.form.enable(); });
  }

  fieldError(): string | null {
    const field = this.form.controls.displayName;
    if (!this.feedback.visible('displayName', this.submitted())) return null;
    if (field.hasError('required')) return 'Enter your display name.';
    if (field.hasError('maxlength')) return 'Use no more than 80 characters.';
    if (field.hasError('controlCharacters')) return 'Use a name without control characters.';
    return null;
  }

  focusField(event: Event): void { event.preventDefault(); this.document.getElementById('profile-name')?.focus(); }
  fieldLink(): string { return this.router.url.split('#')[0] + '#profile-name'; }
  trimName(): void { const field = this.form.controls.displayName; field.setValue(field.value.trim()); }
  edit(): void { this.feedback.edit('displayName'); }
  blur(): void { this.trimName(); this.feedback.blur('displayName', this.form.controls.displayName.invalid); }
  cancel(): void {
    if (this.pending() || this.blocked()) return;
    this.form.reset({ displayName: this.account().displayName }); this.feedback.reset();
    this.submitted.set(false); this.error.set(null); this.cancelled.emit();
  }

  submit(): void {
    if (this.pending() || this.blocked()) return;
    this.trimName(); this.submitted.set(true); this.error.set(null);
    this.form.markAllAsTouched();
    if (this.form.invalid) { this.focusSummary(); return; }
    const name = this.form.controls.displayName.value;
    if (name === this.account().displayName) { this.finish(name); return; }
    this.pending.set(true); this.pendingChange.emit(true); this.form.disable();
    this.api.rename(name).pipe(finalize(() => { this.pending.set(false); this.pendingChange.emit(false); this.form.enable(); }),
      takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (account) => this.finish(account.displayName),
      error: (error: unknown) => { this.error.set(accountSettingsError(error, 'profile')); this.focusSummary(); },
    });
  }

  private finish(name: string): void {
    this.form.reset({ displayName: name }); this.feedback.reset(); this.submitted.set(false);
    this.completed.emit();
  }

  private focusSummary(): void { afterNextRender(() => this.summary()?.nativeElement.focus(), { injector: this.injector }); }
}

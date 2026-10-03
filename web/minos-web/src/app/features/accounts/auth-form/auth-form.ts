import { DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, effect, ElementRef, inject, Injector, input, OnInit, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';

import { AuthMode, AuthNotice } from '../../../core/auth/auth-prompt.service';
import { SessionService } from '../../../core/auth/session.service';
import { FieldFeedback } from '../../../shared/ui/field-feedback';
import { UiIcon } from '../../../shared/ui/ui-icon';
import { AccountsApi } from '../data-access/accounts-api';
import { GoogleSignIn } from '../data-access/google-sign-in';
import { authErrorMessage } from '../account-error-message';
import { displayNameValidator, matchingPasswords } from '../account-validators';

type Field = 'email' | 'displayName' | 'password' | 'confirmation';

@Component({
  selector: 'minos-auth-form',
  imports: [ReactiveFormsModule, UiIcon, FieldFeedback],
  providers: [GoogleSignIn],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './auth-form.html',
  styleUrl: './auth-form.css',
})
export class AuthForm implements OnInit {
  private readonly router = inject(Router);
  private readonly api = inject(AccountsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private readonly summary = viewChild<ElementRef<HTMLElement>>('summary');
  readonly session = inject(SessionService);
  readonly mode = input<AuthMode>('login');
  readonly notice = input<AuthNotice>('');
  readonly completed = output<void>();
  readonly changeMode = output<AuthMode>();
  readonly google = inject(GoogleSignIn);
  get registering(): boolean { return this.mode() === 'register'; }
  readonly pending = signal(false);
  readonly submitted = signal(false);
  readonly serverError = signal<string | null>(null);
  readonly passwordVisible = signal(false);
  get fields(): readonly Field[] { return this.registering
    ? ['displayName', 'email', 'password', 'confirmation'] : ['email', 'password']; }
  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(256)] }),
    displayName: new FormControl('', { nonNullable: true }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(12), Validators.maxLength(256)] }),
    confirmation: new FormControl('', { nonNullable: true }),
  });

  constructor() {
    effect(() => { if (!this.google.waiting() && !this.pending()) this.form.enable(); });
    this.google.completed.pipe(takeUntilDestroyed()).subscribe(() => {
      this.form.reset(); this.completed.emit();
    });
  }

  ngOnInit(): void {
    if (this.registering) {
      this.form.controls.displayName.addValidators(displayNameValidator);
      this.form.controls.confirmation.addValidators(Validators.required);
      this.form.addValidators(matchingPasswords);
      this.form.controls.displayName.updateValueAndValidity();
      this.form.controls.confirmation.updateValueAndValidity();
      this.form.updateValueAndValidity();
    }
  }

  startGoogle(event: Event): void {
    if (this.pending() || this.google.waiting()) { event.preventDefault(); return; }
    this.google.start(event);
    if (this.google.waiting()) {
      this.form.controls.password.reset(); this.form.controls.confirmation.reset();
      this.form.disable();
    }
  }

  cancelGoogle(): void { this.google.stop(); this.google.error.set(null); this.form.enable(); }
  checkGoogle(): void { this.google.check(true); }

  fieldError(field: Field): string | null {
    const control = this.form.controls[field];
    if (!control.touched && !this.submitted()) return null;
    if (field === 'confirmation' && this.form.hasError('passwordMismatch')) return 'Passwords must match.';
    if (control.hasError('required')) return field === 'displayName' ? 'Enter your display name.'
      : field === 'email' ? 'Enter your email address.' : field === 'confirmation' ? 'Confirm your password.' : 'Enter your password.';
    if (control.hasError('email')) return 'Enter a valid email address.';
    if (control.hasError('minlength')) return 'Use at least 12 characters.';
    if (control.hasError('maxlength')) return field === 'email' ? 'Use no more than 256 characters.'
      : field === 'displayName' ? 'Use no more than 80 characters.' : 'Use no more than 256 characters.';
    if (control.hasError('controlCharacters')) return 'Use a name without control characters.';
    return null;
  }

  invalidFields(): readonly Field[] { return this.fields.filter((field) => this.fieldError(field) !== null); }

  fieldLink(field: Field): string { return this.router.url.split('#')[0] + '#' + field; }

  trimField(field: 'email' | 'displayName'): void {
    const control = this.form.controls[field];
    control.setValue(control.value.trim());
  }

  focusField(event: Event, field: Field): void {
    event.preventDefault();
    this.document.getElementById(field)?.focus();
  }

  submit(): void {
    if (this.pending() || this.google.waiting()) return;
    this.trimField('email');
    this.trimField('displayName');
    this.submitted.set(true);
    this.serverError.set(null);
    this.form.markAllAsTouched();
    if (this.form.invalid) { this.focusSummary(); return; }

    const values = this.form.getRawValue();
    const request = this.registering ? this.api.register({ email: values.email, password: values.password, displayName: values.displayName })
      : this.api.signIn({ email: values.email, password: values.password });
    this.pending.set(true);
    this.form.disable();
    request.pipe(
      finalize(() => { this.pending.set(false); this.form.enable(); }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.form.reset(); // Never retain password fields after a successful identity change.
        this.submitted.set(false);
        this.completed.emit();
      },
      error: (error: unknown) => {
        this.serverError.set(authErrorMessage(error, this.registering ? 'register' : 'login'));
        this.focusSummary();
      },
    });
  }

  private focusSummary(): void {
    afterNextRender(() => this.summary()?.nativeElement.focus(), { injector: this.injector });
  }
}

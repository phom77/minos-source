import { DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

import { safeReturnUrl } from '../../../core/auth/safe-return-url';
import { SessionService } from '../../../core/auth/session.service';
import { FieldFeedback } from '../../../shared/ui/field-feedback';
import { SectionHeading } from '../../../shared/ui/section-heading';
import { ForestLandscape } from '../../../shared/ui/forest-landscape';
import { UiIcon } from '../../../shared/ui/ui-icon';
import { AccountsApi } from '../data-access/accounts-api';
import { authErrorMessage } from '../account-error-message';
import { displayNameValidator, matchingPasswords } from './auth-validators';

type Field = 'email' | 'displayName' | 'password' | 'confirmation';

@Component({
  selector: 'minos-auth-page',
  imports: [ReactiveFormsModule, RouterLink, SectionHeading, ForestLandscape, UiIcon, FieldFeedback],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './auth-page.html',
  styleUrl: './auth-page.css',
})
export class AuthPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(AccountsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private readonly summary = viewChild<ElementRef<HTMLElement>>('summary');
  readonly session = inject(SessionService);
  readonly registering = this.route.snapshot.data['mode'] === 'register';
  readonly returnUrl = safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
  readonly returnTree = this.router.parseUrl(this.returnUrl);
  readonly expired = this.route.snapshot.queryParamMap.get('expired') === '1';
  readonly signedOut = this.route.snapshot.queryParamMap.get('signedOut') === '1';
  readonly pending = signal(false);
  readonly submitted = signal(false);
  readonly serverError = signal<string | null>(null);
  readonly passwordVisible = signal(false);
  readonly fields: readonly Field[] = this.registering
    ? ['displayName', 'email', 'password', 'confirmation'] : ['email', 'password'];
  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(256)] }),
    displayName: new FormControl('', { nonNullable: true, validators: this.registering ? [displayNameValidator] : [] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(12), Validators.maxLength(256)] }),
    confirmation: new FormControl('', { nonNullable: true, validators: this.registering ? [Validators.required] : [] }),
  }, { validators: this.registering ? [matchingPasswords] : [] });

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
    if (this.pending()) return;
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
        void this.router.navigateByUrl(this.returnUrl);
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

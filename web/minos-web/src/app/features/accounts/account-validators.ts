import { AbstractControl, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';

export const displayNameValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = String(control.value ?? '');
  if (!value.trim()) return { required: true };
  if (value.trim().length > 80) return { maxlength: true };
  return /[\u0000-\u001f\u007f-\u009f]/.test(value) ? { controlCharacters: true } : null;
};

export const matchingPasswords: ValidatorFn = (group: AbstractControl): ValidationErrors | null =>
  group.get('password')?.value === group.get('confirmation')?.value ? null : { passwordMismatch: true };

export const matchingNewPasswords: ValidatorFn = (group: AbstractControl): ValidationErrors | null =>
  group.get('newPassword')?.value === group.get('confirmation')?.value ? null : { passwordMismatch: true };

export const passwordValidators = [Validators.required, Validators.minLength(12), Validators.maxLength(256)];

export function passwordFieldError(control: AbstractControl, visible: boolean, label: string): string | null {
  if (!visible) return null;
  if (control.hasError('required')) return `Enter your ${label}.`;
  if (control.hasError('minlength')) return 'Use at least 12 characters.';
  if (control.hasError('maxlength')) return 'Use no more than 256 characters.';
  return null;
}

import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

export const displayNameValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = String(control.value ?? '');
  if (!value.trim()) return { required: true };
  if (value.trim().length > 80) return { maxlength: true };
  return /[\u0000-\u001f\u007f-\u009f]/.test(value) ? { controlCharacters: true } : null;
};

export const matchingPasswords: ValidatorFn = (group: AbstractControl): ValidationErrors | null =>
  group.get('password')?.value === group.get('confirmation')?.value ? null : { passwordMismatch: true };

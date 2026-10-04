import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { FieldFeedback } from './field-feedback';
import { UiIcon } from './ui-icon';

/** Presentational field; validation and credentials stay with the owning form. */
@Component({
  selector: 'minos-password-field', imports: [ReactiveFormsModule, FieldFeedback, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="form-field">
    <label [for]="fieldId()">{{ label() }}</label>
    <div class="password-control">
      <input [id]="fieldId()" [formControl]="control()" [type]="visible() ? 'text' : 'password'"
        [autocomplete]="autocomplete()" required minlength="12" maxlength="256" (input)="edited.emit()" (blur)="left.emit()"
        [attr.aria-invalid]="!!error()" [attr.aria-describedby]="describedBy()" />
      <button class="password-toggle" type="button" [disabled]="control().disabled"
        [attr.aria-controls]="fieldId()" [attr.aria-pressed]="visible()"
        [attr.aria-label]="(visible() ? 'Hide ' : 'Show ') + label().toLowerCase()"
        (click)="visible.set(!visible())"><minos-icon [name]="visible() ? 'eye-off' : 'eye'" /></button>
    </div>
    <minos-field-feedback [fieldId]="fieldId()" [hint]="hint()" [error]="error()" />
  </div>`,
  styles: ':host { display: block; min-width: 0; }',
})
export class PasswordField {
  readonly fieldId = input.required<string>();
  readonly label = input.required<string>();
  readonly control = input.required<FormControl<string>>();
  readonly autocomplete = input<'current-password' | 'new-password'>('current-password');
  readonly hint = input('');
  readonly error = input<string | null>(null);
  readonly visible = signal(false);
  readonly edited = output<void>();
  readonly left = output<void>();
  describedBy(): string | null {
    const ids = [this.hint() ? this.fieldId() + '-hint' : '', this.error() ? this.fieldId() + '-error' : ''];
    return ids.filter(Boolean).join(' ') || null;
  }
}

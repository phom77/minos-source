import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'minos-field-feedback',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (hint()) { <p [id]="fieldId() + '-hint'" class="field-hint">{{ hint() }}</p> }
    @if (error()) { <p [id]="fieldId() + '-error'" class="field-error">{{ error() }}</p> }`,
  styles: ':host { display: block; } p { margin: var(--space-2) 0 0; font-size: var(--text-label); } .field-hint { color: var(--color-text-muted); } .field-error { color: var(--color-danger); }',
})
export class FieldFeedback {
  readonly fieldId = input.required<string>();
  readonly hint = input('');
  readonly error = input<string | null>(null);
}

import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'minos-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-tone]': 'tone()' },
  template: '<ng-content />',
  styles: `
    :host { display: inline-flex; max-width: 100%; padding: var(--space-1) var(--space-2);
      border-radius: var(--radius-small); background: var(--color-surface-subtle);
      color: var(--color-text-muted); font-size: var(--text-small); font-weight: 600;
      line-height: 1.5; overflow-wrap: anywhere; }
    :host([data-tone="success"]) { background: var(--color-success-surface); color: var(--color-success); }
    :host([data-tone="warning"]) { background: var(--color-warning-surface); color: var(--color-warning); }
    :host([data-tone="danger"]) { background: var(--color-danger-surface); color: var(--color-danger); }
  `,
})
export class StatusBadge {
  readonly tone = input<'neutral' | 'success' | 'warning' | 'danger'>('neutral');
}

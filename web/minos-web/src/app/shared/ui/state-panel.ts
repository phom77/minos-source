import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { UiIcon } from './ui-icon';

@Component({
  selector: 'minos-state-panel',
  imports: [UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './state-panel.html',
  styleUrl: './state-panel.css',
})
export class StatePanel {
  readonly heading = input.required<string>();
  readonly description = input.required<string>();
  readonly kind = input<'empty' | 'error' | 'loading'>('empty');
}

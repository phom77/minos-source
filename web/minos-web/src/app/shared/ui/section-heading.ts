import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'minos-section-heading',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'section-heading', '[attr.data-tone]': 'tone()' },
  templateUrl: './section-heading.html',
  styleUrl: './section-heading.css',
})
export class SectionHeading {
  readonly eyebrow = input.required<string>();
  readonly heading = input.required<string>();
  readonly summary = input.required<string>();
  readonly tone = input<'light' | 'dark'>('light');
}

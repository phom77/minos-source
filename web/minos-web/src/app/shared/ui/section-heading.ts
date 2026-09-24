import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'minos-section-heading',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'section-heading' },
  template: `
    <p class="eyebrow">{{ eyebrow() }}</p>
    <h1>{{ heading() }}</h1>
    <p class="summary">{{ summary() }}</p>
  `,
  styles: `
    :host {
      display: block;
      max-width: 48rem;
    }

    .eyebrow {
      margin: 0 0 0.9rem;
      color: var(--color-forest-800);
      font-size: 0.72rem;
      font-weight: 750;
      letter-spacing: 0.16em;
      text-transform: uppercase;
    }

    h1 {
      max-width: 14ch;
      margin: 0;
      color: var(--color-forest-950);
      font-family: var(--font-display);
      font-size: clamp(2.7rem, 7vw, 5.8rem);
      font-weight: 500;
      letter-spacing: -0.045em;
      line-height: 0.98;
      text-wrap: balance;
    }

    .summary {
      max-width: 42rem;
      margin: 1.5rem 0 0;
      color: var(--color-muted);
      font-size: clamp(1rem, 2vw, 1.2rem);
      line-height: 1.65;
      text-wrap: pretty;
    }
  `,
})
export class SectionHeading {
  readonly eyebrow = input.required<string>();
  readonly heading = input.required<string>();
  readonly summary = input.required<string>();
}

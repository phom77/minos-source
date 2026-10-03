import { ChangeDetectionStrategy, Component, input } from '@angular/core';

const paths = {
  problems: 'M6 3h8l4 4v14H6z M14 3v5h4 M9 12h6 M9 16h6',
  account: 'M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M4 21v-2a8 8 0 0 1 16 0v2z',
  menu: 'M4 6h16 M4 12h16 M4 18h16',
  close: 'm6 6 12 12 M18 6 6 18',
  arrow: 'M4 12h16 m-6-6 6 6-6 6',
  chevron: 'm9 5 7 7-7 7',
  library: 'M4 4h16v16H4z M8 4v16 M12 8h4 M12 12h4',
  shield: 'M12 3 4 6v6c0 5 8 9 8 9s8-4 8-9V6z m-4 9 3 3 5-5',
  warning: 'm12 3 10 18H2z M12 9v5 M12 17h.01',
  sun: 'M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M12 2v2 M12 20v2 M2 12h2 M20 12h2 m-15-7 1.5 1.5 m11 11L19 19 M5 19l1.5-1.5 M17.5 6.5 19 5',
  moon: 'M20.5 13A9 9 0 0 1 11 3.5 9 9 0 1 0 20.5 13z',
  eye: 'M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0',
  'eye-off': 'm3 3 18 18 M10.6 5.1 12 5c6.5 0 10 7 10 7a20 20 0 0 1-3 4 M6.3 6.3C3.5 8.5 2 12 2 12s3.5 7 10 7a12 12 0 0 0 5.7-1.3 M9.9 9.9a3 3 0 0 0 4.2 4.2',
} as const;

@Component({
  selector: 'minos-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true' },
  styles: ':host { display: inline-flex; flex-shrink: 0; }',
  template: `<svg width="20" height="20" viewBox="0 0 24 24" fill="none"
    stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"
    aria-hidden="true" focusable="false"><path [attr.d]="paths[name()]" /></svg>`,
})
export class UiIcon {
  protected readonly paths = paths;
  readonly name = input.required<keyof typeof paths>();
}

import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { SectionHeading } from '../shared/ui/section-heading';
import { UiIcon } from '../shared/ui/ui-icon';

@Component({
  selector: 'minos-not-found-page',
  imports: [RouterLink, SectionHeading, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './not-found-page.html',
})
export class NotFoundPage {}

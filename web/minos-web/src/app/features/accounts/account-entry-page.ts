import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { SectionHeading } from '../../shared/ui/section-heading';

@Component({
  selector: 'minos-account-entry-page',
  imports: [RouterLink, SectionHeading],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './account-entry-page.html',
  styleUrl: './account-entry-page.css',
})
export class AccountEntryPage {}

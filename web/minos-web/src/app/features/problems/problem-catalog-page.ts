import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { SectionHeading } from '../../shared/ui/section-heading';

@Component({
  selector: 'minos-problem-catalog-page',
  imports: [RouterLink, SectionHeading],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './problem-catalog-page.html',
  styleUrl: './problem-catalog-page.css',
})
export class ProblemCatalogPage {}

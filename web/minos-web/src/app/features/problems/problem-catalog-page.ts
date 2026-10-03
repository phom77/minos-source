import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { SectionHeading } from '../../shared/ui/section-heading';
import { ForestLandscape } from '../../shared/ui/forest-landscape';
import { StatePanel } from '../../shared/ui/state-panel';
import { StatusBadge } from '../../shared/ui/status-badge';
import { UiIcon } from '../../shared/ui/ui-icon';

@Component({
  selector: 'minos-problem-catalog-page',
  imports: [RouterLink, SectionHeading, ForestLandscape, StatePanel, StatusBadge, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './problem-catalog-page.html',
  styleUrl: './problem-catalog-page.css',
})
export class ProblemCatalogPage {}

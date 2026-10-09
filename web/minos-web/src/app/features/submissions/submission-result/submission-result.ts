import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { StatusBadge } from '../../../shared/ui/status-badge';
import { outcomeLabel, outcomeTone, Submission, testLabel } from '../data-access/submission.models';

@Component({ selector: 'minos-submission-result', imports: [DecimalPipe, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './submission-result.html', styleUrl: '../submission-pages.css' })
export class SubmissionResultView {
  readonly submission = input.required<Submission>();
  readonly interrupted = input(false);
  readonly refresh = output<void>();
  readonly outcomeLabel = outcomeLabel;
  readonly outcomeTone = outcomeTone;
  readonly testLabel = testLabel;
}

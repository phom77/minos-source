import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { StatusBadge } from '../../../shared/ui/status-badge';
import { activeSubmission, outcomeLabel, outcomeTone, Submission } from '../data-access/submission.models';

@Component({ selector: 'minos-submission-result', imports: [StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './submission-result.html', styleUrl: '../submission-pages.css' })
export class SubmissionResultView {
  readonly submission = input.required<Submission>();
  readonly interrupted = input(false);
  readonly compact = input(false);
  readonly headingId = input('result-heading');
  readonly refresh = output<void>();
  readonly active = activeSubmission;
  readonly summary = computed(() => {
    const tests = this.submission().result?.tests ?? [];
    return { total: tests.length, passed: tests.filter(test => test.status === 'Passed').length,
      notExecuted: tests.filter(test => test.status === 'NotExecuted').length };
  });
  readonly outcomeLabel = outcomeLabel;
  readonly outcomeTone = outcomeTone;
}

import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { UiIcon } from '../../shared/ui/ui-icon';
import { ProblemTopicCount } from './data-access/problems-api';

@Component({
  selector: 'minos-problem-topic-browser', imports: [UiIcon], changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './problem-topic-browser.html', styleUrl: './problem-topic-browser.css',
})
export class ProblemTopicBrowser {
  readonly topics = input.required<readonly ProblemTopicCount[]>();
  readonly selectedTopic = input<string | null>(null);
  readonly excluded = input(false);
  readonly topicSelected = output<string | null>();
  readonly expandedTopics = signal(false);
}

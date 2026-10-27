import { afterNextRender, ChangeDetectionStrategy, Component, effect, ElementRef, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { FormFeedback } from '../accounts/form-feedback';
import { CommentInput } from './data-access/comments-api';
import { UiIcon } from '../../shared/ui/ui-icon';

export function commentBodyError(value: string): string | null {
  const body = value.trim();
  if (!body) return 'Write a comment.';
  if (body.length > 4000) return 'Keep your comment within 4000 characters.';
  if (/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/.test(body)) return 'Remove unsupported control characters.';
  return null;
}
@Component({ selector: 'minos-comment-editor', imports: [ReactiveFormsModule, UiIcon], changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './comment-editor.html', styleUrl: './comment-editor.css' })
export class CommentEditor {
  readonly editorId = input.required<string>(); readonly label = input('Your comment'); readonly action = input('Comment');
  readonly initial = input<CommentInput>({ body: '', isSpoiler: false }); readonly pending = input(false); readonly error = input<string | null>(null);
  readonly blocked = input(false);
  readonly cancellable = input(false); readonly autofocus = input(false);
  readonly save = output<CommentInput>(); readonly cancel = output<void>();
  readonly body = new FormControl('', { nonNullable: true }); readonly spoiler = new FormControl(false, { nonNullable: true });
  readonly submitted = signal(false); readonly feedback = new FormFeedback<'body'>();
  private readonly summary = viewChild<ElementRef<HTMLElement>>('summary');
  private readonly textarea = viewChild<ElementRef<HTMLTextAreaElement>>('textarea'); private readonly injector = inject(Injector);
  constructor() {
    effect(() => { const initial = this.initial(); this.body.reset(initial.body); this.spoiler.reset(initial.isSpoiler); this.feedback.reset(); this.submitted.set(false); });
    afterNextRender(() => { if (this.autofocus()) this.textarea()?.nativeElement.focus(); });
    effect(() => { if (this.error()) this.focusSummary(); });
  }
  fieldError(): string | null { return this.feedback.visible('body', this.submitted()) ? commentBodyError(this.body.value) : null; }
  blur(): void { this.feedback.blur('body', commentBodyError(this.body.value) !== null); }
  insert(kind: 'code' | 'link'): void {
    const textarea = this.textarea()?.nativeElement;
    if (!textarea || this.pending() || this.blocked()) return;
    const start = textarea.selectionStart, end = textarea.selectionEnd;
    const selected = this.body.value.slice(start, end);
    const prefix = kind === 'code' ? (start > 0 && this.body.value[start - 1] !== '\n' ? '\n\n' : '') + '```cpp\n' : '[';
    const content = selected || (kind === 'code' ? 'code' : 'link text');
    const suffix = kind === 'code' ? '\n```\n' : '](https://)';
    this.body.setValue(this.body.value.slice(0, start) + prefix + content + suffix + this.body.value.slice(end));
    this.body.markAsDirty(); this.feedback.edit('body');
    const selection = kind === 'code' ? start + prefix.length : start + prefix.length + content.length + 2;
    afterNextRender(() => {
      if (textarea.closest('[hidden]')) return;
      textarea.focus(); textarea.setSelectionRange(selection, selection + (kind === 'code' ? content.length : 'https://'.length));
    }, { injector: this.injector });
  }
  submit(): void {
    if (this.pending() || this.blocked()) return;
    this.submitted.set(true);
    if (commentBodyError(this.body.value)) { this.focusSummary(); return; }
    this.save.emit({ body: this.body.value.trim(), isSpoiler: this.spoiler.value });
  }
  private focusSummary(): void { afterNextRender(() => this.summary()?.nativeElement.focus(), { injector: this.injector }); }
}

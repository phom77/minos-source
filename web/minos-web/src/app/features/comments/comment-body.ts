import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Marked } from 'marked';

const escapeHtml = (value: string) => value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const markdown = new Marked({ async: false, gfm: true, breaks: true, renderer: {
  html: token => escapeHtml(token.text), image: token => escapeHtml(token.text),
  heading(token) { return `<p><strong>${this.parser.parseInline(token.tokens)}</strong></p>`; },
  link(token) {
    const text = this.parser.parseInline(token.tokens);
    return /^https?:\/\/[^\s\u0000-\u001f]+$/i.test(token.href)
      ? `<a href="${escapeHtml(token.href)}" rel="nofollow noopener noreferrer">${text}</a>` : text;
  },
} });
@Component({ selector: 'minos-comment-body', changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<div class="comment-body" [innerHTML]="html()"></div>',
  styles: `:host { display: block; min-width: 0; overflow-wrap: anywhere; }
    :host ::ng-deep .comment-body > :first-child { margin-top: 0; }
    :host ::ng-deep .comment-body > :last-child { margin-bottom: 0; }
    :host ::ng-deep pre { max-width: 100%; overflow: auto; padding: .75rem; background: var(--color-surface-subtle); }
    :host ::ng-deep table { display: block; max-width: 100%; overflow: auto; }
    :host ::ng-deep blockquote { margin-inline: 0; padding-left: .75rem; border-left: 2px solid var(--color-border); }`,
})
export class CommentBody {
  readonly body = input.required<string>();
  readonly html = computed(() => markdown.parse(this.body(), { async: false }));
}

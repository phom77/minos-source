import { afterRenderEffect, ChangeDetectionStrategy, Component, computed, ElementRef, inject, input, ViewEncapsulation } from '@angular/core';
import { Marked, TokenizerAndRendererExtension } from 'marked';
import katex from 'katex';

function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

const math: TokenizerAndRendererExtension = {
  name: 'problemMath', level: 'inline',
  start: source => source.search(/\$|\\[([]|\b(?:[a-zA-Z]_(?:\{[^{}\n]+\}|[a-zA-Z]|\d+)|\d+[_^]\d+)(?![\w])/),
  tokenizer(source) {
    const delimited = /^(\${1,3})(?!\$)([\s\S]+?)\1(?!\$)/.exec(source);
    if (delimited && (delimited[1].length > 1 || !/\n/.test(delimited[2]))) {
      return { type: 'problemMath', raw: delimited[0], text: delimited[2], display: delimited[1].length > 1 };
    }
    const bracketed = /^\\\(([\s\S]+?)\\\)/.exec(source) ?? /^\\\[([\s\S]+?)\\\]/.exec(source);
    if (bracketed) return { type: 'problemMath', raw: bracketed[0], text: bracketed[1], display: source.startsWith('\\[') };
    // CodeContests also contains plain subscripts/powers after losing its original inline delimiters.
    const plain = /^(?:[a-zA-Z]_(?:\{[^{}\n]+\}|[a-zA-Z]|\d+)|\d+[_^]\d+)(?![\w])/.exec(source);
    if (plain) return { type: 'problemMath', raw: plain[0], text: plain[0], display: false };
    return undefined;
  },
  renderer: token => `<span class="math-expression${token['display'] ? ' math-display' : ''}">${escapeHtml(token['text'])}</span>`,
};

const markdown = new Marked({
  async: false, gfm: true, breaks: true, extensions: [math],
  renderer: {
    // Statements are untrusted. Raw HTML stays visible text, and remote images never load.
    html: token => escapeHtml(token.text),
    image: token => escapeHtml(token.text),
    heading(token) { return `<h3>${this.parser.parseInline(token.tokens)}</h3>`; },
    link(token) {
      const label = this.parser.parseInline(token.tokens);
      return /^(https?:\/\/|mailto:|\/(?!\/)|#)/i.test(token.href)
        ? `<a href="${escapeHtml(token.href)}" rel="noopener noreferrer">${label}</a>` : label;
    },
    paragraph(token) {
      // Imported plain statements use standalone section names instead of Markdown headings.
      if (/^(Input|Output|Constraints|Note|Notes|Explanation|Input format|Output format)$/i.test(token.text.trim())) {
        return `<h3>${escapeHtml(token.text.trim())}</h3>`;
      }
      return `<p>${this.parser.parseInline(token.tokens)}</p>`;
    },
  },
});

@Component({
  selector: 'minos-problem-statement',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: '<div class="problem-statement" [innerHTML]="html()"></div>',
  styleUrl: './problem-statement.css',
})
export class ProblemStatement {
  readonly statement = input.required<string>();
  readonly html = computed(() => {
    const statement = this.statement();
    // Omit the known importer footer in every reader; stored snapshots stay intact.
    const footer = /(?:\r?\n){2,}(Source: https?:\/\/[^\r\n]+\r?\nDataset: DeepMind CodeContests, CC BY 4\.0 \(https:\/\/creativecommons\.org\/licenses\/by\/4\.0\/\)\. Original-source terms also apply\.)\s*$/.exec(statement);
    return markdown.parse(footer ? statement.slice(0, footer.index) : statement, { async: false });
  });
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    afterRenderEffect(() => {
      this.html();
      // Angular sanitizes the Markdown binding first. Only KaTeX-generated, untrusted-mode
      // math is then inserted; no sanitizer bypass is used for author-supplied HTML.
      for (const element of this.host.nativeElement.querySelectorAll<HTMLElement>('.math-expression')) {
        const displayMode = element.classList.contains('math-display');
        let source = element.textContent?.trim() ?? '';
        if (displayMode && /\\{2,}/.test(source) && !source.includes('\\begin{')) {
          source = '\\begin{gathered}' + source.replace(/\\{2,}/g, '\\\\') + '\\end{gathered}';
        }
        katex.render(source, element, { displayMode, throwOnError: false, trust: false,
          strict: 'ignore', maxExpand: 500, maxSize: 10 });
      }
    });
  }
}

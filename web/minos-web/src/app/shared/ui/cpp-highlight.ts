import { Pipe, PipeTransform } from '@angular/core';
import hljs from 'highlight.js/lib/core';
import cpp from 'highlight.js/lib/languages/cpp';

const highlighter = hljs.newInstance();
highlighter.registerLanguage('cpp', cpp);

@Pipe({ name: 'cppHighlight' })
export class CppHighlight implements PipeTransform {
  transform(source: string): string {
    // Return ordinary HTML for Angular to sanitize; source is never trusted markup.
    return highlighter.highlight(source, { language: 'cpp', ignoreIllegals: true }).value;
  }
}

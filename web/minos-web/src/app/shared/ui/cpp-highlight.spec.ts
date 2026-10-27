import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CppHighlight } from './cpp-highlight';

@Component({ imports: [CppHighlight], template: '<pre><code [innerHTML]="source() | cppHighlight"></code></pre>' })
class HighlightHost { source = signal(''); }

describe('C++ source highlighting', () => {
  it('preserves exact source whitespace and colors C++ constructs through the sanitized binding', () => {
    const fixture = TestBed.createComponent(HighlightHost);
    const source = '#include <bits/stdc++.h>\n// tiếng Việt\nclass Solution {\npublic:\n\tint answer() { return 42; }\n};\n\n';
    fixture.componentInstance.source.set(source); fixture.detectChanges();
    const code = fixture.nativeElement.querySelector('code') as HTMLElement;
    expect(code.textContent).toBe(source);
    expect(code.querySelector('.hljs-meta')).not.toBeNull();
    expect(code.querySelector('.hljs-type')?.textContent).toBe('int');
    expect(code.querySelector('.hljs-title')?.textContent).toBe('Solution');
    expect(code.querySelector('.hljs-number')?.textContent).toBe('42');
    expect(code.querySelector('.hljs-comment')?.textContent).toBe('// tiếng Việt');
  });

  it('keeps HTML-looking strings, comments and unfinished code literal without creating active elements', () => {
    const fixture = TestBed.createComponent(HighlightHost);
    const source = '// <img src=x onerror=alert(1)>\nconst char* s = "<script>alert(1)</script>";\nif (a < b &&';
    fixture.componentInstance.source.set(source); fixture.detectChanges();
    const code = fixture.nativeElement.querySelector('code') as HTMLElement;
    expect(code.textContent).toBe(source);
    expect(code.querySelector('script, img, [onerror]')).toBeNull();
    fixture.componentInstance.source.set(''); fixture.detectChanges();
    expect(code.textContent).toBe(''); expect(code.querySelector('span')).toBeNull();
  });
});

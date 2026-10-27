import { TestBed } from '@angular/core/testing';
import { ProblemStatement } from './problem-statement';

describe('problem statement rendering', () => {
  async function render(statement: string) {
    const fixture = TestBed.createComponent(ProblemStatement);
    fixture.componentRef.setInput('statement', statement);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    return fixture;
  }

  it('formats imported headings, Codeforces math, plain subscripts and links without changing stored text', async () => {
    const statement = 'Compute $$$ (a_1 + a_2) ⊕ (a_{n-1} + a_n) \\\\\\ 10^7 $$$.\n\nInput\n\nRead a_1 and 10^7.\n\nOutput\n\nSee <https://example.com/problem>.';
    const fixture = await render(statement), element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h3')?.textContent).toBe('Input');
    expect(element.querySelectorAll('.katex')).toHaveLength(3);
    expect(element.querySelector('.katex-error')).toBeNull();
    expect(element.querySelector('.katex-display')).not.toBeNull();
    expect(element.querySelector('a')?.getAttribute('href')).toBe('https://example.com/problem');
    expect(fixture.componentInstance.statement()).toBe(statement);
  });

  it('preserves code and sample-like whitespace while keeping math out of code blocks', async () => {
    const fixture = await render('**Print a sum.**\n\n```cpp\n  a_1 = "$x$";\n```\n\nUse `a_1` literally.');
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('strong')?.textContent).toBe('Print a sum.');
    expect(element.querySelector('pre code')?.textContent).toBe('  a_1 = "$x$";\n');
    expect(element.querySelector('pre .katex')).toBeNull();
    expect(element.querySelector('p code')?.textContent).toBe('a_1');
  });

  it('blocks raw HTML, unsafe links, remote images and trusted TeX commands', async () => {
    const fixture = await render('<script>alert(1)</script>\n\n<img src="https://tracker.test/x" onerror="alert(1)">\n\n[attack](javascript:alert%281%29) ![image](https://tracker.test/x)\n\n$\\href{javascript:alert(1)}{click}$');
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('script, img, iframe, [onerror], a')).toBeNull();
    expect(element.textContent).toContain('<script>alert(1)</script>');
    expect(element.querySelector('.katex')).not.toBeNull();
  });

  it('updates rendered math on navigation and leaves malformed expressions readable', async () => {
    const fixture = await render('First $x^2$.');
    fixture.componentRef.setInput('statement', 'Next \\(y_1\\). Invalid $\\unknownCommand{x}$.');
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('annotation')?.textContent).toBe('y_1');
    expect(element.textContent).not.toContain('First');
    expect(element.textContent).toContain('unknownCommand');
  });

  it.each(['\n', '\r\n'])('omits the import source and license entirely from the reader without changing stored text (%j)', async newline => {
    const statement = ['Solve this problem.', '', 'Source: https://codeforces.com/problemset/problem/248/A', 'Dataset: DeepMind CodeContests, CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/). Original-source terms also apply.'].join(newline);
    const fixture = await render(statement), element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.problem-statement')?.textContent).toBe('Solve this problem.');
    expect(element.querySelector('details, .problem-attribution, a')).toBeNull();
    expect(element.textContent).not.toMatch(/Source|Dataset|CodeContests|CC BY|license|Original-source/i);
    expect(fixture.componentInstance.statement()).toBe(statement);
    fixture.componentRef.setInput('statement', 'Source: a graph with n vertices.\n\nDataset: sample input.');
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(element.querySelector('details')).toBeNull(); expect(element.textContent).toContain('Source: a graph');
  });
});

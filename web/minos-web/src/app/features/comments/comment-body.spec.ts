import { TestBed } from '@angular/core/testing';
import { CommentBody } from './comment-body';

describe('comment Markdown', () => {
  it('formats code without executing HTML, loading remote images or accepting dangerous links', () => {
    const fixture = TestBed.createComponent(CommentBody);
    fixture.componentRef.setInput('body', '**Question**\n\n```cpp\n  return 1;\n```\n\n<script>alert(1)</script>\n\n<img src="https://tracker.test/x" onerror="alert(1)">\n\n[bad](javascript:alert%281%29) ![pixel](https://tracker.test/x) [safe](https://example.test/help)');
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('strong')?.textContent).toBe('Question');
    expect(element.querySelector('pre code')?.textContent).toBe('  return 1;\n');
    expect(element.querySelector('script,img,iframe,[onerror]')).toBeNull();
    expect(element.querySelectorAll('a')).toHaveLength(1);
    expect(element.querySelector('a')?.getAttribute('href')).toBe('https://example.test/help');
    expect(element.textContent).toContain('<script>alert(1)</script>');
  });
});

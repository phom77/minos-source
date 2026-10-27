import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CommentEditor } from './comment-editor';

describe('comment editor feedback', () => {
  let fixture: ComponentFixture<CommentEditor>;
  const textarea = () => fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
  function input(value: string) { textarea().value = value; textarea().dispatchEvent(new Event('input')); fixture.detectChanges(); }
  beforeEach(async () => {
    fixture = TestBed.createComponent(CommentEditor); fixture.componentRef.setInput('editorId', 'test-comment');
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
  });
  it('keeps untouched blur quiet, reveals edited invalid text on blur, and clears errors during correction', () => {
    textarea().dispatchEvent(new Event('blur')); fixture.detectChanges();
    expect(textarea().getAttribute('aria-invalid')).toBeNull();
    input(' '); expect(fixture.nativeElement.querySelector('[role=alert]')).toBeNull();
    textarea().dispatchEvent(new Event('blur')); fixture.detectChanges();
    expect(textarea().getAttribute('aria-invalid')).toBe('true');
    expect(textarea().getAttribute('aria-describedby')).toContain('test-comment-error');
    input('A useful question'); expect(textarea().getAttribute('aria-invalid')).toBeNull();
    expect(fixture.nativeElement.querySelector('[role=alert]')).toBeNull();
  });
  it('allows empty submit to request validation and focuses an accessible error summary', async () => {
    const save = vi.fn(); fixture.componentInstance.save.subscribe(save);
    expect(fixture.nativeElement.querySelector('button[type=submit]').disabled).toBe(false);
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('[role=alert]'));
    expect(save).not.toHaveBeenCalled();
    input('🧩'.repeat(2001)); fixture.componentInstance.submit(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('4000'); expect(save).not.toHaveBeenCalled();
    input('Question'); fixture.componentInstance.submit(); expect(save).toHaveBeenCalledWith({ body: 'Question', isSpoiler: false });
  });
  it('resets interaction and content after completion or a different edit target, and blocks duplicate saves while pending', () => {
    input(' '); textarea().dispatchEvent(new Event('blur')); fixture.detectChanges();
    fixture.componentRef.setInput('initial', { body: 'Existing comment', isSpoiler: true }); fixture.detectChanges();
    expect(textarea().value).toBe('Existing comment'); expect(textarea().getAttribute('aria-invalid')).toBeNull();
    const save = vi.fn(); fixture.componentInstance.save.subscribe(save);
    fixture.componentRef.setInput('pending', true); fixture.detectChanges(); fixture.componentInstance.submit();
    expect(save).not.toHaveBeenCalled(); expect(textarea().readOnly).toBe(true);
    fixture.componentRef.setInput('initial', { body: '', isSpoiler: false }); fixture.detectChanges();
    expect(textarea().value).toBe(''); expect(fixture.nativeElement.querySelector('[role=alert]')).toBeNull();
    fixture.componentRef.setInput('pending', false); fixture.componentRef.setInput('blocked', true); fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('button[type=submit]') as HTMLButtonElement;
    expect(button.disabled).toBe(true); expect(button.textContent?.trim()).toBe('Comment');
  });

  it('wraps selected code and focuses its editable text without submitting', async () => {
    const save = vi.fn(); fixture.componentInstance.save.subscribe(save);
    input('Explain a + b here'); textarea().setSelectionRange(8, 13);
    fixture.nativeElement.querySelector('[aria-label="Insert code block"]').click();
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(textarea().value).toBe('Explain \n\n```cpp\na + b\n```\n here');
    expect(document.activeElement).toBe(textarea());
    expect(textarea().value.slice(textarea().selectionStart, textarea().selectionEnd)).toBe('a + b');
    expect(save).not.toHaveBeenCalled();
  });

  it('inserts a link with a selected destination and blocks formatting during a save', async () => {
    input('Read reference'); textarea().setSelectionRange(5, 14);
    fixture.nativeElement.querySelector('[aria-label="Insert link"]').click();
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(textarea().value).toBe('Read [reference](https://)');
    expect(textarea().value.slice(textarea().selectionStart, textarea().selectionEnd)).toBe('https://');
    fixture.componentRef.setInput('pending', true); fixture.detectChanges(); fixture.componentInstance.insert('code');
    expect(textarea().value).toBe('Read [reference](https://)');
    expect(fixture.nativeElement.querySelector('[aria-label="Insert link"]').disabled).toBe(true);
  });
});

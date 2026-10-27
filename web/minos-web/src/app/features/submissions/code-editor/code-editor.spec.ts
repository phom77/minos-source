import { TestBed } from '@angular/core/testing';
import { FormControl } from '@angular/forms';
import { CodeEditor } from './code-editor';

describe('highlighted native code editor', () => {
  it('updates colors from native input and programmatic drafts without replacing the textarea or selection', async () => {
    const fixture = TestBed.createComponent(CodeEditor), control = new FormControl('int main() {}', { nonNullable: true });
    fixture.componentRef.setInput('control', control); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const editor = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    const code = fixture.nativeElement.querySelector('.syntax-layer code') as HTMLElement;
    expect(code.textContent).toBe(control.value); expect(code.querySelector('.hljs-type')?.textContent).toBe('int');
    const source = 'int main() {\n\treturn 42;\n}\n';
    editor.value = source; editor.dispatchEvent(new Event('input')); editor.setSelectionRange(14, 14);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(control.value).toBe(source); expect(code.textContent).toBe(source);
    expect(code.querySelector('.hljs-number')?.textContent).toBe('42');
    expect(fixture.nativeElement.querySelector('textarea')).toBe(editor); expect(editor.selectionStart).toBe(14);
    control.setValue('// restored draft\n'); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(code.textContent).toBe(control.value); expect(editor.value).toBe(control.value);
    expect(fixture.nativeElement.querySelector('.highlight-viewport').getAttribute('aria-hidden')).toBe('true');
  });

  it('scrolls highlighting on both axes while keeping the gutter bounded', async () => {
    const fixture = TestBed.createComponent(CodeEditor);
    fixture.componentRef.setInput('control', new FormControl('int n;\n'.repeat(1000), { nonNullable: true }));
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const editor = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    Object.defineProperties(editor, { clientWidth: { value: 400 }, clientHeight: { value: 240 } });
    editor.scrollTop = 240; editor.scrollLeft = 80; editor.dispatchEvent(new Event('scroll')); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.syntax-layer').style.transform).toBe('translate(-80px, -240px)');
    expect(fixture.nativeElement.querySelector('.highlight-viewport').style.width).toBe('400px');
    expect(fixture.nativeElement.querySelector('.highlight-viewport').style.height).toBe('240px');
    expect(fixture.nativeElement.querySelector('.line-gutter pre').textContent.split('\n')).toHaveLength(12);
  });

  it('uses native text during IME composition and highlights the committed value afterwards', async () => {
    const fixture = TestBed.createComponent(CodeEditor), control = new FormControl('', { nonNullable: true });
    fixture.componentRef.setInput('control', control); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const editor = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    editor.dispatchEvent(new CompositionEvent('compositionstart')); editor.value = '// tiếng Việt';
    editor.dispatchEvent(new InputEvent('input', { isComposing: true })); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.code-input--composing')).not.toBeNull();
    expect(editor.value).toBe('// tiếng Việt');
    editor.dispatchEvent(new CompositionEvent('compositionend')); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.code-input--composing')).toBeNull();
    expect(control.value).toBe('// tiếng Việt');
    expect(fixture.nativeElement.querySelector('.syntax-layer code').textContent).toBe('// tiếng Việt');
  });

  it('clears highlighted source when the control is cleared or replaced for another identity', async () => {
    const fixture = TestBed.createComponent(CodeEditor), control = new FormControl('// private source', { nonNullable: true });
    fixture.componentRef.setInput('control', control); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    control.setValue(''); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.syntax-layer code').textContent).toBe('');
    control.setValue('// private source'); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    fixture.componentRef.setInput('control', new FormControl('', { nonNullable: true }));
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('private source');
    expect(fixture.nativeElement.querySelector('textarea').value).toBe('');
    control.setValue('old identity'); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('old identity');
  });
});

import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { startWith, switchMap } from 'rxjs';
import { CppHighlight } from '../../../shared/ui/cpp-highlight';

@Component({ selector: 'minos-code-editor', imports: [ReactiveFormsModule, CppHighlight], changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './code-editor.html', styleUrl: './code-editor.css' })
export class CodeEditor {
  readonly control = input.required<FormControl<string>>();
  readonly error = input<string | null>(null);
  readonly edited = output<void>();
  readonly blurred = output<void>();
  private readonly destroyRef = inject(DestroyRef);
  private readonly editor = viewChild.required<ElementRef<HTMLTextAreaElement>>('editor');
  readonly text = toSignal(toObservable(this.control).pipe(switchMap(control => control.valueChanges.pipe(startWith(control.value)))), { initialValue: '' });
  private readonly scrollTop = signal(0);
  private readonly scrollLeft = signal(0);
  readonly viewport = signal<{ width: number; height: number } | null>(null);
  readonly composing = signal(false);
  readonly highlightOffset = computed(() => `translate(${-this.scrollLeft()}px, ${-this.scrollTop()}px)`);
  private readonly visibleRows = signal(40);
  readonly cursor = signal({ line: 1, column: 1 });
  private readonly firstLine = computed(() => Math.floor(this.scrollTop() / 24));
  readonly gutterOffset = computed(() => this.firstLine() * 24 - this.scrollTop());
  readonly lineNumbers = computed(() => {
    const total = this.text().split('\n').length, start = Math.min(this.firstLine(), total - 1);
    return Array.from({ length: Math.min(this.visibleRows(), total - start) }, (_, index) => start + index + 1).join('\n');
  });
  constructor() {
    afterNextRender(() => {
      if (typeof ResizeObserver === 'undefined') { this.scroll(); return; }
      const observer = new ResizeObserver(() => this.scroll());
      observer.observe(this.editor().nativeElement); this.scroll();
      this.destroyRef.onDestroy(() => observer.disconnect());
    });
  }
  edit(): void { this.position(); this.edited.emit(); }
  position(): void {
    const editor = this.editor().nativeElement, before = editor.value.slice(0, editor.selectionStart), lastBreak = before.lastIndexOf('\n');
    this.cursor.set({ line: before.split('\n').length, column: before.length - lastBreak });
  }
  scroll(): void {
    const editor = this.editor().nativeElement;
    this.scrollTop.set(editor.scrollTop); this.visibleRows.set(Math.ceil(editor.clientHeight / 24) + 2);
    this.scrollLeft.set(editor.scrollLeft);
    this.viewport.set({ width: editor.clientWidth, height: editor.clientHeight });
  }
}

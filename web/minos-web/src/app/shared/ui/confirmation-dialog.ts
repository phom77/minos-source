import { DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, effect, ElementRef, inject, Injector, input, output, viewChild } from '@angular/core';

@Component({
  selector: 'minos-confirmation-dialog', changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<dialog #dialog tabindex="-1" aria-labelledby="confirmation-title" aria-describedby="confirmation-description"
    [attr.aria-busy]="busy()" (cancel)="cancel($event)" (keydown)="trapFocus($event)">
    <h3 id="confirmation-title">{{ heading() }}</h3>
    <p id="confirmation-description">{{ description() }}</p>
    @if (busy()) { <p role="status">{{ busyLabel() }} The result must be confirmed before closing.</p> }
    <div class="dialog-actions">
      <button #cancelButton class="button button--secondary" type="button" [disabled]="busy()" (click)="cancelled.emit()">Cancel</button>
      <button class="button button--danger" type="button" [disabled]="busy()" (click)="confirmed.emit()">{{ busy() ? busyLabel() : actionLabel() }}</button>
    </div>
  </dialog>`,
  styleUrl: './confirmation-dialog.css',
})
export class ConfirmationDialog {
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly cancelButton = viewChild.required<ElementRef<HTMLButtonElement>>('cancelButton');
  readonly heading = input.required<string>();
  readonly description = input.required<string>();
  readonly actionLabel = input.required<string>();
  readonly busyLabel = input('Working…');
  readonly busy = input(false);
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();
  private opener: HTMLElement | null = null;

  constructor() {
    effect(() => {
      if (this.busy()) afterNextRender(() => this.dialog().nativeElement.focus(), { injector: this.injector });
    });
    afterNextRender(() => {
      this.opener = this.document.activeElement instanceof HTMLElement ? this.document.activeElement : null;
      this.dialog().nativeElement.showModal();
      this.document.documentElement.classList.add('confirmation-open');
      this.cancelButton().nativeElement.focus();
    });
    inject(DestroyRef).onDestroy(() => {
      const dialog = this.dialog().nativeElement;
      if (dialog.open) dialog.close();
      this.document.documentElement.classList.remove('confirmation-open');
      if (this.opener?.isConnected) this.opener.focus({ preventScroll: true });
    });
  }

  cancel(event: Event): void { event.preventDefault(); if (!this.busy()) this.cancelled.emit(); }

  trapFocus(event: KeyboardEvent): void {
    if (event.key !== 'Tab') return;
    const controls = this.dialog().nativeElement.querySelectorAll<HTMLButtonElement>('button:not([disabled])');
    const first = controls.item(0), last = controls.item(controls.length - 1);
    if (!first) { event.preventDefault(); return; }
    if (event.shiftKey && this.document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && this.document.activeElement === last) { event.preventDefault(); first.focus(); }
  }
}

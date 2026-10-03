import { DOCUMENT } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, effect, ElementRef, inject, Injector, viewChild } from '@angular/core';

import { AuthPrompt } from '../../../core/auth/auth-prompt.service';
import { ThemeService } from '../../../core/theme/theme.service';
import { UiIcon } from '../../../shared/ui/ui-icon';
import { AuthForm } from '../auth-form/auth-form';

@Component({
  selector: 'minos-auth-dialog', imports: [AuthForm, UiIcon], changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './auth-dialog.html', styleUrl: './auth-dialog.css',
})
export class AuthDialog {
  readonly prompt = inject(AuthPrompt);
  readonly theme = inject(ThemeService);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly heading = viewChild.required<ElementRef<HTMLElement>>('heading');
  private opener: HTMLElement | null = null;

  constructor() {
    effect(() => {
      const mode = this.prompt.mode();
      afterNextRender(() => {
        const dialog = this.dialog().nativeElement;
        if (mode) {
          if (!dialog.open) {
            this.opener = this.document.activeElement instanceof HTMLElement ? this.document.activeElement : null;
            dialog.showModal();
          }
          this.heading().nativeElement.focus();
        } else if (dialog.open) {
          dialog.close(); this.restoreFocus();
        }
      }, { injector: this.injector });
    });
    inject(DestroyRef).onDestroy(() => { const dialog = this.dialog().nativeElement; if (dialog.open) dialog.close(); });
  }

  cancel(event: Event): void { event.preventDefault(); this.prompt.close(); }

  dismissBackdrop(event: MouseEvent): void {
    const dialog = this.dialog().nativeElement;
    if (event.target !== dialog) return;
    const rect = dialog.getBoundingClientRect();
    if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) this.prompt.close();
  }

  trapFocus(event: KeyboardEvent): void {
    if (event.key !== 'Tab') return;
    const controls = this.dialog().nativeElement.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], input:not([disabled])');
    const first = controls.item(0), last = controls.item(controls.length - 1);
    if (event.shiftKey && (this.document.activeElement === first || this.document.activeElement === this.heading().nativeElement)) {
      event.preventDefault(); last?.focus();
    } else if (!event.shiftKey && this.document.activeElement === last) {
      event.preventDefault(); first?.focus();
    }
  }

  private restoreFocus(): void {
    const target = this.opener?.isConnected ? this.opener : this.document.getElementById('main-content');
    target?.focus({ preventScroll: true }); this.opener = null;
  }
}

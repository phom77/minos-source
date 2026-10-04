import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender, ChangeDetectionStrategy, Component, DestroyRef,
  computed, ElementRef, inject, Injector, signal, viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';

import { UiIcon } from '../shared/ui/ui-icon';
import { SessionService } from '../core/auth/session.service';
import { ThemeService } from '../core/theme/theme.service';
import { AuthPrompt } from '../core/auth/auth-prompt.service';
import { AuthDialog } from '../features/accounts/auth-dialog/auth-dialog';

@Component({
  selector: 'minos-app-shell',
  imports: [NgTemplateOutlet, RouterLink, RouterLinkActive, RouterOutlet, UiIcon, AuthDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.css',
})
export class AppShell {
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly drawer = viewChild<ElementRef<HTMLDialogElement>>('drawer');
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');
  private readonly accountTrigger = viewChild<ElementRef<HTMLButtonElement>>('accountTrigger');
  private readonly settingsLink = viewChild<ElementRef<HTMLAnchorElement>>('settingsLink');

  readonly navigationOpen = signal(false);
  readonly accountMenuOpen = signal(false);
  readonly skipHref = signal('/problems#main-content');
  readonly session = inject(SessionService);
  readonly theme = inject(ThemeService);
  readonly auth = inject(AuthPrompt);
  private lastLocation = '';
  readonly accountLabel = computed(() => {
    const state = this.session.state();
    return state.status === 'authenticated' ? state.account.displayName
      : state.status === 'checking' ? 'Checking session' : state.status === 'unavailable' ? 'Account unavailable' : 'Sign in';
  });

  constructor() {
    this.session.ensure().pipe(takeUntilDestroyed()).subscribe();
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      takeUntilDestroyed(),
    ).subscribe((event) => {
      this.skipHref.set(event.urlAfterRedirects.split('#')[0] + '#main-content');
      this.closeNavigation();
      this.accountMenuOpen.set(false);
      const location = event.urlAfterRedirects.split('?')[0] + '#' + (this.router.routerState.snapshot.root.fragment ?? '');
      const moved = location !== this.lastLocation;
      this.lastLocation = location;
      if (this.auth.mode() || !moved) return;
      const fragment = this.router.routerState.snapshot.root.fragment;
      afterNextRender(() => {
        const target = fragment ? this.document.getElementById(fragment) : null;
        (target ?? this.main().nativeElement).focus();
      }, { injector: this.injector });
    });

    afterNextRender(() => {
      const media = this.document.defaultView?.matchMedia?.('(min-width: 64rem)');
      if (!media) return;
      const onResize = () => {
        if (media.matches && this.navigationOpen()) {
          this.closeNavigation();
          this.main().nativeElement.focus();
        }
      };
      media.addEventListener('change', onResize);
      this.destroyRef.onDestroy(() => media.removeEventListener('change', onResize));
    });
  }

  openSignIn(): void {
    this.closeNavigation();
    this.auth.open();
  }

  toggleAccountMenu(): void {
    this.accountMenuOpen.update((open) => !open);
    if (this.accountMenuOpen()) afterNextRender(() => this.settingsLink()?.nativeElement.focus(), { injector: this.injector });
  }
  closeAccountMenu(event: KeyboardEvent): void {
    if (event.key !== 'Escape') return;
    event.preventDefault(); this.accountMenuOpen.set(false); this.accountTrigger()?.nativeElement.focus();
  }
  leaveAccountMenu(event: FocusEvent): void {
    if (!(event.currentTarget as HTMLElement).contains(event.relatedTarget as Node | null)) this.accountMenuOpen.set(false);
  }

  openNavigation(): void {
    const dialog = this.drawer()?.nativeElement;
    if (!dialog || dialog.open) return;
    dialog.showModal();
    this.navigationOpen.set(true);
  }

  closeNavigation(): void {
    const dialog = this.drawer()?.nativeElement;
    if (dialog?.open) dialog.close();
    this.navigationOpen.set(false);
  }

  onDrawerClosed(): void {
    this.navigationOpen.set(false);
  }

  onDrawerKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Tab') return;
    const controls = this.drawer()?.nativeElement.querySelectorAll<HTMLElement>('button:not([disabled]), a[href]');
    const first = controls?.item(0);
    const last = controls?.item(controls.length - 1);
    if (event.shiftKey && this.document.activeElement === first) {
      event.preventDefault();
      last?.focus();
    } else if (!event.shiftKey && this.document.activeElement === last) {
      event.preventDefault();
      first?.focus();
    }
  }

  dismissBackdrop(event: MouseEvent): void {
    const dialog = this.drawer()?.nativeElement;
    if (!dialog || event.target !== dialog) return;
    const bounds = dialog.getBoundingClientRect();
    if (event.clientX < bounds.left || event.clientX > bounds.right ||
        event.clientY < bounds.top || event.clientY > bounds.bottom) this.closeNavigation();
  }
}

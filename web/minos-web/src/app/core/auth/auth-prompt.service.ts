import { DOCUMENT, ViewportScroller } from '@angular/common';
import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, Scroll, UrlTree } from '@angular/router';

import { safeReturnUrl } from './safe-return-url';

export type AuthMode = 'login' | 'register';
export type AuthNotice = 'expired' | 'signed-out' | '';

const modalKeys = ['auth', 'authReturn', 'authNotice'];

/** Authentication is an overlay on a public route, not a separate destination. */
@Injectable({ providedIn: 'root' })
export class AuthPrompt {
  private readonly router = inject(Router);
  private readonly scroller = inject(ViewportScroller);
  private readonly document = inject(DOCUMENT);
  private readonly value = signal<AuthMode | null>(null);
  readonly mode = this.value.asReadonly();
  readonly notice = signal<AuthNotice>('');
  private scrollPosition: [number, number] | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.document.documentElement.classList.remove('auth-open'));
    this.readLocation();
    this.router.events.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (event instanceof NavigationEnd) this.readLocation();
      if (event instanceof Scroll && this.scrollPosition) {
        this.scroller.scrollToPosition(this.scrollPosition);
        this.scrollPosition = null;
      }
    });
  }

  open(mode: AuthMode = 'login'): void {
    this.scrollPosition = this.scroller.getScrollPosition();
    const tree = this.router.parseUrl(this.router.url);
    tree.queryParams = { ...tree.queryParams, auth: mode };
    void this.router.navigateByUrl(tree, { replaceUrl: this.mode() !== null });
  }

  entryTree(mode: AuthMode, returnUrl: string | null, notice: AuthNotice = ''): UrlTree {
    const path = this.router.url.split(/[?#]/)[0];
    const publicPage = path === '/problems' || path.startsWith('/problems/');
    if (publicPage) this.scrollPosition = this.scroller.getScrollPosition();
    const background = publicPage ? this.clean(this.router.url) : '/problems';
    const tree = this.router.parseUrl(background);
    tree.queryParams = { ...tree.queryParams, auth: mode, authReturn: this.clean(safeReturnUrl(returnUrl)), ...(notice ? { authNotice: notice } : {}) };
    return tree;
  }

  close(): void {
    this.scrollPosition = this.scroller.getScrollPosition();
    void this.router.navigateByUrl(this.clean(this.router.url), { replaceUrl: true });
  }

  complete(): void {
    const destination = this.router.parseUrl(this.router.url).queryParams['authReturn'];
    const target = typeof destination === 'string' ? this.clean(safeReturnUrl(destination)) : this.clean(this.router.url);
    if (target.split('?')[0].split('#')[0] === this.router.url.split('?')[0].split('#')[0]) {
      this.scrollPosition = this.scroller.getScrollPosition();
    }
    void this.router.navigateByUrl(target, { replaceUrl: true });
  }

  private clean(url: string): string {
    const tree = this.router.parseUrl(url);
    const query = { ...tree.queryParams };
    for (const key of modalKeys) delete query[key];
    tree.queryParams = query;
    return this.router.serializeUrl(tree);
  }

  private readLocation(): void {
    const query = this.router.parseUrl(this.router.url).queryParams;
    this.value.set(query['auth'] === 'login' || query['auth'] === 'register' ? query['auth'] : null);
    this.notice.set(query['authNotice'] === 'expired' || query['authNotice'] === 'signed-out' ? query['authNotice'] : '');
    // Native dialogs make the background inert; also prevent wheel/touch page scrolling.
    this.document.documentElement.classList.toggle('auth-open', this.value() !== null);
  }
}

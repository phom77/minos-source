import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'minos-app-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip-link" href="#main-content">Skip to content</a>

    <header class="site-header">
      <a class="brand" routerLink="/" aria-label="Minos home">
        <span class="brand-mark" aria-hidden="true">M</span>
        <span>Minos</span>
      </a>

      <nav aria-label="Primary navigation">
        <a routerLink="/problems" routerLinkActive="active">Problems</a>
        <a routerLink="/account" routerLinkActive="active">Account</a>
      </nav>

      <p class="edition"><span aria-hidden="true"></span> Foundation edition</p>
    </header>

    <main id="main-content" tabindex="-1">
      <router-outlet />
    </main>

    <footer>
      <p>Minos</p>
      <p>Practice with intent. Judge with evidence.</p>
    </footer>
  `,
  styles: `
    :host {
      display: flex;
      min-height: 100vh;
      flex-direction: column;
    }

    .skip-link {
      position: fixed;
      z-index: 10;
      top: 0.75rem;
      left: 0.75rem;
      padding: 0.6rem 0.8rem;
      background: var(--color-paper);
      color: var(--color-forest-950);
      transform: translateY(-200%);
    }

    .skip-link:focus {
      transform: translateY(0);
    }

    .site-header {
      display: grid;
      width: min(100%, var(--content-width));
      margin: 0 auto;
      padding: 1.2rem var(--page-gutter);
      border-bottom: 1px solid var(--color-border);
      align-items: center;
      grid-template-columns: 1fr auto 1fr;
    }

    .brand {
      display: inline-flex;
      width: fit-content;
      align-items: center;
      gap: 0.7rem;
      color: var(--color-forest-950);
      font-family: var(--font-display);
      font-size: 1.35rem;
      font-weight: 700;
      text-decoration: none;
    }

    .brand-mark {
      display: grid;
      width: 2rem;
      height: 2rem;
      border-radius: 50%;
      background: var(--color-forest-900);
      color: var(--color-cream-100);
      font-family: var(--font-body);
      font-size: 0.78rem;
      place-items: center;
    }

    nav {
      display: flex;
      gap: 2rem;
    }

    nav a {
      padding: 0.5rem 0;
      border-bottom: 1px solid transparent;
      color: var(--color-muted);
      font-size: 0.86rem;
      font-weight: 650;
      letter-spacing: 0.03em;
      text-decoration: none;
    }

    nav a:hover,
    nav a.active {
      border-color: var(--color-forest-800);
      color: var(--color-forest-950);
    }

    .edition {
      display: flex;
      margin: 0;
      align-items: center;
      justify-self: end;
      gap: 0.55rem;
      color: var(--color-muted);
      font-size: 0.75rem;
      letter-spacing: 0.04em;
    }

    .edition span {
      width: 0.45rem;
      height: 0.45rem;
      border-radius: 50%;
      background: var(--color-sage-500);
    }

    main {
      display: block;
      width: 100%;
      flex: 1;
    }

    footer {
      display: flex;
      width: min(100%, var(--content-width));
      margin: 0 auto;
      padding: 1.5rem var(--page-gutter) 2rem;
      border-top: 1px solid var(--color-border);
      justify-content: space-between;
      color: var(--color-muted);
      font-size: 0.75rem;
    }

    footer p {
      margin: 0;
    }

    footer p:first-child {
      color: var(--color-forest-950);
      font-family: var(--font-display);
      font-weight: 700;
    }

    @media (max-width: 46rem) {
      .site-header {
        grid-template-columns: 1fr auto;
      }

      nav {
        gap: 1.2rem;
      }

      .edition {
        display: none;
      }
    }

    @media (max-width: 30rem) {
      .site-header {
        align-items: start;
        gap: 1rem;
        grid-template-columns: 1fr;
      }

      nav {
        width: 100%;
      }

      footer {
        flex-direction: column;
        gap: 0.35rem;
      }
    }
  `,
})
export class AppShell {}

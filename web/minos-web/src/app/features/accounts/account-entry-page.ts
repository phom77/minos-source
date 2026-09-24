import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { SectionHeading } from '../../shared/ui/section-heading';

@Component({
  selector: 'minos-account-entry-page',
  imports: [RouterLink, SectionHeading],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="page">
      <minos-section-heading
        eyebrow="Accounts"
        heading="Your work, kept together."
        summary="Minos uses protected browser sessions backed by the existing ASP.NET Core account boundary. Forms and session state will arrive as the next focused feature slice."
      />

      <section class="account-panel" aria-labelledby="account-foundation-heading">
        <div class="panel-copy">
          <p class="index">02 / Identity</p>
          <h2 id="account-foundation-heading">Session-first by design.</h2>
          <p>
            Angular will request a CSRF token, submit account operations through feature-local data access,
            and ask <code>/accounts/me</code> for the current account. Cookies stay under browser and server control.
          </p>
          <a routerLink="/problems">Return to problems <span aria-hidden="true">→</span></a>
        </div>

        <ol class="flow" aria-label="Planned authentication request flow">
          <li>
            <span>01</span>
            <div>
              <strong>Request protection</strong>
              <p>Fetch a fresh anti-forgery token before a state-changing account request.</p>
            </div>
          </li>
          <li>
            <span>02</span>
            <div>
              <strong>Protected session</strong>
              <p>Let the API issue and validate its secure HttpOnly authentication cookie.</p>
            </div>
          </li>
          <li>
            <span>03</span>
            <div>
              <strong>Current account</strong>
              <p>Keep one small application-wide signal sourced from the authoritative endpoint.</p>
            </div>
          </li>
        </ol>
      </section>
    </article>
  `,
  styles: `
    .page {
      width: min(100%, var(--content-width));
      margin: 0 auto;
      padding: clamp(4rem, 10vw, 8.5rem) var(--page-gutter) 6rem;
    }

    .account-panel {
      display: grid;
      margin-top: clamp(4rem, 10vw, 8rem);
      border-top: 2px solid var(--color-forest-950);
      border-bottom: 1px solid var(--color-border);
      grid-template-columns: minmax(0, 1fr) minmax(20rem, 0.9fr);
    }

    .panel-copy {
      padding: 2rem 3rem 3rem 0;
    }

    .index {
      margin: 0 0 3rem;
      color: var(--color-muted);
      font-size: 0.68rem;
      font-weight: 750;
      letter-spacing: 0.14em;
      text-transform: uppercase;
    }

    h2 {
      margin: 0 0 1rem;
      color: var(--color-forest-950);
      font-family: var(--font-display);
      font-size: clamp(1.8rem, 4vw, 2.8rem);
      font-weight: 500;
    }

    .panel-copy > p:not(.index) {
      max-width: 36rem;
      color: var(--color-muted);
      line-height: 1.75;
    }

    code {
      color: var(--color-forest-800);
      font-size: 0.92em;
    }

    a {
      display: inline-flex;
      margin-top: 1.5rem;
      gap: 0.5rem;
      color: var(--color-forest-800);
      font-size: 0.82rem;
      font-weight: 750;
      text-decoration-thickness: 1px;
      text-underline-offset: 0.3rem;
    }

    .flow {
      margin: 0;
      padding: 0;
      border-left: 1px solid var(--color-border);
      list-style: none;
    }

    .flow li {
      display: grid;
      min-height: 8.5rem;
      padding: 1.5rem 0 1.5rem 2rem;
      border-bottom: 1px solid var(--color-border);
      align-items: start;
      gap: 1.5rem;
      grid-template-columns: 2rem 1fr;
    }

    .flow li:last-child {
      border-bottom: 0;
    }

    .flow span {
      color: var(--color-olive-500);
      font-family: var(--font-display);
      font-style: italic;
    }

    .flow strong {
      color: var(--color-forest-950);
      font-size: 0.88rem;
    }

    .flow p {
      max-width: 29rem;
      margin: 0.55rem 0 0;
      color: var(--color-muted);
      font-size: 0.85rem;
      line-height: 1.65;
    }

    @media (max-width: 48rem) {
      .account-panel {
        grid-template-columns: 1fr;
      }

      .panel-copy {
        padding-right: 0;
      }

      .flow {
        border-top: 1px solid var(--color-border);
        border-left: 0;
      }

      .flow li {
        padding-left: 0;
      }
    }
  `,
})
export class AccountEntryPage {}

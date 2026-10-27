# UI conventions

The Angular application uses standalone components, lazy feature routes and strict TypeScript. Working screens prioritize reading problems, writing code and following real results. Features and statistics without backend support should not appear as decorative controls.

## Navigation and layout

- A compact top header contains Problems, Contests and account controls. Settings is reachable from the user menu; there is no persistent global sidebar.
- Settings has local Profile, Security and Account sections, with contextual editors rather than all forms open together.
- The solve workspace has independent description/editor panes, responsive stacking, expand/restore and results near the editor. A local submissions tab preserves the current problem context.
- Authentication opens in an accessible dialog over the current task. Legacy login/register URLs enter that flow with safe internal return URLs.
- Discussion is collapsed initially and reads lazily when opened. Closing it preserves drafts/pages; changing identity/problem clears them.

## Visual system

Preserve the folded M assets in `public/brand/` and use the shared semantic tokens. Large working surfaces are neutral; green is reserved for primary actions, brand and restrained emphasis.

| Role | Light | Dark |
| --- | --- | --- |
| Canvas | `#f5f5f4` | `#171717` |
| Main panel | `#ffffff` | `#242424` |
| Toolbar/inset | `#f0f0ef` | `#2e2e2e` |
| Editor | `#ffffff` | `#202020` |
| Header | `#fafafa` | `#1d1d1d` |
| Main/muted text | `#262626` / `#626262` | `#e8e8e8` / `#adadad` |
| Primary action | `#2f6b45` | `#39764f` |

Use readable sans-serif text, monospace code, compact hierarchy, fine borders and restrained elevation. Support Vietnamese diacritics. Keep code syntax readable in both themes. Theme initialization runs before Angular and respects system preference until an explicit choice is saved.

## Interaction

Separate validity from error visibility. Unchanged focus/blur must not show errors or `aria-invalid=true`. Before first submit, show errors after edit and blur; once shown, update them while the user corrects input. Submit validates the whole form and focuses an accessible summary/invalid field. Reset, cancel and successful completion clear interaction state. Do not disable submission merely because a form is invalid.

Provide loading, empty, permission, conflict, network, validation and success states. Keep concise useful constraints and material destructive consequences. Omit internal account UUIDs, default role labels, routine infrastructure details, filler slogans and repeated introductions. Confirm destructive account/comment operations explicitly.

Keyboard navigation, dialog focus trapping/restoration, tab semantics, status announcements, reduced motion, touch targets and 320-pixel layouts are part of verification. Browser palette checks measure selected contrast pairs; they are not complete accessibility certification.

## Frontend boundaries

`core` owns session, theme, configuration and transport; `shared` owns feature-independent presentation; `layouts` owns application chrome; features own their routes, APIs and state. Use signals for synchronous state, typed reactive forms for input and RxJS for HTTP/cancellation. Extract shared components when real consumers justify them.

Cookies remain browser-owned. Never put credentials/tokens or submission source in browser storage. Source/retry drafts are bounded, account-owned and in memory. Cancel identity-scoped reads and ignore stale responses when identity changes. Poll active submissions serially, pause hidden tabs and stop on terminal states or errors requiring refresh.

Render problem Markdown/math and comment Markdown with their existing safe allowlists. Comments do not load remote images or raw HTML. Keep private tests and source evidence out of public views. API authorization remains final regardless of guards or hidden controls.

Only implement settings supported by verified contracts. Google linking, email changes/recovery, MFA, billing and notification preferences need new contracts before UI controls. See [frontend development](../web/minos-web/README.md) for commands and [development verification](development.md#verification) for browser journeys.

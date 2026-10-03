# Minos reference assets

These PNG files are unchanged copies of the owner-approved originals in `docs/design/references/`:

- `light-minos.png`: light folded M on an opaque dark background.
- `dark-minos.png`: dark folded M on an opaque light background.

The shell selects `dark-minos.png` for light navigation and `light-minos.png` for dark navigation, including the mobile drawer. Both use the same SVG viewport (`320 320 615 615`) to remove presentation margins without changing source pixels, facets, or proportions. The favicon switches to the same supplied variant before Angular starts and on theme changes. This is not a new logo drawing or a transparent cutout; each mark retains a small amount of its opaque original background.

The wordmark uses the application system sans-serif stack, not a claimed exact font match. Reviewed, optimized small-size/vector derivatives can replace these presentation PNGs later; keep the reference originals unchanged. The authentication popup selects the same theme-specific assets and presentation viewport.

`google-g.svg` preserves the G paths and colors from Google's official [sign-in HTML configurator](https://developers.google.com/identity/branding-guidelines#render_html_button_element), retrieved 2026-10-03. It is a provider brand asset, not a Minos-logo derivative. It is displayed at 18px with a text action and neutral provider-button theme. Google's marks remain Google's property; see its [branding guidelines](https://developers.google.com/identity/branding-guidelines). No remote logo request or Google script is needed at runtime.

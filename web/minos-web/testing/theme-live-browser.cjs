// Read-only verification of the deployed SPA. No account, fixture, submission or API interception.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  const out = path.resolve(__dirname, '../../../artifacts/neutral-theme/live'); await fs.mkdir(out, { recursive: true });
  const base = process.env.MINOS_LIVE_BROWSER_URL || 'https://localhost:8443';
  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
    const page = await context.newPage(), errors = [], writes = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => { if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method())) writes.push(request.method()); });
    let problemUrl;
    for (const theme of ['dark', 'light']) {
      await page.goto(base + '/problems');
      const first = page.locator('tbody th a').first(); await first.waitFor();
      problemUrl = await first.getAttribute('href'); assert.match(problemUrl, /^\/problems\/[0-9a-f-]{36}/);
      if (await page.locator('html').getAttribute('data-theme') !== theme) await page.getByRole('button', { name: `Switch to ${theme} mode` }).click();
      await page.waitForFunction(theme => document.documentElement.dataset.theme === theme, theme);
      await page.evaluate(() => document.activeElement?.blur());
      await page.screenshot({ path: path.join(out, `library-${theme}.png`), fullPage: true });
      await page.goto(base + problemUrl);
      const source = page.getByLabel('C++20 source code', { exact: true });
      await source.fill('#include <iostream>\n\nint main() {\n    // Unsent layout check\n    return 0;\n}\n');
      const before = await source.inputValue();
      for (const width of [1440, 390, 320]) {
        await page.setViewportSize({ width, height: 1000 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
        assert.equal(await source.inputValue(), before);
        if (width !== 320) await page.screenshot({ path: path.join(out, `solve-${theme}-${width}.png`), fullPage: true });
      }
      await page.setViewportSize({ width: 1440, height: 1000 });
      await page.getByRole('button', { name: 'Discussion', exact: true }).click();
      await page.getByRole('button', { name: 'Sign in to comment', exact: true }).waitFor();
      await page.getByRole('button', { name: 'Sign in to comment', exact: true }).click();
      await page.getByRole('dialog').waitFor();
      await page.screenshot({ path: path.join(out, `sign-in-${theme}.png`) }); await page.keyboard.press('Escape');
      await page.goto(base + '/contests'); await page.getByRole('heading', { name: 'Contests', exact: true }).waitFor();
      await page.screenshot({ path: path.join(out, `contests-${theme}.png`), fullPage: true });
      await page.reload(); assert.equal(await page.locator('html').getAttribute('data-theme'), theme);
      const header = await page.locator('.workspace-header').evaluate(element => getComputedStyle(element).backgroundColor);
      assert.equal(header, theme === 'dark' ? 'rgb(29, 29, 29)' : 'rgb(250, 250, 250)');
    }
    assert.deepEqual(writes, []); assert.deepEqual(errors, []);
    await fs.writeFile(path.join(out, 'result.json'), JSON.stringify({ realHttps: true, readOnly: true, noApiFixtures: true, themes: ['light', 'dark'], widths: [320, 390, 1440] }, null, 2));
    await context.close();
    console.log('Real HTTPS neutral theme passed: library, solve, discussion sign-in, contests, theme persistence and 320–1440px; no writes or API fixtures.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });

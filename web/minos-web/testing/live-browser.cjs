// Invoked by scripts/checks/http-smoke.py with temporary fixture identities; credentials stay out of arguments/artifacts.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
(async () => {
  const base = process.env.MINOS_LIVE_BROWSER_URL, pid = process.env.MINOS_LIVE_BROWSER_PROBLEM;
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage(); const errors = []; const keys = []; let admitted;
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(base + `/problems/${pid}`);
    await page.getByRole('button', { name: 'Sign in to submit', exact: true }).click();
    await page.getByLabel('Email address', { exact: true }).fill(process.env.MINOS_LIVE_BROWSER_EMAIL);
    await page.getByLabel('Password', { exact: true }).fill(process.env.MINOS_LIVE_BROWSER_PASSWORD);
    await Promise.all([page.waitForResponse(response => response.url().endsWith('/accounts/login') && response.status() === 200),
      page.getByRole('dialog').getByRole('button', { name: 'Sign in', exact: true }).click()]);
    await page.getByLabel('Email address', { exact: true }).waitFor({ state: 'hidden' });
    console.log('Real browser sign-in passed.');
    await page.route(`**/api/problems/${pid}/submissions`, async route => {
      keys.push(route.request().postDataJSON().requestKey);
      const response = await route.fetch({ maxRetries: 0 }); assert.equal(response.status(), 201);
      const body = await response.json();
      if (keys.length === 1) { admitted = body.id; return route.abort('failed'); }
      assert.equal(body.id, admitted); assert.equal(keys[0], keys[1]);
      return route.fulfill({ response });
    });
    await page.goto(base + `/problems/${pid}`);
    await page.getByLabel('C++20 source code').fill(process.env.MINOS_LIVE_BROWSER_SOURCE);
    await page.getByRole('button', { name: 'Submit', exact: true }).click();
    await page.getByRole('button', { name: 'Retry submission', exact: true }).click();
    await page.getByText('Accepted', { exact: true }).first().waitFor({ timeout: 120000 }); assert.equal(keys.length, 2);
    console.log('Real browser practice replay and Worker Accepted passed.');
    await page.getByRole('link', { name: 'View details', exact: true }).click();
    await page.waitForURL(`**/submissions/${admitted}`); await page.getByText('Accepted', { exact: true }).first().waitFor();
    const out = path.resolve(__dirname, '../../../artifacts/phase1-live-browser'); await fs.mkdir(out, { recursive: true });
    for (const width of [320, 1440]) {
      await page.setViewportSize({ width, height: 1000 });
      await page.screenshot({ path: path.join(out, `owner-result-${width}.png`), fullPage: true });
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `owner result overflows at ${width}px`);
    }
    await page.goto(base + '/settings/profile');
    await page.getByRole('navigation', { name: 'Settings sections' }).waitFor();
    await page.getByRole('link', { name: 'Security', exact: true }).click(); await page.waitForURL('**/settings/security');
    await page.getByRole('link', { name: 'Account', exact: true }).click(); await page.waitForURL('**/settings/account');
    const contest = process.env.MINOS_LIVE_BROWSER_CONTEST, cp = process.env.MINOS_LIVE_BROWSER_CONTEST_PROBLEM;
    await page.goto(base + `/contests/${contest}/problems/${cp}`);
    await page.getByLabel('C++20 source code').fill(process.env.MINOS_LIVE_BROWSER_SOURCE);
    await page.getByRole('button', { name: 'Submit to contest', exact: true }).click();
    await page.getByRole('link', { name: 'View judging result', exact: true }).click();
    await page.getByText('Accepted', { exact: true }).first().waitFor({ timeout: 120000 });
    assert.deepEqual(errors, []);
    console.log('Real HTTPS browser passed: login, lost-response practice replay, Worker Accepted, owner evidence, Settings navigation and contest Worker Accepted.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exit(1); });

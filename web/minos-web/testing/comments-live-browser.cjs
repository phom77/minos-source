// Uses only the temporary account/problem supplied by scripts/checks/http-smoke.py. No API fixtures or stored browser credentials.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
  const base = process.env.MINOS_LIVE_BROWSER_URL, pid = process.env.MINOS_LIVE_BROWSER_PROBLEM;
  assert.ok(base && pid && process.env.MINOS_LIVE_BROWSER_EMAIL && process.env.MINOS_LIVE_BROWSER_PASSWORD);
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage(), errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(base + `/problems/${pid}`);
    const comments = page.locator('minos-problem-comments');
    await comments.getByRole('button', { name: 'Discussion', exact: true }).click();
    await comments.getByRole('button', { name: 'Sign in to comment', exact: true }).click();
    await page.getByLabel('Email address', { exact: true }).fill(process.env.MINOS_LIVE_BROWSER_EMAIL);
    await page.getByLabel('Password', { exact: true }).fill(process.env.MINOS_LIVE_BROWSER_PASSWORD);
    await page.getByRole('dialog').getByRole('button', { name: 'Sign in', exact: true }).click();
    await page.getByRole('dialog').waitFor({ state: 'hidden' });
    const source = page.getByRole('textbox', { name: 'C++20 source code', exact: true });
    await source.fill('// unsent comment smoke fixture');
    const editor = comments.getByRole('textbox', { name: 'Your comment', exact: true });
    await editor.fill('**Real browser comment** — kiểm tra PostgreSQL\n\n```cpp\nreturn a + b;\n```');
    await comments.getByRole('checkbox', { name: 'Contains spoilers', exact: true }).check();
    const posted = page.waitForResponse(response => response.url().endsWith(`/api/problems/${pid}/comments`) && response.request().method() === 'POST' && response.status() === 201);
    await comments.getByRole('button', { name: 'Comment', exact: true }).click();
    const value = await (await posted).json();
    let root = comments.locator('#comment-' + value.id);
    await root.getByRole('button', { name: 'Show spoiler', exact: true }).focus();
    await page.keyboard.press('Enter');
    await root.getByText('Real browser comment', { exact: true }).waitFor();
    assert.equal(await root.locator('pre code').textContent(), 'return a + b;\n');
    await root.getByRole('button', { name: 'Edit', exact: true }).click();
    await root.getByRole('textbox', { name: 'Edit comment', exact: true }).fill('Edited in the real browser — đã lưu');
    await root.getByRole('checkbox', { name: 'Contains spoilers', exact: true }).uncheck();
    await root.getByRole('button', { name: 'Save changes', exact: true }).click();
    await root.getByText('Edited in the real browser — đã lưu', { exact: true }).waitFor();
    assert.equal(await source.inputValue(), '// unsent comment smoke fixture');
    await page.reload();
    await comments.getByRole('button', { name: 'Discussion', exact: true }).click();
    root = comments.locator('#comment-' + value.id);
    await root.getByText('Edited in the real browser — đã lưu', { exact: true }).waitFor();
    await root.getByRole('button', { name: 'Reply', exact: true }).click();
    await comments.getByRole('textbox', { name: 'Reply to Docker fixture solver', exact: true }).fill('A persisted browser reply.');
    await comments.getByRole('button', { name: 'Post reply', exact: true }).click();
    await comments.getByText('A persisted browser reply.', { exact: true }).waitFor();
    const out = path.resolve(__dirname, '../../../artifacts/comments-ui-refinement/live');
    await fs.mkdir(out, { recursive: true });
    for (const theme of ['light', 'dark']) {
      if (theme === 'dark') await page.getByRole('button', { name: 'Switch to dark mode', exact: true }).click();
      for (const width of [1440, 390]) {
        await page.setViewportSize({ width, height: 1000 });
        await comments.getByRole('heading', { name: 'Discussion', exact: true }).evaluate(element => element.scrollIntoView({ block: 'start' }));
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
        await page.screenshot({ path: path.join(out, `live-${theme}-${width}.png`), fullPage: true });
      }
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
    await root.getByRole('button', { name: 'Delete', exact: true }).click();
    await root.getByRole('button', { name: 'Confirm delete', exact: true }).click();
    await root.getByText('Comment deleted.', { exact: true }).waitFor();
    assert.match(await comments.innerText(), /A persisted browser reply/);
    assert.deepEqual(errors, []);
    await context.close();
    console.log('Real HTTPS browser comments passed: login, Markdown/spoiler, create/edit/reload/reply/delete, preserved code, light/dark and responsive layout.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });

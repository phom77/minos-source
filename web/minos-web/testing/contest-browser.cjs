// Run with a Playwright installation on NODE_PATH; fixtures are confined to this browser check.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const cid = '11111111-1111-4111-8111-111111111111', pid = '22222222-2222-4222-8222-222222222222';
const vid = '33333333-3333-4333-8333-333333333333', sid = '44444444-4444-4444-8444-444444444444';
const now = new Date().toISOString();
const problem = { problemId: pid, versionId: vid, versionNumber: 1, title: 'Tổng hai số — Sum of two numbers', statement: 'Read two integers and print their sum.',
  timeLimitMilliseconds: 1000, memoryLimitMegabytes: 64, checker: 'Exact', privateTestCount: 1, publishedAtUtc: now, samples: [{ input: '1 2\n', output: '3\n' }] };
let role = 'Setter', joined = false, phase = 'Draft', created = false, submitted = false;
const submissionKeys = [];
let contest = { id: cid, title: 'Minos practice', description: 'A short practice contest.', startsAtUtc: '2099-10-09T01:00:00Z', endsAtUtc: '2099-10-09T03:00:00Z', revision: 2, problemCount: 1 };
const details = () => ({ ...contest, state: phase, canManage: role === 'Setter', isRegistered: joined, canRegister: role === 'Solver' && !joined && phase !== 'Ended',
  canSubmit: role === 'Solver' && joined && phase === 'Live', serverTimeUtc: now,
  problems: role === 'Setter' || phase === 'Live' || phase === 'Ended' ? [{ id: pid, problemId: pid, versionId: vid, order: 1, title: problem.title }] : [] });
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.route('**/accounts/**', async route => {
    const url = new URL(route.request().url());
    const value = url.pathname.endsWith('/csrf') ? { requestToken: 'browser-fixture-csrf' } :
      { id: role === 'Setter' ? cid : sid, displayName: role === 'Setter' ? 'Minos organizer' : 'Participant', role, createdAtUtc: now };
    await route.fulfill({ json: value });
  });
  await page.route('**/api/**', async route => {
    const req = route.request(), url = new URL(req.url()), pathname = url.pathname;
    if (pathname === '/api/problems') return route.fulfill({ json: { items: [problem], page: 1, pageSize: 20, total: 1 } });
    if (pathname === '/api/contests' && req.method() === 'POST') {
      const body = req.postDataJSON(); assert.deepEqual(body.versionIds, [vid]); assert.ok(body.startsAtUtc.endsWith('Z'));
      contest = { ...contest, ...body }; created = true; return route.fulfill({ status: 201, json: details() });
    }
    if (pathname === '/api/contests' || pathname === '/api/contests/mine') return route.fulfill({ json: { items: created ? [details()] : [], page: 1, pageSize: 20, total: created ? 1 : 0 } });
    if (pathname.endsWith('/publish')) { phase = 'Scheduled'; return route.fulfill({ json: details() }); }
    if (pathname.endsWith('/registration')) { joined = true; return route.fulfill({ json: details() }); }
    if (pathname.endsWith('/submissions/mine')) return route.fulfill({ json: { items: [], page: 1, pageSize: 20, total: 0 } });
    if (pathname.endsWith('/submissions')) {
      assert.equal(pathname, `/api/contests/${cid}/problems/${pid}/submissions`);
      assert.equal(req.postDataJSON().language, 'cpp20');
      const key = req.postDataJSON().requestKey; assert.match(key, /^[0-9a-f-]{36}$/); submissionKeys.push(key);
      submitted = true;
      if (submissionKeys.length === 1) { phase = 'Ended'; return route.abort('failed'); }
      assert.equal(submissionKeys[1], submissionKeys[0]);
      return route.fulfill({ status: 201, json: { id: sid } });
    }
    if (pathname === `/api/contests/${cid}/problems/${pid}`) return route.fulfill({ json: problem });
    if (pathname === `/api/contests/${cid}`) return route.fulfill({ json: details() });
    throw new Error(`Unmocked request: ${req.method()} ${pathname}`);
  });
  const out = path.resolve(__dirname, '../../../artifacts/contest-browser'); await fs.mkdir(out, { recursive: true });
  await page.goto((process.env.MINOS_BROWSER_BASE_URL || 'http://127.0.0.1:4320') + '/contests');
  await page.getByRole('link', { name: 'Create contest', exact: true }).click();
  await page.getByLabel('Contest title', { exact: true }).focus(); await page.keyboard.press('Tab');
  assert.equal(await page.getByLabel('Contest title', { exact: true }).getAttribute('aria-invalid'), null);
  await page.getByRole('button', { name: 'Save draft' }).click(); await page.getByRole('alert').waitFor();
  assert.equal(await page.evaluate(() => document.activeElement.getAttribute('role')), 'alert');
  await page.getByLabel('Contest title', { exact: true }).fill('Minos Weekly — Luyện tập thuật toán');
  await page.getByLabel('Description (optional)').fill('Solve a focused set of programming problems.');
  await page.getByLabel('Starts', { exact: true }).fill('2099-10-09T08:00'); await page.getByLabel('Ends', { exact: true }).fill('2099-10-09T10:00');
  await page.getByRole('checkbox', { name: problem.title }).check();
  for (const width of [320, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `editor overflows at ${width}`);
    await page.screenshot({ path: path.join(out, `editor-${width}.png`), fullPage: true });
  }
  await page.getByRole('button', { name: 'Save draft' }).click(); await page.waitForURL(`**/contests/${cid}`); assert.ok(created);
  await page.getByRole('link', { name: 'Edit contest' }).click(); await page.getByRole('button', { name: 'Review publication' }).click();
  await page.getByRole('button', { name: 'Confirm publish' }).click(); await page.waitForURL(`**/contests/${cid}`);
  assert.equal(phase, 'Scheduled');
  role = 'Solver'; await page.reload();
  await page.getByRole('button', { name: 'Register for contest' }).click(); await page.getByText('You are registered.', { exact: false }).waitFor(); assert.ok(joined);
  assert.equal(await page.getByRole('link', { name: problem.title, exact: false }).count(), 0);
  phase = 'Live'; await page.reload(); await page.getByRole('link', { name: problem.title, exact: false }).click();
  await page.getByLabel('C++20 source code').fill('int main() { return 0; }');
  await page.getByRole('button', { name: 'Submit to contest' }).click();
  await page.getByRole('button', { name: 'Retry submission' }).waitFor();
  await page.getByRole('button', { name: 'Retry submission' }).click();
  await page.getByRole('link', { name: 'View judging result' }).waitFor(); assert.ok(submitted); assert.equal(submissionKeys.length, 2);
  for (const width of [320, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `solve overflows at ${width}`);
    await page.screenshot({ path: path.join(out, `solve-${width}.png`), fullPage: true });
  }
  await page.getByRole('button', { name: 'Switch to dark mode' }).click(); await page.screenshot({ path: path.join(out, 'solve-dark.png'), fullPage: true });
  await page.getByLabel('C++20 source code').focus(); await page.keyboard.press('Tab');
  assert.notEqual(await page.evaluate(() => document.activeElement.tagName), 'BODY');
  assert.deepEqual(errors, []); console.log('Contest browser checks passed: organizer creation/publication, participant registration/submission, keyboard and responsive widths. API fixtures only.');
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });

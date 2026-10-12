const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const pid = '22222222-2222-4222-8222-222222222222', vid = '33333333-3333-4333-8333-333333333333';
const sid = '44444444-4444-4444-8444-444444444444', aid = '55555555-5555-4555-8555-555555555555';
const now = new Date().toISOString();
const problem = { problemId: pid, versionId: vid, versionNumber: 1, title: 'Browser sum', statement: 'Print the sum.',
  timeLimitMilliseconds: 1000, memoryLimitMegabytes: 64, checker: 'Exact', privateTestCount: 1, publishedAtUtc: now, samples: [{ input: '1 2\n', output: '3\n' }] };
const completed = { id: sid, problemId: pid, problemVersionId: vid, language: 'cpp20', toolchainId: 'pinned-fixture', source: 'int main(){}',
  status: 'Completed', createdAtUtc: now, completedAtUtc: now, attempts: [{ id: aid, number: 1, status: 'Succeeded', failureKind: null, startedAtUtc: now, finishedAtUtc: now }],
  configuration: { timeLimitMilliseconds: 1000, memoryLimitMegabytes: 64, checker: 'Exact', testCount: 1 },
  result: { attemptId: aid, verdict: 'Accepted', compilerDiagnostics: '', maximumCpuTimeMilliseconds: 1, maximumWallTimeMilliseconds: 2, maximumMemoryBytes: 1024,
    completedAtUtc: now, tests: [{ order: 1, status: 'Passed', cpuTimeMilliseconds: 1, wallTimeMilliseconds: 2, memoryPeakBytes: 1024 }],
    testSummary: { totalTests: 1, executedTests: 1, passedTests: 1, failedTests: 0 } } };
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = [], keys = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/accounts/**', route => route.fulfill({ json: route.request().url().endsWith('/csrf') ? { requestToken: 'fixture-csrf' }
      : { id: aid, displayName: 'Browser solver', role: 'Solver', createdAtUtc: now } }));
    await page.route('**/api/**', async route => {
      const req = route.request(), url = new URL(req.url());
      if (req.method() === 'POST' && url.pathname === `/api/problems/${pid}/submissions`) {
        const body = req.postDataJSON(); assert.equal(body.language, 'cpp20'); assert.match(body.requestKey, /^[0-9a-f-]{36}$/);
        keys.push(body.requestKey);
        // The first response is lost after the fixture accepted the work; the next request must replay its key.
        if (keys.length === 1) return route.abort('failed');
        return route.fulfill({ status: 201, json: completed });
      }
      if (url.pathname === `/api/submissions/${sid}`) return route.fulfill({ json: completed });
      if (url.pathname === '/api/me/submissions') return route.fulfill({ json: { page: 1, pageSize: 20, total: 1, items: [{ ...completed, verdict: 'Accepted' }] } });
      if (url.pathname === '/api/problems') return route.fulfill({ json: { page: 1, pageSize: 20, total: 1, items: [problem] } });
      if (url.pathname === `/api/problems/${pid}` || url.pathname === `/api/problems/versions/${vid}`) return route.fulfill({ json: problem });
      throw new Error(`Unexpected browser fixture request: ${req.method()} ${url.pathname}`);
    });
    await page.goto(process.env.MINOS_BROWSER_BASE_URL + `/problems/${pid}`);
    const source = page.getByLabel('C++20 source code');
    await source.focus(); await page.keyboard.press('Tab'); assert.equal(await source.getAttribute('aria-invalid'), null);
    await page.getByRole('button', { name: 'Submit', exact: true }).click();
    await page.getByRole('alert', { name: 'Submission error' }).waitFor();
    assert.equal(await page.evaluate(() => document.activeElement.getAttribute('role')), 'alert');
    await source.fill('int main(){}'); await page.getByRole('button', { name: 'Submit', exact: true }).click();
    await page.getByRole('button', { name: 'Retry submission', exact: true }).waitFor();
    await page.getByRole('button', { name: 'Retry submission', exact: true }).click();
    await page.getByText('Accepted', { exact: true }).first().waitFor();
    assert.equal(keys.length, 2); assert.equal(keys[0], keys[1]);
    await Promise.all([page.waitForResponse(response => response.url().endsWith(`/api/problems/${pid}/submissions`) && response.status() === 201),
      page.getByRole('button', { name: 'Submit', exact: true }).click()]);
    assert.notEqual(keys[2], keys[1]);
    const out = path.resolve(__dirname, '../../../artifacts/submission-browser'); await fs.mkdir(out, { recursive: true });
    for (const width of [320, 768, 1024, 1440]) {
      await page.setViewportSize({ width, height: 1000 });
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `practice overflows at ${width}`);
      await page.screenshot({ path: path.join(out, `practice-${width}.png`), fullPage: true });
    }
    await page.goto(process.env.MINOS_BROWSER_BASE_URL + `/submissions/${sid}`);
    await page.getByText('Accepted', { exact: true }).first().waitFor();
    await page.getByRole('link', { name: 'Submission history', exact: false }).click();
    await page.waitForURL('**/submissions'); await page.getByText('Browser sum', { exact: true }).filter({ visible: true }).first().waitFor();
    assert.deepEqual(errors, []);
    console.log('Practice browser passed: validation/focus, lost response replay, new submission, owner result/history and responsive widths. API fixtures only.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });

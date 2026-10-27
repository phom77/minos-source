const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const pid = '22222222-2222-4222-8222-222222222222', vid = '33333333-3333-4333-8333-333333333333';
const sid = '44444444-4444-4444-8444-444444444444', aid = '55555555-5555-4555-8555-555555555555';
const now = new Date().toISOString(), base = process.env.MINOS_BROWSER_BASE_URL;
const source = '#include <iostream>\nint main() {\n  std::cout << 42;\n}\n';
const problem = { problemId: pid, versionId: vid, versionNumber: 1, title: 'Pairwise sums', statement: 'Print the answer.',
  timeLimitMilliseconds: 1000, memoryLimitMegabytes: 128, checker: 'Exact', privateTestCount: 1, publishedAtUtc: now, samples: [] };
const result = { id: sid, problemId: pid, problemVersionId: vid, language: 'cpp20', toolchainId: 'fixture', source,
  status: 'Completed', createdAtUtc: now, completedAtUtc: now, attempts: [],
  configuration: { timeLimitMilliseconds: 1000, memoryLimitMegabytes: 128, checker: 'Exact', testCount: 1 },
  result: { attemptId: aid, verdict: 'Accepted', compilerDiagnostics: '', completedAtUtc: now, comparisonEligible: true,
    maximumCpuTimeMilliseconds: 50, maximumWallTimeMilliseconds: 65, maximumMemoryBytes: 8 * 1048576,
    tests: [{ order: 1, status: 'Passed', cpuTimeMilliseconds: 50, wallTimeMilliseconds: 65, memoryPeakBytes: 8 * 1048576 }] } };
const buckets = counts => Array.from({ length: 20 }, (_, index) => ({ index, count: counts[index] ?? 0 }));
const comparison = { submissionId: sid, status: 'Ready', sampleCount: 100, minimumSampleCount: 20,
  runtime: { value: 50, beatsPercentage: 75, equalCount: 5, rangeMaximum: 101, buckets: buckets({ 2: 20, 9: 5, 19: 75 }) },
  memory: { value: 8 * 1048576, beatsPercentage: 55, equalCount: 10, rangeMaximum: 16 * 1048576 + 1, buckets: buckets({ 2: 35, 9: 10, 19: 55 }) } };

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  const out = path.resolve(__dirname, '../../../artifacts/submission-performance'); await fs.mkdir(out, { recursive: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } }), errors = [];
    let mode = 'ready', reads = 0;
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/accounts/**', route => route.fulfill({ json: route.request().url().endsWith('/csrf') ? { requestToken: 'fixture' }
      : { id: aid, displayName: 'Performance reviewer', role: 'Solver', createdAtUtc: now } }));
    await page.route('**/api/**', route => {
      const url = new URL(route.request().url());
      if (url.pathname.endsWith('/comments') && route.request().method() === 'GET') return route.fulfill({ json: { items: [], nextCursor: null } });
      if (url.pathname === '/api/problems/' + pid || url.pathname === '/api/problems/versions/' + vid) return route.fulfill({ json: problem });
      if (url.pathname === '/api/me/submissions') return route.fulfill({ json: { items: [{ ...result, verdict: 'Accepted' }], page: 1, pageSize: 20, total: 1 } });
      if (url.pathname === '/api/submissions/' + sid) return route.fulfill({ json: { ...result, result: { ...result.result, comparisonEligible: mode !== 'legacy' } } });
      if (url.pathname === '/api/submissions/' + sid + '/performance') {
        reads++;
        if (mode === 'failed') return route.fulfill({ status: 503, json: {} });
        if (mode === 'small') return route.fulfill({ json: { ...comparison, status: 'InsufficientSamples', sampleCount: 3,
          runtime: { ...comparison.runtime, beatsPercentage: null, equalCount: null, rangeMaximum: 0, buckets: [] },
          memory: { ...comparison.memory, beatsPercentage: null, equalCount: null, rangeMaximum: 0, buckets: [] } } });
        if (mode === 'ties') return route.fulfill({ json: { ...comparison, sampleCount: 20,
          runtime: { ...comparison.runtime, beatsPercentage: 0, equalCount: 20, buckets: buckets({ 9: 20 }) },
          memory: { ...comparison.memory, beatsPercentage: 0, equalCount: 20, buckets: buckets({ 9: 20 }) } } });
        return route.fulfill({ json: comparison });
      }
      throw new Error('Unexpected performance fixture request: ' + url.pathname);
    });
    await page.goto(base + `/problems/${pid}?tab=submissions&submission=${sid}`);
    await page.getByText('Beats 75.00%', { exact: true }).waitFor();
    assert.equal(reads, 1);
    assert.equal(await page.locator('.chart-bar').count(), 20);
    const runtime = page.getByRole('tab', { name: /^Runtime/ }), memory = page.getByRole('tab', { name: /^Memory/ });
    await runtime.focus(); await page.keyboard.press('End');
    await page.locator('[data-metric="memory"][aria-selected="true"]').waitFor();
    assert.match(await page.locator('.distribution figcaption').innerText(), /100 other accounts · 10 equal/);
    await page.keyboard.press('Home');
    await page.locator('[data-metric="runtime"][aria-selected="true"]').waitFor();
    assert.equal(reads, 1, 'switching metric must not read the API again');
    for (const theme of ['light', 'dark']) {
      if (theme === 'dark') await page.getByRole('button', { name: 'Switch to dark mode', exact: true }).click();
      for (const width of [1440, 1024, 768, 390, 320]) {
        await page.setViewportSize({ width, height: 900 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `${theme} performance overflows at ${width}`);
        if (width >= 1024) assert.ok(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight + 1));
        await page.screenshot({ path: path.join(out, `problem-${theme}-${width}.png`), fullPage: true });
      }
    }
    assert.equal(reads, 1, 'responsive changes must not refetch');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(base + '/submissions/' + sid);
    await page.getByText('Beats 75.00%', { exact: true }).waitFor();
    await page.screenshot({ path: path.join(out, 'detail-dark-1440.png'), fullPage: true });
    mode = 'small'; await page.reload();
    await page.getByText('Not enough comparable results:', { exact: false }).waitFor();
    assert.equal(await page.locator('.chart-plot, .metric-beats').count(), 0);
    assert.match(await page.locator('.performance-panel').innerText(), /3 \/ 20 other accounts/);
    mode = 'failed'; await page.reload();
    await page.getByRole('button', { name: 'Try again', exact: true }).waitFor();
    assert.match(await page.locator('.performance-panel').innerText(), /50 ms/);
    mode = 'ready'; await page.getByRole('button', { name: 'Try again', exact: true }).click();
    await page.getByText('Beats 75.00%', { exact: true }).waitFor();
    mode = 'ties'; await page.reload();
    await page.getByText('Beats 0.00%', { exact: true }).first().waitFor();
    assert.match(await page.locator('.distribution figcaption').innerText(), /20 equal/);
    mode = 'legacy'; const beforeLegacy = reads; await page.reload();
    await page.getByText('Comparison is unavailable for this historical measurement.', { exact: true }).waitFor();
    assert.equal(reads, beforeLegacy);
    assert.equal(await page.locator('.chart-plot, .metric-beats').count(), 0);
    assert.equal(await page.locator('.submitted-source code').textContent(), source);
    assert.deepEqual(errors, []);
    console.log('Performance browser passed: owner detail and in-problem graphs, one request for both metrics, keyboard, 20 bars, responsive light/dark, ties, small samples, retry and legacy evidence. API fixtures only.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

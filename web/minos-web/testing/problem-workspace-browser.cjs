const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const pid = '22222222-2222-4222-8222-222222222222', vid = '33333333-3333-4333-8333-333333333333';
const sid = '44444444-4444-4444-8444-444444444444', aid = '55555555-5555-4555-8555-555555555555';
const now = new Date().toISOString(), base = process.env.MINOS_BROWSER_BASE_URL;
const source = '#include <iostream>\nint main() {\n\tstd::cout << "hello";\n\treturn 0;\n}\n';
const problem = { problemId: pid, versionId: vid, versionNumber: 1, title: 'Cupboards', publishedAtUtc: now,
  statement: 'Choose the minimum number of doors to close.\n\nInput\n\nRead n pairs of integers.\n\nOutput\n\nPrint the minimum.\n\nSource: https://codeforces.com/problemset/problem/248/A\nDataset: DeepMind CodeContests, CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/). Original-source terms also apply.',
  timeLimitMilliseconds: 2000, memoryLimitMegabytes: 244, checker: 'Exact', privateTestCount: 93,
  samples: [{ input: '2\n0 1\n1 1\n', output: '1\n' }] };
let submission = { id: sid, problemId: pid, problemVersionId: vid, language: 'cpp20', toolchainId: 'fixture', source,
  status: 'Completed', createdAtUtc: now, completedAtUtc: now, attempts: [],
  configuration: { timeLimitMilliseconds: 2000, memoryLimitMegabytes: 244, checker: 'Exact', testCount: 93 },
  result: { attemptId: aid, verdict: 'Accepted', compilerDiagnostics: '', completedAtUtc: now,
    maximumCpuTimeMilliseconds: 102, maximumWallTimeMilliseconds: 115, maximumMemoryBytes: 2097152,
    tests: Array.from({ length: 93 }, (_, index) => ({ order: index + 1, status: 'Passed', cpuTimeMilliseconds: 12, wallTimeMilliseconds: 24, memoryPeakBytes: 2097152 })) } };

function contrast(a, b) {
  const luminance = value => {
    const rgb = value.match(/[\d.]+/g).slice(0, 3).map(Number).map(channel => {
      const s = channel / 255; return s <= .04045 ? s / 12.92 : ((s + .055) / 1.055) ** 2.4;
    });
    return rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722;
  };
  const x = luminance(a), y = luminance(b); return (Math.max(x, y) + .05) / (Math.min(x, y) + .05);
}

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  const out = path.resolve(__dirname, '../../../artifacts/problem-workspace'); await fs.mkdir(out, { recursive: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } }), errors = [], historyReads = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/accounts/**', route => route.fulfill({ json: route.request().url().endsWith('/csrf') ? { requestToken: 'fixture' }
      : { id: aid, displayName: 'Workspace reviewer', role: 'Solver', createdAtUtc: now } }));
    await page.route('**/api/**', route => {
      const request = route.request(), url = new URL(request.url());
      if (url.pathname.endsWith('/comments') && request.method() === 'GET') return route.fulfill({ json: { items: [], nextCursor: null } });
      if (url.pathname === '/api/problems/' + pid) return route.fulfill({ json: problem });
      if (url.pathname === '/api/me/submissions') {
        assert.equal(url.searchParams.get('problemId'), pid, 'history must be filtered by the server');
        const number = Number(url.searchParams.get('page')); historyReads.push(number);
        return route.fulfill({ json: { page: number, pageSize: 20, total: 21, items: [{ id: sid, problemId: pid, problemVersionId: vid,
          status: 'Completed', verdict: 'Accepted', createdAtUtc: now, completedAtUtc: now }] } });
      }
      if (url.pathname === '/api/submissions/' + sid) return route.fulfill({ json: submission });
      if (request.method() === 'POST' && url.pathname === '/api/problems/' + pid + '/submissions') {
        submission = { ...submission, source: request.postDataJSON().source };
        return route.fulfill({ status: 201, json: submission });
      }
      throw new Error('Unexpected workspace fixture request: ' + url.pathname);
    });
    await page.goto(base + '/problems/' + pid);
    const editor = page.getByRole('textbox', { name: 'C++20 source code', exact: true }); await editor.fill(source);
    await page.waitForFunction(() => document.querySelector('.syntax-layer code')?.textContent.includes('return'));
    await editor.evaluate(element => { element.dataset.persistentEditor = 'original'; });
    assert.doesNotMatch(await page.locator('.problem-statement').innerText(), /Source:|Dataset:/);
    assert.equal(await page.locator('.problem-attribution').count(), 0);
    assert.doesNotMatch(await page.locator('#problem-description-panel').innerText(), /Source & license|Dataset:|DeepMind CodeContests|Original-source/);
    const panelColors = await page.locator('.description-pane').evaluate(element => {
      const style = getComputedStyle(element); return [style.borderTopColor, style.backgroundColor];
    });
    assert.ok(contrast(...panelColors) >= 3, `light pane border contrast ${contrast(...panelColors)}`);
    await page.locator('.description-content').evaluate(element => { element.scrollTop = 0; });
    await page.screenshot({ path: path.join(out, 'description-light-1440.png'), fullPage: true });
    const description = page.getByRole('tab', { name: 'Description', exact: true });
    const submissions = page.getByRole('tab', { name: 'Submissions', exact: true });
    await description.focus(); await page.keyboard.press('End');
    await page.locator('.submission-select').waitFor();
    assert.equal(await submissions.getAttribute('aria-selected'), 'true');
    assert.equal(new URL(page.url()).pathname, '/problems/' + pid);
    assert.equal(await editor.inputValue(), source);
    await page.getByRole('button', { name: 'Next', exact: true }).click();
    await page.waitForURL('**submissionPage=2'); await page.getByText('Page 2 of 2', { exact: true }).waitFor();
    assert.equal(historyReads.at(-1), 2);
    await page.locator('.submission-select').click();
    await page.locator('.source-view .hljs-type').first().waitFor();
    assert.equal(new URL(page.url()).pathname, '/problems/' + pid);
    assert.equal(await page.locator('.source-view code').textContent(), source);
    assert.match(await page.locator('#problem-submissions-panel .test-summary').innerText(), /93 \/ 93 tests passed/);
    assert.equal(await editor.getAttribute('data-persistent-editor'), 'original');
    assert.equal(await editor.inputValue(), source);
    await page.goBack(); await page.locator('.submission-select').waitFor();
    await page.goForward(); await page.locator('.source-view .hljs-type').first().waitFor();
    await page.getByRole('button', { name: 'All submissions for this problem', exact: false }).click();
    await page.locator('.submission-select').waitFor();
    for (const theme of ['light', 'dark']) {
      if (theme === 'dark') await page.getByRole('button', { name: 'Switch to dark mode', exact: true }).click();
      for (const width of [1440, 1024, 768, 390, 320]) {
        await page.setViewportSize({ width, height: 900 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `${theme} history overflow at ${width}`);
        await page.screenshot({ path: path.join(out, `history-${theme}-${width}.png`), fullPage: true });
        await page.locator('.submission-select').click(); await page.locator('.source-view .hljs-type').first().waitFor();
        assert.equal(await editor.inputValue(), source);
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `${theme} detail overflow at ${width}`);
        await page.screenshot({ path: path.join(out, `detail-${theme}-${width}.png`), fullPage: true });
        await page.getByRole('button', { name: 'All submissions for this problem', exact: false }).click(); await page.locator('.submission-select').waitFor();
      }
    }
    await page.setViewportSize({ width: 1440, height: 900 });
    await description.click();
    await page.getByRole('button', { name: 'Submit', exact: true }).click();
    await page.getByRole('button', { name: 'Expand editor', exact: true }).click();
    await page.getByRole('link', { name: 'View details', exact: false }).click();
    await page.locator('.source-view .hljs-type').first().waitFor();
    assert.equal(new URL(page.url()).pathname, '/problems/' + pid);
    assert.equal(await editor.getAttribute('data-persistent-editor'), 'original');
    assert.equal(await editor.inputValue(), source);
    assert.equal(await page.locator('.solve-layout--expanded').count(), 0);
    assert.equal(await page.locator('.description-pane').isVisible(), true);
    const duplicateIds = await page.evaluate(() => [...document.querySelectorAll('[id]')].map(element => element.id).filter((id, index, ids) => ids.indexOf(id) !== index));
    assert.deepEqual(duplicateIds, []); assert.deepEqual(errors, []);
    console.log('Problem workspace browser passed: omitted attribution, clear light borders, scoped pagination, inline list/detail, browser history, keyboard, preserved editor, View details and light/dark 320–1440px. API fixtures only.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

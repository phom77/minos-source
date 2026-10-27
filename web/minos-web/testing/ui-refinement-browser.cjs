const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

const pid = '22222222-2222-4222-8222-222222222222', vid = '33333333-3333-4333-8333-333333333333';
const sid = '44444444-4444-4444-8444-444444444444', aid = '55555555-5555-4555-8555-555555555555';
const now = new Date().toISOString(), base = process.env.MINOS_BROWSER_BASE_URL;
const cppSource = '#include <bits/stdc++.h>\nusing namespace std;\n\nclass Solution {\npublic:\n\tvector<int> twoSum(vector<int>& nums, int target) {\n\t\tunordered_map<int, int> seen;\n\t\tfor (int i = 0; i < nums.size(); ++i) {\n\t\t\tint key = target - nums[i];\n\t\t\tif (seen.find(key) != seen.end()) {\n\t\t\t\treturn {seen[key], i};\n\t\t\t}\n\t\t\tseen[nums[i]] = i;\n\t\t}\n\t\treturn {};\n\t}\n};\n// tiếng Việt — <img src=x onerror=alert(1)>\n';
const problem = { problemId: pid, versionId: vid, versionNumber: 17, title: 'Pairwise sums', publishedAtUtc: now,
  publicNumber: 42, classification: { difficulty: 'Medium', topics: [{ slug: 'math', displayName: 'Math' }], needsReassessment: false }, solveStatus: 'NotAttempted',
  statement: 'Compute the **XOR** of the sums.\n\n$$$ (a_1 + a_2) ⊕ (a_1 + a_n) \\\\\\ ⊕ (a_{n-1} + a_n) $$$\n\nInput\n\nRead a_1 through a_n, with values up to 10^7.\n\nOutput\n\nPrint the answer.\n\n' +
    ('A long statement must remain inside its own scrolling panel. See <https://example.com/problem>.\n\n').repeat(20),
  timeLimitMilliseconds: 3000, memoryLimitMegabytes: 488, checker: 'Exact', privateTestCount: 3,
  samples: [{ input: '  3\n1 2 3\n', output: '2\n' }, { input: '2\n1 2\n', output: '3\n' }] };
const result = { id: sid, problemId: pid, problemVersionId: vid, language: 'cpp20', toolchainId: 'fixture', source: cppSource,
  status: 'Completed', createdAtUtc: now, completedAtUtc: now, attempts: [],
  configuration: { timeLimitMilliseconds: 3000, memoryLimitMegabytes: 488, checker: 'Exact', testCount: 3 },
  result: { attemptId: aid, verdict: 'Accepted', compilerDiagnostics: '', completedAtUtc: now,
    maximumCpuTimeMilliseconds: 12, maximumWallTimeMilliseconds: 24, maximumMemoryBytes: 2097152,
    tests: [1, 2, 3].map(order => ({ order, status: 'Passed', cpuTimeMilliseconds: 12, wallTimeMilliseconds: 24, memoryPeakBytes: 2097152 })) } };

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  const out = path.resolve(__dirname, '../../../artifacts/ui-refinement'); await fs.mkdir(out, { recursive: true });
  try {
    async function pageWithFixtures(authenticated) {
      const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      await page.route('**/accounts/**', route => route.request().url().endsWith('/csrf')
        ? route.fulfill({ json: { requestToken: 'fixture' } })
        : route.fulfill(authenticated ? { json: { id: aid, displayName: 'UI reviewer', role: 'Solver', createdAtUtc: now } }
          : { status: 401, json: {} }));
      await page.route('**/api/**', route => {
        const request = route.request(), url = new URL(request.url());
        if (url.pathname.endsWith('/comments') && request.method() === 'GET') return route.fulfill({ json: { items: [], nextCursor: null } });
        if (url.pathname.endsWith('/submissions') && request.method() === 'POST') return route.fulfill({ status: 201, json: result });
        if (url.pathname === '/api/submissions/' + sid) return route.fulfill({ json: result });
        if (url.pathname === '/api/problems/topics') return route.fulfill({ json: [{ slug: 'math', displayName: 'Math', problemCount: 1 }] });
        if (url.pathname === '/api/problems') return route.fulfill({ json: { page: 1, pageSize: 20, total: 1, items: [problem] } });
        if (url.pathname === '/api/problems/' + pid || url.pathname === '/api/problems/versions/' + vid) return route.fulfill({ json: problem });
        throw new Error('Unexpected UI fixture request: ' + url.pathname);
      });
      return { page, errors };
    }

    const { page, errors } = await pageWithFixtures(true);
    await page.goto(base + '/problems');
    await page.getByRole('link', { name: `${problem.publicNumber}. ${problem.title}`, exact: true }).waitFor();
    assert.equal(await page.locator('thead th').count(), 3);
    assert.doesNotMatch(await page.locator('#problem-library').innerText(), /published|version/i);
    await page.screenshot({ path: path.join(out, 'library-light.png'), fullPage: true });
    await page.getByRole('button', { name: 'Switch to dark mode' }).click();
    await page.screenshot({ path: path.join(out, 'library-dark.png'), fullPage: true });
    await page.getByRole('link', { name: `${problem.publicNumber}. ${problem.title}`, exact: true }).click();
    await page.locator('.katex').first().waitFor();
    assert.equal(await page.locator('.katex-error').count(), 0);
    assert.doesNotMatch(await page.locator('.problem-heading').innerText(), /version|published/i);
    const source = page.getByLabel('C++20 source code'); await source.fill('int main() {}');
    const case1 = page.getByRole('tab', { name: 'Case 1', exact: true });
    await case1.focus(); await page.keyboard.press('End');
    await page.locator('#example-tab-1[aria-selected="true"]').waitFor();
    assert.equal(await page.getByRole('tab', { name: 'Case 2' }).getAttribute('aria-selected'), 'true');
    assert.equal(await page.locator('#example-panel-1 pre').first().textContent(), '2\n1 2\n');
    await page.keyboard.press('Home');
    await page.locator('#example-tab-0[aria-selected="true"]').waitFor();
    assert.equal(await page.locator('#example-panel-0 pre').first().textContent(), '  3\n1 2 3\n');
    await source.focus();
    assert.notEqual(await page.locator('.editor-surface').evaluate(element => getComputedStyle(element).boxShadow), 'none');
    await page.getByRole('button', { name: 'Expand editor', exact: true }).click();
    assert.equal(await source.inputValue(), 'int main() {}');
    await page.getByRole('button', { name: 'Restore split view', exact: true }).click();

    await source.fill(cppSource);
    await page.waitForFunction(expected => document.querySelector('.syntax-layer code')?.textContent === expected, cppSource);
    assert.equal(await page.locator('.syntax-layer img').count(), 0);
    // Native editing, undo and Tab-to-exit must survive the visual overlay.
    await source.focus(); await page.keyboard.press('ControlOrMeta+End'); await page.keyboard.type('x');
    await page.waitForFunction(() => document.querySelector('.syntax-layer code')?.textContent.endsWith('\nx'));
    await page.keyboard.press('ControlOrMeta+z');
    assert.equal(await source.inputValue(), cppSource);
    await page.keyboard.press('Tab');
    assert.equal(await source.evaluate(element => document.activeElement === element), false);
    await source.dispatchEvent('compositionstart');
    await page.waitForFunction(() => getComputedStyle(document.querySelector('#submission-source')).webkitTextFillColor !== 'rgba(0, 0, 0, 0)');
    assert.equal(await page.locator('.highlight-viewport').evaluate(element => getComputedStyle(element).visibility), 'hidden');
    await source.dispatchEvent('compositionend');
    await page.emulateMedia({ forcedColors: 'active' });
    assert.equal(await page.locator('.highlight-viewport').evaluate(element => getComputedStyle(element).display), 'none');
    assert.notEqual(await source.evaluate(element => getComputedStyle(element).webkitTextFillColor), 'rgba(0, 0, 0, 0)');
    await page.emulateMedia({ forcedColors: 'none' });

    // Both axes follow the textarea; the overlay must not change its scroll extent.
    const longSource = cppSource + ('// ' + 'long '.repeat(150) + '\n').repeat(200);
    await source.fill(longSource);
    await page.waitForFunction(expected => document.querySelector('.syntax-layer code')?.textContent === expected, longSource);
    await source.evaluate(element => { element.scrollTop = 240; element.scrollLeft = 160; element.dispatchEvent(new Event('scroll')); });
    await page.waitForFunction(() => document.querySelector('.syntax-layer').style.transform === 'translate(-160px, -240px)');
    const geometry = await source.evaluate(element => {
      const overlay = document.querySelector('.syntax-layer'), viewport = document.querySelector('.highlight-viewport');
      const styles = getComputedStyle(element), paint = getComputedStyle(overlay);
      return { width: element.clientWidth, height: element.clientHeight, overlayWidth: viewport.clientWidth, overlayHeight: viewport.clientHeight,
        sameFont: styles.font === paint.font, samePadding: styles.padding === paint.padding, sameTabs: styles.tabSize === paint.tabSize };
    });
    assert.equal(geometry.width, geometry.overlayWidth); assert.equal(geometry.height, geometry.overlayHeight);
    assert.ok(geometry.sameFont && geometry.samePadding && geometry.sameTabs, 'highlight/caret geometry differs');
    await source.fill(cppSource);
    await source.evaluate(element => { element.scrollTop = 0; element.scrollLeft = 0; element.dispatchEvent(new Event('scroll')); });

    for (const theme of ['dark', 'light']) {
      if (theme === 'light') await page.getByRole('button', { name: 'Switch to light mode' }).click();
      const colors = await page.locator('.syntax-layer').evaluate(element => {
        const color = selector => getComputedStyle(element.querySelector(selector)).color;
        return [color('.hljs-keyword'), color('.hljs-type'), color('.hljs-number'), color('.hljs-comment')];
      });
      assert.equal(new Set(colors).size, 4, `${theme} syntax colors missing`);
      for (const [width, height] of [[1440, 900], [1920, 1080], [1024, 768], [768, 900], [320, 780]]) {
        await page.setViewportSize({ width, height });
        await page.evaluate(() => scrollTo(0, 0));
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `${theme} horizontal overflow ${width}`);
        if (width >= 1024) {
          assert.ok(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight + 1), `${theme} blank space/document scroll ${width}`);
          await page.locator('.description-content').evaluate(element => { element.scrollTop = element.scrollHeight; });
          assert.ok(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight + 1), `scrolled statement escapes at ${width}`);
          await page.locator('.description-content').evaluate(element => { element.scrollTop = 0; });
        }
        await page.screenshot({ path: path.join(out, `solve-${theme}-${width}.png`), fullPage: true });
      }
    }
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.getByRole('button', { name: 'Submit', exact: true }).click();
    await page.getByText('Accepted', { exact: true }).waitFor();
    assert.match(await page.locator('.test-summary').innerText(), /3 \/ 3 tests passed/);
    assert.equal(await page.locator('#console-result table, #console-result .metrics, .test-evidence').count(), 0);
    assert.doesNotMatch(await page.locator('.result-panel').innerText(), /CPU|wall time|memory|MiB|Limits:|\d+ ms/i);
    await page.screenshot({ path: path.join(out, 'result-light.png'), fullPage: true });
    await page.goto(base + '/submissions/' + sid);
    await page.locator('.submitted-source .hljs-type').first().waitFor();
    assert.equal(await page.locator('.submitted-source code').textContent(), cppSource);
    assert.equal(await page.locator('.submitted-source img, .result-panel table, .result-panel .metrics').count(), 0);
    for (const theme of ['light', 'dark']) {
      if (theme === 'dark') await page.getByRole('button', { name: 'Switch to dark mode' }).click();
      for (const width of [1440, 768, 390, 320]) {
        await page.setViewportSize({ width, height: 900 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `submitted code overflows at ${width}`);
        assert.match(await page.locator('.test-summary').innerText(), /3 \/ 3 tests passed/);
        await page.screenshot({ path: path.join(out, `submission-${theme}-${width}.png`), fullPage: true });
      }
    }
    assert.deepEqual(errors, []); await page.close();

    const guest = await pageWithFixtures(false);
    await guest.page.goto(base + '/problems?auth=register');
    const password = guest.page.getByLabel('Password', { exact: true }); await password.waitFor();
    await password.fill('a-long-password');
    for (const width of [1440, 320]) {
      await guest.page.setViewportSize({ width, height: 900 });
      const emailBox = await guest.page.getByLabel('Email address', { exact: true }).boundingBox();
      const passwordBox = await password.boundingBox();
      const toggleBox = await guest.page.getByRole('button', { name: 'Show passwords', exact: true }).boundingBox();
      assert.ok(Math.abs(emailBox.width - passwordBox.width) <= 1, 'password width differs from email');
      assert.ok(toggleBox.x >= passwordBox.x && toggleBox.x + toggleBox.width <= passwordBox.x + passwordBox.width, 'toggle outside input');
    }
    const toggle = guest.page.getByRole('button', { name: 'Show passwords', exact: true });
    await toggle.focus(); await guest.page.keyboard.press('Enter');
    await guest.page.getByRole('button', { name: 'Hide passwords', exact: true }).waitFor();
    assert.equal(await password.getAttribute('type'), 'text');
    assert.equal(await password.inputValue(), 'a-long-password');
    assert.equal(await guest.page.getByLabel('Confirm password', { exact: true }).getAttribute('type'), 'text');
    await guest.page.getByRole('button', { name: 'Hide passwords', exact: true }).click();
    await guest.page.getByRole('button', { name: 'Show passwords', exact: true }).waitFor();
    await guest.page.screenshot({ path: path.join(out, 'password-mobile.png'), fullPage: true });
    assert.deepEqual(guest.errors, []); await guest.page.close();
    console.log('UI refinement browser passed: safe math, metadata, bounded layout, public cases, summary-only results, safe C++ colors, editing/undo/scroll/Tab, password alignment and light/dark responsive views.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

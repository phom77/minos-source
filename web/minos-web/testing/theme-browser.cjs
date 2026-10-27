// Rendered palette requirements and representative routes. Fixture data never reaches the development API.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const base = process.env.MINOS_BROWSER_BASE_URL;
const pid = '11111111-1111-4111-8111-111111111111', vid = '22222222-2222-4222-8222-222222222222';
const aid = '33333333-3333-4333-8333-333333333333', sid = '44444444-4444-4444-8444-444444444444';
const cid = '55555555-5555-4555-8555-555555555555', rid = '66666666-6666-4666-8666-666666666666';
const now = new Date().toISOString();
const problem = { problemId: pid, versionId: vid, versionNumber: 1, publicNumber: 42, title: 'Tổng hai số — Sum of two numbers', publishedAtUtc: now,
  classification: { difficulty: 'Easy', topics: [{ slug: 'math', displayName: 'Math' }], needsReassessment: false }, solveStatus: 'Solved',
  statement: 'Read two integers and print their **sum**.\n\n## Input\nTwo integers $a$ and $b$.\n\n## Output\nPrint $a + b$.',
  timeLimitMilliseconds: 1000, memoryLimitMegabytes: 64, checker: 'Exact', privateTestCount: 3, samples: [{ input: '1 2\n', output: '3\n' }] };
const summary = { id: sid, problemId: pid, problemVersionId: vid, status: 'Completed', verdict: 'Accepted', createdAtUtc: now, completedAtUtc: now };
const source = '#include <iostream>\nusing namespace std;\n\nint main() {\n    // Đọc đầu vào\n    int a, b;\n    cin >> a >> b;\n    cout << a + b << "\\n";\n    return 0;\n}\n';
const detail = { ...summary, language: 'cpp20', toolchainId: 'fixture', source, attempts: [],
  configuration: { timeLimitMilliseconds: 1000, memoryLimitMegabytes: 64, checker: 'Exact', testCount: 3 },
  result: { attemptId: aid, verdict: 'Accepted', compilerDiagnostics: '', completedAtUtc: now, comparisonEligible: false,
    maximumCpuTimeMilliseconds: 12, maximumWallTimeMilliseconds: 24, maximumMemoryBytes: 2097152,
    tests: [1, 2, 3].map(order => ({ order, status: 'Passed', cpuTimeMilliseconds: 12, wallTimeMilliseconds: 24, memoryPeakBytes: 2097152 })) } };
const contest = { id: cid, title: 'Minos practice', description: 'Luyện tập thuật toán', state: 'Scheduled', startsAtUtc: '2099-10-20T01:00:00Z',
  endsAtUtc: '2099-10-20T03:00:00Z', problemCount: 1, revision: 1, serverTimeUtc: now, canManage: true, canRegister: false,
  canSubmit: false, isRegistered: false, problems: [{ id: pid, problemId: pid, versionId: vid, order: 1, title: problem.title }] };

const rgb = value => value.startsWith('#') ? [1, 3, 5].map(index => parseInt(value.slice(index, index + 2), 16)) : value.match(/[\d.]+/g).slice(0, 3).map(Number);
const luminance = color => rgb(color).map(value => { const s = value / 255; return s <= .04045 ? s / 12.92 : ((s + .055) / 1.055) ** 2.4; })
  .reduce((sum, value, index) => sum + value * [.2126, .7152, .0722][index], 0);
const ratio = (a, b) => (Math.max(luminance(a), luminance(b)) + .05) / (Math.min(luminance(a), luminance(b)) + .05);

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  const out = path.resolve(__dirname, '../../../artifacts/neutral-theme/browser'); await fs.mkdir(out, { recursive: true });
  const report = {};
  try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
    const page = await context.newPage(), errors = [];
    let authenticated = true;
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/accounts/**', route => route.fulfill(route.request().url().endsWith('/csrf') ? { json: { requestToken: 'fixture' } }
      : authenticated ? { json: { id: aid, displayName: 'Theme reviewer', role: 'Setter', createdAtUtc: now } } : { status: 401, json: {} }));
    await page.route('**/api/**', route => {
      const request = route.request(), url = new URL(request.url()), name = url.pathname;
      assert.equal(request.method(), 'GET', 'Palette checks must not mutate fixtures.');
      if (name === '/api/problems/topics') return route.fulfill({ json: [{ slug: 'math', displayName: 'Math', problemCount: 1 }] });
      if (name === '/api/problems') return route.fulfill({ json: { items: [problem], page: 1, pageSize: Number(url.searchParams.get('pageSize')), total: 1, progress: { solved: 1, attempted: 1, total: 1 } } });
      if (name === `/api/problems/${pid}` || name === `/api/problems/versions/${vid}`) return route.fulfill({ json: problem });
      if (name === `/api/problems/${pid}/comments`) return route.fulfill({ json: { nextCursor: null, items: [{ id: rid, problemId: pid, publishedVersionId: vid,
        parentCommentId: null, authorName: 'Theme reviewer', body: 'Can we use `long long` for larger inputs?', isSpoiler: false, state: 'Visible',
        revision: 1, createdAtUtc: now, updatedAtUtc: now, replyCount: 0, canEdit: true, canDelete: true, canHide: false }] } });
      if (name === '/api/me/submissions') return route.fulfill({ json: { items: [summary], page: 1, pageSize: Number(url.searchParams.get('pageSize')), total: 1 } });
      if (name === `/api/submissions/${sid}`) return route.fulfill({ json: detail });
      if (name === '/api/contests' || name === '/api/contests/mine') return route.fulfill({ json: { items: [contest], page: 1, pageSize: 20, total: 1 } });
      if (name === `/api/contests/${cid}`) return route.fulfill({ json: contest });
      throw new Error('Unexpected theme fixture request: ' + name);
    });

    async function capture(name, prepare = async () => {}) {
      await page.evaluate(() => document.activeElement?.blur());
      for (const width of [1440, 390, 320]) {
        await page.setViewportSize({ width, height: 1000 });
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        await prepare();
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `${name} overflows ${width}`);
        if (width !== 320) await page.screenshot({ path: path.join(out, `${name}-${width}.png`), fullPage: true });
      }
      await page.setViewportSize({ width: 1440, height: 1000 });
    }

    for (const theme of ['light', 'dark']) {
      await page.goto(base + '/problems');
      await page.getByRole('link', { name: '42. ' + problem.title, exact: true }).waitFor();
      if (await page.locator('html').getAttribute('data-theme') !== theme) await page.getByRole('button', { name: `Switch to ${theme} mode` }).click();
      await page.waitForFunction(theme => document.documentElement.dataset.theme === theme, theme);
      const colors = await page.evaluate(() => {
        const probe = document.createElement('span'); document.body.append(probe);
        const read = token => { probe.style.color = `var(--${token})`; return getComputedStyle(probe).color; };
        const names = ['canvas', 'surface', 'surface-subtle', 'editor', 'selected', 'text', 'text-muted', 'action', 'primary', 'primary-hover', 'on-primary',
          'focus', 'control-border', 'chrome', 'chrome-raised', 'on-chrome', 'on-chrome-muted', 'chrome-selected', 'on-chrome-selected',
          'success', 'success-surface', 'warning', 'warning-surface', 'danger', 'danger-surface', 'on-danger'];
        const values = Object.fromEntries(names.map(name => [name, read('color-' + name)])); probe.remove(); return values;
      });
      const pairs = [];
      const check = (fg, bg, min) => {
        const measured = ratio(colors[fg], colors[bg]); pairs.push({ foreground: fg, background: bg, ratio: +measured.toFixed(2), minimum: min });
        assert.ok(measured >= min, `${theme} ${fg}/${bg} contrast ${measured.toFixed(2)} < ${min}`);
      };
      for (const surface of ['canvas', 'surface', 'surface-subtle', 'editor', 'selected']) {
        for (const foreground of ['text', 'text-muted', 'action']) check(foreground, surface, 4.5);
        check('focus', surface, 3);
      }
      for (const fill of ['primary', 'primary-hover']) check('on-primary', fill, 4.5);
      for (const status of ['success', 'warning', 'danger']) { check(status, status + '-surface', 4.5); check(status, 'surface', 4.5); }
      check('on-danger', 'danger', 4.5);
      check('on-chrome', 'chrome', 4.5); check('on-chrome-muted', 'chrome', 4.5); check('on-chrome-selected', 'chrome-selected', 4.5);
      check('control-border', 'surface', 3); check('control-border', 'surface-subtle', 3);
      for (const surface of ['canvas', 'surface', 'surface-subtle', 'editor', 'chrome', 'chrome-raised', 'selected', 'chrome-selected']) {
        const channels = rgb(colors[surface]); assert.ok(Math.max(...channels) - Math.min(...channels) <= 8, `${theme} ${surface} is tinted`);
      }
      const meta = await page.locator('meta[name="theme-color"]').getAttribute('content');
      assert.equal(ratio(meta, colors.chrome), 1, 'Browser chrome differs from header');
      report[theme] = { colors, contrast: pairs };
      const placeholder = await page.locator('.search-form input').evaluate(element => getComputedStyle(element, '::placeholder').color);
      assert.ok(ratio(placeholder, colors['surface-subtle']) >= 4.5, `${theme} search placeholder is faint`);
      report[theme].searchPlaceholderContrast = +ratio(placeholder, colors['surface-subtle']).toFixed(2);
      await capture('library-' + theme);
      await page.getByRole('button', { name: 'Filter problems', exact: true }).click();
      await page.locator('.filter-popover').waitFor();
      await capture('filters-' + theme, async () => {
        const panel = page.locator('.filter-popover');
        if (!await panel.isVisible()) await page.getByRole('button', { name: 'Filter problems', exact: true }).click();
        await panel.waitFor();
        const box = await panel.boundingBox(), viewport = page.viewportSize();
        assert.ok(box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width && box.y + box.height <= viewport.height, 'Filter popover escapes viewport');
      });
      await page.keyboard.press('Escape');
      await page.goto(base + `/problems/${pid}`); const editor = page.getByLabel('C++20 source code', { exact: true });
      await editor.fill(source); await page.locator('.syntax-layer .hljs-comment').waitFor();
      const syntax = await page.locator('.syntax-layer code').evaluate(element => Array.from(element.querySelectorAll('[class]')).map(span => ({ text: span.textContent, color: getComputedStyle(span).color })));
      for (const span of syntax) assert.ok(ratio(span.color, colors.editor) >= 4.5, `${theme} syntax ${span.text} is faint`);
      report[theme].syntax = syntax;
      await capture('solve-' + theme);
      await editor.focus(); assert.notEqual(await page.locator('.editor-surface').evaluate(element => getComputedStyle(element).boxShadow), 'none');
      await page.getByRole('button', { name: 'Discussion', exact: true }).click();
      await page.getByText('Can we use', { exact: false }).waitFor();
      await page.getByRole('heading', { name: 'Discussion' }).evaluate(element => element.scrollIntoView({ block: 'start' }));
      await capture('discussion-' + theme);
      await page.goto(base + '/submissions'); await page.locator('.history-table-region').getByText('Accepted', { exact: true }).waitFor(); await capture('history-' + theme);
      await page.goto(base + `/submissions/${sid}`); await page.locator('.submitted-source .hljs-keyword').first().waitFor(); await capture('result-' + theme);
      await page.goto(base + '/contests'); await page.getByRole('link', { name: contest.title, exact: true }).waitFor(); await capture('contests-' + theme);
      await page.goto(base + '/contests/new'); await page.getByLabel('Contest title', { exact: true }).waitFor(); await capture('contest-editor-' + theme);
      for (const section of ['profile', 'security', 'account']) {
        await page.goto(base + '/settings/' + section); await page.locator('.settings-content .panel').waitFor();
        if (section === 'profile') { await page.getByRole('button', { name: 'Edit name', exact: true }).click(); await page.getByLabel('Display name', { exact: true }).waitFor(); }
        await capture('settings-' + section + '-' + theme);
      }
      await page.reload(); assert.equal(await page.locator('html').getAttribute('data-theme'), theme, 'Theme preference lost on reload');
    }
    authenticated = false;
    for (const theme of ['dark', 'light']) {
      await page.goto(base + '/problems?auth=register'); await page.getByRole('dialog').waitFor();
      if (await page.locator('html').getAttribute('data-theme') !== theme) {
        await page.keyboard.press('Escape'); await page.getByRole('button', { name: `Switch to ${theme} mode` }).click();
        await page.goto(base + '/problems?auth=register'); await page.getByRole('dialog').waitFor();
      }
      const password = page.getByLabel('Password', { exact: true }); await password.focus(); await page.keyboard.press('Tab');
      assert.notEqual(await password.getAttribute('aria-invalid'), 'true');
      await capture('register-' + theme);
      await page.getByRole('dialog').getByRole('button', { name: 'Create account', exact: true }).click();
      await page.getByRole('dialog').getByRole('alert').waitFor(); await capture('validation-' + theme);
      await page.keyboard.press('Escape'); await page.goto(base + '/missing-page'); await page.getByRole('heading', { name: "This page isn't here." }).waitFor(); await capture('missing-' + theme);
    }
    assert.deepEqual(errors, []);
    await fs.writeFile(path.join(out, 'contrast.json'), JSON.stringify(report, null, 2));
    await context.close();

    // Check early initialization with Angular intentionally blocked: the first paint and browser chrome must agree.
    for (const theme of ['light', 'dark']) {
      const early = await browser.newPage({ colorScheme: theme });
      await early.route('**/*.js', route => route.request().url().endsWith('/theme-init.js') ? route.continue() : route.abort());
      await early.goto(base, { waitUntil: 'domcontentloaded' });
      assert.equal(await early.locator('html').getAttribute('data-theme'), theme);
      assert.equal(await early.locator('meta[name="theme-color"]').getAttribute('content'), theme === 'dark' ? '#1d1d1d' : '#fafafa');
      await early.close();
    }
    console.log('Neutral theme browser passed: rendered contrast, neutral surfaces, readable syntax, early initialization/persistence, focus, library/filters/solve/comments/submissions/contests/settings/auth/error at 320–1440px in both themes.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

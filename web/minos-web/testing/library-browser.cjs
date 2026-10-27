const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const now = new Date().toISOString(), base = process.env.MINOS_BROWSER_BASE_URL;
const topics = [{ slug: 'math', displayName: 'Math', problemCount: 13 }, { slug: 'greedy', displayName: 'Greedy', problemCount: 12 }];
const rows = Array.from({ length: 25 }, (_, i) => ({
  problemId: `11111111-1111-4111-8111-${String(i + 1).padStart(12, '0')}`,
  versionId: `22222222-2222-4222-8222-${String(i + 1).padStart(12, '0')}`,
  versionNumber: 1, publicNumber: 101 + i, title: `Library problem ${i + 1}`, publishedAtUtc: now,
  classification: { difficulty: ['Easy', 'Medium', 'Hard'][i % 3], topics: [topics[i % 2]], needsReassessment: false },
  solveStatus: i === 0 ? 'Solved' : i === 1 ? 'Attempted' : 'NotAttempted',
  statement: 'Print the supplied integer.', timeLimitMilliseconds: 1000, memoryLimitMegabytes: 64,
  checker: 'Exact', privateTestCount: 1, samples: [{ input: '1\n', output: '1\n' }],
}));

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  const out = path.resolve(__dirname, '../../../artifacts/library-browser'); await fs.mkdir(out, { recursive: true });
  try {
    async function open(authenticated) {
      const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
      const errors = []; page.on('pageerror', error => errors.push(error.message));
      let failed = false;
      const account = { id: '33333333-3333-4333-8333-333333333333', displayName: 'Library solver', role: 'Solver', createdAtUtc: now };
      await page.route('**/accounts/**', route => {
        const pathname = new URL(route.request().url()).pathname;
        if (pathname.endsWith('/csrf')) return route.fulfill({ json: { requestToken: 'fixture' } });
        if (pathname.endsWith('/login')) { authenticated = true; return route.fulfill({ json: account }); }
        return route.fulfill(authenticated ? { json: account } : { status: 401, json: {} });
      });
      await page.route('**/api/**', route => {
        const url = new URL(route.request().url());
        if (url.pathname.endsWith('/comments') && route.request().method() === 'GET') return route.fulfill({ json: { items: [], nextCursor: null } });
        if (url.pathname === '/api/problems/topics') return route.fulfill({ json: topics });
        if (url.pathname === '/api/problems') {
          const q = url.searchParams.get('q') || '', difficulty = url.searchParams.get('difficulty'), topic = url.searchParams.get('topic');
          const status = url.searchParams.get('status'), sort = url.searchParams.get('sort');
          if (q === 'fail' && !failed) { failed = true; return route.fulfill({ status: 503, json: { detail: 'private diagnostic sentinel' } }); }
          if (status && !authenticated) return route.fulfill({ status: 401, json: {} });
          let filtered = rows.filter(row => {
            const matches = [];
            if (difficulty) matches.push((row.classification.difficulty === difficulty) !== (url.searchParams.get('difficultyIsNot') === 'true'));
            if (topic) matches.push(row.classification.topics.some(t => t.slug === topic) !== (url.searchParams.get('topicIsNot') === 'true'));
            if (status) matches.push((row.solveStatus === status) !== (url.searchParams.get('statusIsNot') === 'true'));
            return (!q || row.title.toLowerCase().includes(q.toLowerCase()) || '#' + row.publicNumber === q) &&
              (!matches.length || (url.searchParams.get('match') === 'Any' ? matches.some(Boolean) : matches.every(Boolean)));
          });
          if (sort === 'DifficultyDescending') filtered.sort((a, b) => ['Hard', 'Medium', 'Easy'].indexOf(a.classification.difficulty) - ['Hard', 'Medium', 'Easy'].indexOf(b.classification.difficulty));
          const pageNumber = Number(url.searchParams.get('page') || 1), size = Number(url.searchParams.get('pageSize') || 20);
          return route.fulfill({ json: { page: pageNumber, pageSize: size, total: filtered.length,
            progress: authenticated ? { solved: 1, attempted: 2, total: rows.length } : null,
            items: filtered.slice((pageNumber - 1) * size, pageNumber * size).map(row => ({ ...row, solveStatus: authenticated ? row.solveStatus : null })) } });
        }
        const problem = rows.find(row => url.pathname === '/api/problems/' + row.problemId);
        if (problem) return route.fulfill({ json: problem });
        throw new Error('Unexpected library fixture request: ' + url.pathname);
      });
      return { page, errors };
    }
    const { page, errors } = await open(true);
    const link = (p, index) => p.getByRole('link', { name: `${100 + index}. Library problem ${index}`, exact: true });
    await page.goto(base + '/problems?pageSize=10');
    await link(page, 1).waitFor();
    assert.equal(await page.locator('tbody tr').count(), 10);
    assert.equal(await page.locator('.problem-number').first().innerText(), '101.');
    assert.match(await page.locator('.library-progress').innerText(), /1 \/ 25 Solved/);
    assert.equal(await page.locator('.problem-topics').count(), 0);
    assert.match(await page.locator('.quick-topic').first().innerText(), /Math.*13/s);
    await page.getByLabel('Show topics', { exact: true }).check();
    await page.locator('.topic-chip').first().waitFor();
    await page.getByRole('searchbox', { name: 'Search', exact: true }).fill('#115');
    await page.getByRole('searchbox', { name: 'Search', exact: true }).press('Enter');
    await link(page, 15).waitFor();
    const filterButton = page.getByRole('button', { name: 'Filter problems', exact: true });
    await filterButton.click();
    const panel = page.getByRole('dialog', { name: 'Filter problems', exact: true });
    await panel.waitFor();
    assert.equal(await panel.locator('.filter-row').count(), 3);
    await panel.getByLabel('Difficulty', { exact: true }).selectOption('Hard');
    await page.waitForURL(url => url.searchParams.get('difficulty') === 'Hard');
    await panel.getByLabel('Topic', { exact: true }).selectOption('math');
    await page.waitForURL(url => url.searchParams.get('topic') === 'math');
    await panel.getByLabel('Difficulty operator', { exact: true }).selectOption('true');
    await page.getByRole('heading', { name: 'No problems match your filters.', exact: true }).waitFor();
    await panel.getByLabel('Match filters', { exact: true }).selectOption('Any');
    await link(page, 15).waitFor();
    assert.equal(await page.locator('tbody tr').count(), 1);
    await panel.getByRole('button', { name: 'Reset filters', exact: true }).click();
    await page.waitForURL(url => !url.searchParams.has('difficulty') && !url.searchParams.has('match'));
    assert.equal(new URL(page.url()).searchParams.get('q'), '#115');
    await panel.getByRole('button', { name: 'Remove Topics filter', exact: true }).click();
    await panel.getByRole('button', { name: 'Add Topics', exact: true }).click();
    await panel.getByLabel('Topic', { exact: true }).waitFor();
    assert.equal(await panel.locator('.filter-row').count(), 3);
    await page.keyboard.press('Escape');
    await panel.waitFor({ state: 'hidden' });
    assert.equal(await filterButton.evaluate(el => el === document.activeElement), true);
    await link(page, 15).click();
    await page.getByRole('heading', { name: 'Library problem 15', exact: true }).waitFor();
    await page.getByRole('link', { name: 'Problem library', exact: true }).click();
    await link(page, 15).waitFor();
    assert.equal(await page.getByRole('searchbox', { name: 'Search', exact: true }).inputValue(), '#115');
    await page.getByRole('button', { name: 'Clear all', exact: true }).click();
    await link(page, 1).waitFor();
    await page.getByRole('link', { name: 'Next page', exact: true }).click();
    await link(page, 11).waitFor();
    assert.equal(await page.locator('.problem-number').first().innerText(), '111.');
    await page.getByRole('button', { name: 'Sort problems', exact: true }).click();
    await page.getByRole('button', { name: 'Difficulty: hard first', exact: true }).click();
    await link(page, 3).waitFor();
    assert.equal(await page.locator('tbody .difficulty-badge').first().innerText(), 'Hard');
    for (const width of [1440, 768, 390, 320]) {
      await page.setViewportSize({ width, height: 1000 });
      await filterButton.click(); await panel.waitFor();
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `library overflows at ${width}`);
      const box = await panel.boundingBox(); assert.ok(box.x >= 0 && box.x + box.width <= width && box.y >= 0 && box.y + box.height <= 1000);
      await page.screenshot({ path: path.join(out, `library-filter-${width}.png`), fullPage: false });
      await panel.getByRole('button', { name: 'Done', exact: true }).click();
      await page.screenshot({ path: path.join(out, `library-${width}.png`), fullPage: true });
    }
    await page.getByRole('button', { name: 'Switch to dark mode' }).click();
    await filterButton.click();
    await page.screenshot({ path: path.join(out, 'library-filter-dark.png'), fullPage: false });
    await page.keyboard.press('Escape');
    await page.getByRole('searchbox', { name: 'Search', exact: true }).fill('no-match');
    await page.getByRole('heading', { name: 'No problems match your filters.', exact: true }).waitFor(); // Debounced input without Enter.
    await page.goBack(); await link(page, 3).waitFor();
    await page.getByRole('searchbox', { name: 'Search', exact: true }).fill('fail');
    await page.getByRole('searchbox', { name: 'Search', exact: true }).press('Enter');
    await page.getByRole('button', { name: 'Retry loading problems', exact: true }).click();
    await page.getByRole('heading', { name: 'No problems match your filters.', exact: true }).waitFor();
    assert.doesNotMatch(await page.locator('body').innerText(), /private diagnostic sentinel/);
    assert.deepEqual(errors, []);
    const guest = await open(false);
    await guest.page.goto(base + '/problems?status=Solved');
    await guest.page.getByRole('heading', { name: 'Sign in to filter your progress.', exact: true }).waitFor();
    assert.equal(await guest.page.locator('progress').count(), 0);
    await guest.page.locator('minos-state-panel').getByRole('button', { name: 'Sign in', exact: true }).click();
    await guest.page.getByLabel('Email address', { exact: true }).fill('fixture@example.test');
    await guest.page.getByLabel('Password', { exact: true }).fill('correct horse battery staple');
    await guest.page.getByRole('dialog').getByRole('button', { name: 'Sign in', exact: true }).click();
    await link(guest.page, 1).waitFor();
    assert.equal(await guest.page.locator('tbody tr').count(), 1);
    assert.equal(new URL(guest.page.url()).searchParams.get('status'), 'Solved');
    assert.match(await guest.page.locator('.library-progress').innerText(), /1 \/ 25 Solved/);
    assert.deepEqual(guest.errors, []);
    console.log('Library browser passed: live search, counted topics, All/Any, exclusions, row add/remove/reset, popover keyboard/focus, sort, safe return, auth and responsive light/dark. API fixtures only.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

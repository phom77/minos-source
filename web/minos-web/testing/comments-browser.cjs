const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const pid = '22222222-2222-4222-8222-222222222222', vid = '33333333-3333-4333-8333-333333333333';
const ownerId = '44444444-4444-4444-8444-444444444444', otherId = '55555555-5555-4555-8555-555555555555';
const rootId = '66666666-6666-4666-8666-666666666666', now = new Date().toISOString();
const base = process.env.MINOS_BROWSER_BASE_URL;
const problem = { problemId: pid, versionId: vid, versionNumber: 1, title: 'Sum of two numbers', publishedAtUtc: now,
  statement: 'Read two integers and print their sum.', timeLimitMilliseconds: 1000, memoryLimitMegabytes: 64,
  checker: 'Exact', privateTestCount: 1, samples: [{ input: '1 2\n', output: '3\n' }] };
const id = index => `${index.toString(16).padStart(8, '0')}-aaaa-4aaa-8aaa-aaaaaaaaaaaa`;
const entry = (index, overrides = {}) => ({ id: id(index), problemId: pid, publishedVersionId: vid, parentCommentId: null,
  author: otherId, authorName: 'Other solver', body: `Question ${index} — thảo luận thuật toán`, isSpoiler: false,
  state: 'Visible', revision: 1, createdAtUtc: new Date(Date.now() - index * 1000).toISOString(), updatedAtUtc: now, ...overrides });

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  const out = path.resolve(__dirname, '../../../artifacts/comments-browser'); await fs.mkdir(out, { recursive: true });
  try {
    let identity = { id: ownerId, displayName: 'Comment reviewer', role: 'Solver', createdAtUtc: now };
    let nextError = 0, failReads = false, writes = 0, reads = 0, sequence = 1000;
    let roots = [entry(0, { id: rootId, author: ownerId, authorName: 'Comment reviewer', isSpoiler: true,
      body: '**Spoiler explanation**\n\n```cpp\n  return a + b;\n```\n\n<script>window.commentXss = true</script>\n\n![tracker](https://tracker.test/pixel) [unsafe](javascript:alert%281%29)' }),
      ...Array.from({ length: 21 }, (_, index) => entry(index + 1))];
    let replies = Array.from({ length: 21 }, (_, index) => entry(index + 100, { parentCommentId: rootId, body: `Reply ${index + 1}` }));
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = [], externalRequests = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => { if (request.url().includes('tracker.test')) externalRequests.push(request.url()); });
    await page.route('**/accounts/**', route => {
      const pathname = new URL(route.request().url()).pathname;
      if (pathname.endsWith('/csrf')) return route.fulfill({ json: { requestToken: 'fixture-csrf' } });
      if (pathname.endsWith('/logout')) { identity = null; return route.fulfill({ status: 204 }); }
      if (pathname.endsWith('/login')) { identity = { id: ownerId, displayName: 'Comment reviewer', role: 'Solver', createdAtUtc: now }; return route.fulfill({ json: identity }); }
      return route.fulfill(identity ? { json: identity } : { status: 401, json: {} });
    });
    const project = value => {
      const { author, ...publicValue } = value, available = value.state === 'Visible';
      return { ...publicValue, body: available ? value.body : null, isSpoiler: available && value.isSpoiler,
        replyCount: value.parentCommentId ? 0 : replies.filter(reply => reply.parentCommentId === value.id).length,
        canEdit: available && identity?.id === author, canDelete: available && identity?.id === author,
        canHide: available && identity?.role === 'Administrator' };
    };
    await page.route('**/api/**', route => {
      const request = route.request(), url = new URL(request.url()), pathname = url.pathname;
      if (pathname === '/api/problems/topics') return route.fulfill({ json: [] });
      if (pathname === '/api/problems') return route.fulfill({ json: { items: [], total: 0, page: 1, pageSize: 20 } });
      if (pathname === '/api/problems/' + pid || pathname === '/api/problems/versions/' + vid) return route.fulfill({ json: problem });
      if (!pathname.startsWith(`/api/problems/${pid}/comments`)) throw new Error('Unexpected comment fixture: ' + pathname);
      if (request.method() === 'GET') {
        reads++;
        if (failReads) return route.fulfill({ status: 503, json: { detail: 'private database diagnostic' } });
        const root = pathname.match(/comments\/([^/]+)\/replies$/)?.[1];
        const values = root ? replies.filter(value => value.parentCommentId === root) : roots;
        const after = url.searchParams.get('after'), start = after ? values.findIndex(value => value.id === after) + 1 : 0;
        const items = values.slice(start, start + 20);
        return route.fulfill({ json: { items: items.map(project), nextCursor: start + 20 < values.length ? items.at(-1).id : null } });
      }
      writes++;
      assert.equal(request.headers()['x-csrf-token'], 'fixture-csrf');
      if (nextError) { const status = nextError; nextError = 0; return route.fulfill({ status, json: { detail: 'private database diagnostic' } }); }
      const body = request.postDataJSON();
      if (pathname.endsWith('/comments')) {
        assert.equal(body.publishedVersionId, vid);
        const value = entry(sequence++, { author: identity.id, authorName: identity.displayName, ...body, id: id(sequence), createdAtUtc: new Date().toISOString() });
        if (body.parentCommentId) replies.unshift(value); else roots.unshift(value);
        return route.fulfill({ status: 201, json: project(value) });
      }
      const targetId = pathname.split('/comments/')[1].split('/')[0], value = [...roots, ...replies].find(value => value.id === targetId);
      assert.equal(body.expectedRevision, value.revision);
      value.revision++;
      if (request.method() === 'PUT') { value.body = body.body; value.isSpoiler = body.isSpoiler; return route.fulfill({ json: project(value) }); }
      value.body = ''; value.isSpoiler = false; value.state = pathname.endsWith('/hide') ? 'Hidden' : 'Deleted';
      return route.fulfill({ status: 204 });
    });
    await page.goto(base + '/problems/' + pid);
    const code = page.getByRole('textbox', { name: 'C++20 source code', exact: true });
    await code.fill('// unsent solution\nint main() {}'); await code.evaluate(element => element.dataset.persistentCode = 'original');
    const comments = page.locator('minos-problem-comments');
    const discussion = comments.getByRole('button', { name: 'Discussion', exact: true });
    await discussion.waitFor(); assert.equal(await discussion.getAttribute('aria-expanded'), 'false'); assert.equal(reads, 0);
    await discussion.scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(out, 'discussion-collapsed.png') });
    await discussion.focus(); await page.keyboard.press('Enter');
    const editor = comments.getByRole('textbox', { name: 'Your comment', exact: true });
    await comments.getByRole('button', { name: 'Show spoiler', exact: true }).waitFor();
    await editor.focus(); await page.keyboard.press('Tab'); assert.equal(await editor.getAttribute('aria-invalid'), null);
    await comments.getByRole('button', { name: 'Insert code block', exact: true }).focus(); await page.keyboard.press('Enter');
    await page.waitForFunction(() => document.activeElement?.id === 'new-comment');
    assert.equal(await editor.inputValue(), '```cpp\ncode\n```\n');
    await editor.fill('Draft survives closing'); const beforeReopen = reads;
    await discussion.click(); await editor.waitFor({ state: 'hidden' });
    await discussion.focus(); await page.keyboard.press('Space'); await editor.waitFor();
    assert.equal(await editor.inputValue(), 'Draft survives closing'); assert.equal(reads, beforeReopen);
    await editor.fill('');
    assert.equal(await comments.getByRole('button', { name: 'Comment', exact: true }).isEnabled(), true);
    await comments.getByRole('button', { name: 'Comment', exact: true }).click();
    await page.waitForFunction(() => document.activeElement?.id === 'new-comment-error');
    assert.equal(await page.evaluate(() => document.activeElement.id), 'new-comment-error');
    assert.equal(writes, 0);
    await editor.fill('Câu hỏi về giới hạn đầu vào');
    await page.waitForFunction(() => document.getElementById('new-comment')?.getAttribute('aria-invalid') === null);
    assert.equal(await editor.getAttribute('aria-invalid'), null);
    nextError = 409; await comments.getByRole('button', { name: 'Comment', exact: true }).click();
    await comments.getByRole('alert').filter({ hasText: 'This comment changed' }).waitFor();
    assert.equal(await editor.inputValue(), 'Câu hỏi về giới hạn đầu vào');
    assert.doesNotMatch(await comments.innerText(), /private database diagnostic/);
    assert.equal(writes, 1);
    await comments.getByRole('button', { name: 'Comment', exact: true }).click();
    await comments.getByRole('status').filter({ hasText: 'Comment posted.' }).waitFor();
    assert.equal(await editor.inputValue(), '');
    const original = comments.locator('#comment-' + rootId);
    assert.doesNotMatch(await original.innerText(), /Spoiler explanation/);
    await original.getByRole('button', { name: 'Show spoiler', exact: true }).focus(); await page.keyboard.press('Enter');
    await original.getByText('Spoiler explanation', { exact: true }).waitFor();
    await page.waitForFunction(rootId => document.activeElement?.id === 'comment-' + rootId, rootId);
    assert.match(await original.innerText(), /Spoiler explanation/);
    assert.equal(await original.locator('pre code').textContent(), '  return a + b;\n');
    assert.equal(await original.locator('script,img,[onerror],a[href^="javascript:"]').count(), 0);
    assert.equal(await page.evaluate(() => window.commentXss), undefined); assert.deepEqual(externalRequests, []);
    await original.getByRole('button', { name: 'Edit', exact: true }).click();
    const edit = original.getByRole('textbox', { name: 'Edit comment', exact: true });
    await edit.fill('Updated question — đã sửa'); await original.getByRole('checkbox', { name: 'Contains spoilers', exact: true }).uncheck();
    await original.getByRole('button', { name: 'Save changes', exact: true }).click();
    await original.getByText('Updated question — đã sửa', { exact: true }).waitFor();
    await original.getByRole('button', { name: 'Reply', exact: true }).click();
    const replyEditor = comments.getByRole('textbox', { name: 'Reply to Comment reviewer', exact: true });
    await replyEditor.fill('Một phản hồi với `code`.'); await comments.getByRole('button', { name: 'Post reply', exact: true }).click();
    await comments.getByRole('status').filter({ hasText: 'Reply posted.' }).waitFor();
    await comments.locator('#replies-' + rootId).getByText('Một phản hồi với', { exact: false }).waitFor();
    assert.equal(await comments.locator('.replies .comment-actions').getByRole('button', { name: 'Reply', exact: true }).count(), 0);
    await comments.getByRole('button', { name: 'More replies', exact: true }).click();
    await comments.getByText('Reply 21', { exact: true }).waitFor();
    const beforeReplies = reads;
    await original.getByRole('button', { name: 'Hide replies (22)', exact: true }).click();
    await comments.getByText('Reply 21', { exact: true }).waitFor({ state: 'hidden' });
    await original.getByRole('button', { name: 'Show replies (22)', exact: true }).click();
    await comments.getByText('Reply 21', { exact: true }).waitFor(); assert.equal(reads, beforeReplies);
    await comments.getByRole('button', { name: 'More comments', exact: true }).click();
    await comments.getByText('Question 21 — thảo luận thuật toán', { exact: true }).waitFor();
    for (const theme of ['light', 'dark']) {
      if (theme === 'dark') await page.getByRole('button', { name: 'Switch to dark mode', exact: true }).click();
      for (const width of [1440, 768, 390, 320]) {
        await page.setViewportSize({ width, height: 1000 }); await comments.getByRole('heading', { name: 'Discussion', exact: true }).evaluate(element => element.scrollIntoView({ block: 'start' }));
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `${theme} comments overflow at ${width}`);
        await page.screenshot({ path: path.join(out, `comments-${theme}-${width}.png`), fullPage: true });
        if (width <= 390) await page.screenshot({ path: path.join(out, `comments-${theme}-${width}-viewport.png`) });
      }
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
    await original.getByRole('button', { name: 'Delete', exact: true }).click();
    const beforeConfirm = writes;
    await original.getByRole('button', { name: 'Cancel', exact: true }).click(); assert.equal(writes, beforeConfirm);
    await original.getByRole('button', { name: 'Delete', exact: true }).click();
    await original.getByRole('button', { name: 'Confirm delete', exact: true }).click();
    await original.getByText('Comment deleted.', { exact: true }).waitFor();
    assert.equal(await original.getByRole('button', { name: 'Edit', exact: true }).count(), 0);
    assert.match(await comments.locator('#replies-' + rootId).innerText(), /Một phản hồi/);
    assert.equal(await code.inputValue(), '// unsent solution\nint main() {}'); assert.equal(await code.getAttribute('data-persistent-code'), 'original');
    failReads = true; await comments.getByRole('button', { name: 'Refresh comments', exact: true }).first().click();
    await comments.getByRole('alert').filter({ hasText: 'Comments could not be loaded.' }).waitFor();
    failReads = false; await comments.getByRole('button', { name: 'Try again', exact: true }).click();
    await comments.getByRole('alert').waitFor({ state: 'detached' });

    // Exercise the supported Settings sign-out journey and return to the public problem.
    await editor.fill('Account-owned unsent comment');
    await page.getByRole('button', { name: 'Account menu: Comment reviewer', exact: true }).click();
    await page.getByRole('link', { name: 'Settings', exact: true }).click();
    await page.getByRole('navigation', { name: 'Settings sections' }).getByRole('link', { name: 'Security', exact: true }).click();
    await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await page.getByRole('dialog').waitFor();
    await page.goto(base + '/problems/' + pid);
    await discussion.click();
    await comments.getByRole('button', { name: 'Sign in to comment', exact: true }).waitFor();
    assert.equal(await comments.locator('textarea').count(), 0);
    assert.equal(await comments.getByRole('button', { name: 'Edit', exact: true }).count(), 0);
    await comments.getByRole('button', { name: 'Sign in to comment', exact: true }).click();
    await page.getByRole('dialog').waitFor(); await page.keyboard.press('Escape');
    assert.equal(new URL(page.url()).pathname, '/problems/' + pid);
    identity = { id: ownerId, displayName: 'Comment reviewer', role: 'Administrator', createdAtUtc: now };
    await page.reload();
    await discussion.click();
    const other = comments.locator('#comment-' + id(1)); await other.getByRole('button', { name: 'Hide', exact: true }).waitFor();
    await other.getByRole('button', { name: 'Hide', exact: true }).click(); await other.getByRole('button', { name: 'Confirm hide', exact: true }).click();
    await other.getByText('Comment hidden by a moderator.', { exact: true }).waitFor();
    assert.equal(await comments.getByRole('textbox', { name: 'Your comment', exact: true }).inputValue(), '');
    assert.deepEqual(errors, []); assert.deepEqual(externalRequests, []);
    console.log('Comments browser journeys passed: keyboard disclosure/lazy reads/draft retention, formatting, reply toggles, validation, create/reply/edit/delete/moderation, cursors, spoiler/XSS, retry, logout, responsive light/dark, persistent code.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

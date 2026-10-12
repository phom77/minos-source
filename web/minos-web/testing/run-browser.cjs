// Serve only the production SPA on a loopback ephemeral port; all API fixtures live in the browser harness.
const http = require('node:http');
const fs = require('node:fs/promises');
const path = require('node:path');
const { spawn } = require('node:child_process');
const root = path.resolve(__dirname, '../dist/minos-web/browser');
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
const server = http.createServer(async (req, res) => {
  try {
    const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    const target = path.resolve(root, '.' + pathname);
    if (target !== root && !target.startsWith(root + path.sep)) { res.writeHead(403).end(); return; }
    let file = target;
    try { if (!(await fs.stat(file)).isFile()) file = path.join(root, 'index.html'); }
    catch { file = path.join(root, 'index.html'); }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
    res.end(await fs.readFile(file));
  } catch { res.writeHead(500).end(); }
});
(async () => {
  await fs.access(path.join(root, 'index.html'));
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  try {
    for (const script of ['contest-browser.cjs', 'submission-browser.cjs']) {
      await new Promise((resolve, reject) => {
        const child = spawn(process.execPath, [path.join(__dirname, script)], { stdio: 'inherit', env: { ...process.env,
          MINOS_BROWSER_BASE_URL: `http://127.0.0.1:${server.address().port}` } });
        child.on('error', reject);
        child.on('exit', code => code === 0 ? resolve() : reject(new Error(`${script} exited ${code}`)));
      });
    }
  } finally { await new Promise(resolve => server.close(resolve)); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });

// Runs before styles/Angular to avoid a theme flash. No account data is stored.
(() => {
  let preference = null;
  try { preference = localStorage.getItem('minos.theme'); } catch { /* Storage may be unavailable. */ }
  const theme = preference === 'light' || preference === 'dark' ? preference
    : window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  document.documentElement.dataset.theme = theme;
  document.documentElement.style.colorScheme = theme;
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme === 'dark' ? '#1d1d1d' : '#fafafa');
  document.querySelector('link[rel="icon"]')?.setAttribute('href', theme === 'dark' ? 'brand/light-minos.png' : 'brand/dark-minos.png');
})();

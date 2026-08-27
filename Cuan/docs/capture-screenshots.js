/**
 * Pengambil tangkapan layar untuk README dan docs/.
 *
 * Menjalankan aplikasi yang sudah hidup di http://localhost:5081, masuk sebagai
 * admin, lalu memotret setiap halaman utama dalam tema terang dan gelap.
 *
 *   dotnet run                       # jendela terminal pertama
 *   node docs/capture-screenshots.js # jendela terminal kedua
 *
 * Pakai --base untuk alamat lain, --only untuk memotret sebagian halaman saja.
 */
const { chromium } = require('playwright');
const path = require('path');
const fs = require('fs');

const args = process.argv.slice(2);
const argOf = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback;
};

const BASE = argOf('--base', 'http://localhost:5081');
const ONLY = argOf('--only', null);
const OUT = path.join(__dirname, 'screenshots');

const PAGES = [
  { slug: 'login', url: '/login', title: 'Halaman masuk', auth: false },
  { slug: 'dashboard', url: '/', title: 'Dasbor' },
  { slug: 'executive', url: '/reports/executive', title: 'Dasbor eksekutif' },
  { slug: 'journal', url: '/transactions/journal', title: 'Jurnal umum' },
  { slug: 'sales', url: '/transactions/sales', title: 'Penjualan' },
  { slug: 'purchases', url: '/transactions/purchases', title: 'Pembelian' },
  { slug: 'cash', url: '/transactions/cash', title: 'Transaksi kas' },
  { slug: 'bank', url: '/transactions/bank', title: 'Transaksi bank' },
  { slug: 'giro', url: '/transactions/giro', title: 'Giro' },
  { slug: 'stock', url: '/transactions/stock', title: 'Mutasi stok' },
  { slug: 'coa', url: '/master/coa', title: 'Daftar akun' },
  { slug: 'items', url: '/master/items', title: 'Master barang' },
  { slug: 'customers', url: '/master/customers', title: 'Pelanggan' },
  { slug: 'tax-faktur', url: '/tax/faktur', title: 'Faktur pajak' },
  { slug: 'tax-spt', url: '/tax/spt', title: 'SPT masa PPN' },
  { slug: 'tax-withholding', url: '/tax/withholding', title: 'Bukti potong PPh' },
  { slug: 'tax-djp', url: '/tax/djp', title: 'Integrasi DJP' },
  { slug: 'ledger', url: '/reports/ledger', title: 'Buku besar' },
  { slug: 'trial-balance', url: '/reports/trial-balance', title: 'Neraca saldo' },
  { slug: 'profit-loss', url: '/reports/profit-loss', title: 'Laba rugi' },
  { slug: 'balance-sheet', url: '/reports/balance-sheet', title: 'Neraca' },
  { slug: 'cash-flow', url: '/reports/cash-flow', title: 'Arus kas' },
  { slug: 'tax-report', url: '/reports/tax', title: 'Laporan pajak' },
  { slug: 'sales-report', url: '/reports/sales', title: 'Laporan penjualan' },
  { slug: 'stock-report', url: '/reports/stock', title: 'Laporan stok' },
  { slug: 'settings', url: '/admin/settings', title: 'Pengaturan' },
  { slug: 'users', url: '/admin/users', title: 'Pengguna & peran' },
  { slug: 'audit', url: '/admin/audit', title: 'Jejak audit' },
];

/** Menunggu Blazor selesai menyambungkan circuit dan render pertama tuntas. */
async function settle(page) {
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(700);
  // Font Google perlu waktu; tanpa ini angka bisa terpotret dengan huruf cadangan.
  await page.evaluate(() => document.fonts && document.fonts.ready).catch(() => {});
  await page.waitForTimeout(250);
}

async function setTheme(page, theme) {
  await page.evaluate((t) => {
    document.documentElement.setAttribute('data-theme', t);
    try { localStorage.setItem('cuan-theme', t); } catch (e) {}
  }, theme);
  await page.waitForTimeout(250);
}

(async () => {
  fs.mkdirSync(OUT, { recursive: true });

  const browser = await chromium.launch();
  const context = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 2,
    locale: 'id-ID',
  });
  const page = await context.newPage();

  // Masuk sekali; cookie-nya dipakai untuk seluruh halaman berikutnya.
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await settle(page);

  const targets = ONLY ? PAGES.filter((p) => p.slug === ONLY) : PAGES;

  // Halaman masuk dipotret dulu, selagi belum ada sesi.
  for (const spec of targets.filter((p) => p.auth === false)) {
    for (const theme of ['light', 'dark']) {
      await setTheme(page, theme);
      const file = path.join(OUT, `${spec.slug}${theme === 'dark' ? '-dark' : ''}.png`);
      await page.screenshot({ path: file });
      console.log('tersimpan', path.basename(file));
    }
  }

  await setTheme(page, 'light');
  await page.fill('input[name="Email"]', 'admin@cuan.id');
  await page.fill('input[name="Password"]', 'Cuan@123');
  await Promise.all([
    page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 20000 }),
    page.click('button[type="submit"]'),
  ]);
  await settle(page);

  let ok = 0;
  let failed = 0;

  for (const spec of targets.filter((p) => p.auth !== false)) {
    try {
      await page.goto(BASE + spec.url, { waitUntil: 'domcontentloaded' });
      await settle(page);

      for (const theme of ['light', 'dark']) {
        await setTheme(page, theme);
        const file = path.join(OUT, `${spec.slug}${theme === 'dark' ? '-dark' : ''}.png`);
        await page.screenshot({ path: file, fullPage: false });
        console.log('tersimpan', path.basename(file));
      }
      ok++;
    } catch (err) {
      console.error('gagal', spec.slug, '-', err.message);
      failed++;
    }
  }

  await browser.close();
  console.log(`\n${ok} halaman terpotret, ${failed} gagal. Berkas ada di docs/screenshots/`);
  process.exit(failed > 0 ? 1 : 0);
})();

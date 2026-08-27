/**
 * Uji alur impor Excel dari ujung ke ujung.
 *
 * Membuka halaman Barang, mengunduh templatnya, mengisi beberapa baris —
 * termasuk satu baris yang sengaja salah — lalu mengunggahnya dan memastikan
 * pratinjaunya memisahkan baris yang lolos dari yang bermasalah.
 *
 *   dotnet run                  # jendela terminal pertama
 *   node docs/test-import.js    # jendela terminal kedua
 */
const { chromium } = require('playwright');
const path = require('path');
const fs = require('fs');
const os = require('os');

const BASE = process.env.CUAN_BASE || 'http://localhost:5081';
const OUT = fs.mkdtempSync(path.join(os.tmpdir(), 'cuan-import-'));

(async () => {
  const browser = await chromium.launch();
  const context = await browser.newContext({ acceptDownloads: true, locale: 'id-ID' });
  const page = await context.newPage();

  page.on('pageerror', (e) => console.error('galat halaman:', e.message));

  // Masuk
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.fill('input[name="Email"]', 'admin@cuan.id');
  await page.fill('input[name="Password"]', 'Cuan@123');
  await Promise.all([
    page.waitForURL((u) => !u.pathname.startsWith('/login')),
    page.click('button[type="submit"]'),
  ]);

  await page.goto(`${BASE}/master/items`, { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(900);

  const before = await page.locator('table.cu-table tbody tr').count();
  console.log('baris barang sebelum impor:', before);

  // Buka dialog impor
  await page.click('button:has-text("Impor Excel")');
  await page.waitForSelector('.cu-modal:has-text("Barang")');
  console.log('dialog impor terbuka');

  // Tahap awal: tombol unduh template dan pemilih berkas masih terlihat.
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(__dirname, 'screenshots', 'import-template.png') });

  // Daftar kolom yang dibaca, dibuka agar terlihat di potret berikutnya.
  await page.click('summary:has-text("Kolom yang dibaca")');
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(__dirname, 'screenshots', 'import-columns.png') });
  await page.click('summary:has-text("Kolom yang dibaca")');

  // Unduh template
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.click('button:has-text("Template .xlsx")'),
  ]);
  const templatePath = path.join(OUT, 'template.xlsx');
  await download.saveAs(templatePath);
  console.log('template terunduh:', fs.statSync(templatePath).size, 'byte');

  // Isi template: dua baris benar, satu baris salah (kategori tidak dikenal)
  const XLSX = require(path.join(__dirname, 'xlsx-helper.js'));
  const filled = path.join(OUT, 'isi.xlsx');
  await XLSX.fill(templatePath, filled, [
    ['UJI-001', 'Barang Uji Satu', 'Elektronik', 'PCS', '10000', '15000', '5', '2', '50', 'tidak', 'ya', 'dari uji otomatis'],
    ['UJI-002', 'Barang Uji Dua', 'Alat Tulis Kantor', 'BOX', '25000', '32000', '3', '1', '20', 'tidak', 'ya', ''],
    ['UJI-003', 'Barang Uji Tiga', 'KategoriNgawur', 'PCS', '1000', '2000', '0', '0', '0', 'tidak', 'ya', 'harus ditolak'],
  ]);
  console.log('template terisi 3 baris (1 sengaja salah)');

  // Unggah
  await page.setInputFiles('input[type="file"]', filled);
  await page.waitForSelector('text=Impor 2 baris', { timeout: 15000 });

  const inserts = await page.locator('.cu-badge:has-text("tambah")').count();
  const skips = await page.locator('.cu-badge:has-text("dilewati")').count();
  console.log(`pratinjau: ${inserts} tambah, ${skips} dilewati`);

  await page.screenshot({ path: path.join(__dirname, 'screenshots', 'import-preview.png') });

  // Impor
  await page.click('button:has-text("Impor 2 baris")');
  await page.waitForSelector('text=Impor selesai', { timeout: 20000 });
  const summary = await page.locator('.cu-alert-success').innerText();
  console.log('hasil:', summary.replace(/\s+/g, ' ').trim());

  await page.screenshot({ path: path.join(__dirname, 'screenshots', 'import-done.png') });

  await page.click('button:has-text("Selesai")');
  await page.waitForTimeout(1200);

  const after = await page.locator('table.cu-table tbody tr').count();
  console.log('baris barang sesudah impor:', after);

  const ok = after > before && inserts === 2 && skips === 1;
  console.log(ok ? '\nLULUS' : '\nGAGAL');

  await browser.close();
  process.exit(ok ? 0 : 1);
})();

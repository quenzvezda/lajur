// Opt-in browser acceptance against an isolated local host and disposable account.
// LAJUR_SMOKE_CREDENTIALS points to {username,password}; never print its contents.
// LAJUR_PLAYWRIGHT_MODULE can select a centrally installed Playwright package.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.LAJUR_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.LAJUR_SMOKE_URL || 'http://127.0.0.1:18080';
assert(['127.0.0.1', 'localhost', '[::1]'].includes(new URL(base).hostname), 'Use an isolated loopback host only.');
const credentials = JSON.parse(fs.readFileSync(process.env.LAJUR_SMOKE_CREDENTIALS, 'utf8'));
const output = process.env.LAJUR_SMOKE_OUTPUT;
assert(output, 'Set LAJUR_SMOKE_OUTPUT to an ignored output directory.');
fs.mkdirSync(output, { recursive: true });

(async () => {
  const browser = await chromium.launch({ headless: true });
  try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, timezoneId: 'Asia/Jakarta' });
    const page = await context.newPage(); const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('dialog', dialog => dialog.accept());
    await page.goto(base + '/masuk');
    await page.locator('[name=Username]').fill(credentials.username); await page.locator('[name=Password]').fill(credentials.password);
    await page.getByRole('button', { name: 'Masuk', exact: true }).click(); await page.waitForURL('**/buku**');
    await page.goto(base + '/buku/2026#okt');
    const marker = 'BatchReport-' + Date.now();
    const editor = () => page.locator('#transaction-editor-10');
    const field = name => editor().locator(`[name=${name}]`);
    const table = () => page.locator('.tx');
    const search = () => page.getByRole('searchbox', { name: 'Cari transaksi', exact: true });
    const visibleRows = () => table().locator('tbody tr[data-transaction-id]');
    const filteredSearch = async text => {
      await search().fill(text);
      await page.waitForFunction(query => [...document.querySelectorAll('.tx tbody tr[data-transaction-id]')].every(row => row.querySelector('.desc')?.textContent.includes(query)), text);
      await page.waitForTimeout(150);
    };
    const api = (method, suffix, data) => page.evaluate(async ({ method, suffix, data }) => {
      const token = document.querySelector('input[name=__RequestVerificationToken]').value;
      const response = await fetch('/web/books/2026' + suffix, { method, headers: { 'Content-Type': 'application/json', 'X-Lajur-CSRF': token }, body: data ? JSON.stringify(data) : undefined });
      const body = await response.json().catch(() => null);
      if (!response.ok) throw new Error('Smoke API returned ' + response.status);
      return body;
    }, { method, suffix, data });
    const refresh = async () => { await page.getByRole('button', { name: 'Muat ulang data', exact: true }).click(); await page.waitForTimeout(250); };
    const add = async description => {
      await field('category').fill('Kategori Uji 06'); await field('amountIdr').fill('15000'); await field('description').fill(description);
      await editor().getByRole('button', { name: 'Simpan & lanjut', exact: true }).click();
      await page.waitForFunction(() => document.querySelectorAll('[data-queue-status]').length > 0 && [...document.querySelectorAll('[data-queue-status]')].every(el => el.dataset.queueStatus === 'saved'));
    };
    const defaultDate = await field('date').inputValue();
    await field('date').fill('2026-10-14');
    await editor().getByRole('button', { name: 'Pertahankan tanggal setelah simpan', exact: true }).click();
    await add(marker + '-locked'); assert.equal(await field('date').inputValue(), '2026-10-14');
    await editor().getByRole('button', { name: 'Pertahankan tanggal setelah simpan', exact: true }).click();
    await field('date').fill('2026-10-15'); await add(marker + '-unlocked'); assert.equal(await field('date').inputValue(), defaultDate);
    const ids = [];
    for (let index = 0; index < 38; index++) {
      const result = await api('POST', '/transactions', { transactionId: crypto.randomUUID(), input: {
        date: `2026-10-${String(index % 24 + 1).padStart(2, '0')}`, type: 'Pengeluaran', paymentMethod: index % 4 === 0 ? 'Bank Uji' : '',
        category: 'Kategori Uji 06', amountIdr: String(10000 + index * 100),
        description: marker + '-' + index + ' Café Ω · ' + (index % 5 === 0 ? 'Deskripsi panjang untuk menguji pembungkusan teks pada laporan. '.repeat(4) : 'Transaksi sintetis')
      } }); ids.push(result.transactionId);
    }
    await refresh(); await filteredSearch(marker); assert.equal(await visibleRows().count(), 40);
    assert.equal(await table().getByRole('button', { name: 'Bulan', exact: true }).count(), 0);
    assert.match(await visibleRows().first().innerText(), /\d{2}-Okt-2026/);
    // Only blank payments inside the search result are selected.
    await page.getByRole('button', { name: 'Filter Pembayaran', exact: true }).click();
    const pop = page.getByRole('dialog', { name: 'Filter Pembayaran', exact: true });
    await pop.getByRole('checkbox', { name: /Pilih semua/ }).uncheck();
    await pop.getByRole('checkbox', { name: /\(kosong\)/ }).check();
    await pop.getByRole('button', { name: 'Terapkan', exact: true }).click();
    const blanks = await visibleRows().count(); assert(blanks > 20 && blanks < 40);
    await page.getByRole('checkbox', { name: 'Pilih semua transaksi terfilter', exact: true }).check();
    assert.equal(await table().locator('tbody input:checked').count(), blanks);
    await page.getByRole('button', { name: 'Edit batch', exact: true }).click();
    const dialog = () => page.getByRole('dialog', { name: 'Edit batch transaksi', exact: true });
    await dialog().getByLabel('Pembayaran baru', { exact: true }).selectOption('Bank Uji');
    assert(await dialog().getByRole('button', { name: 'Periksa hasil', exact: true }).isHidden());
    await page.screenshot({ path: path.join(output, 'batch-desktop.png'), fullPage: false });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: path.join(output, 'batch-mobile.png'), fullPage: false });
    assert(!await page.evaluate(() => document.documentElement.scrollWidth > innerWidth));
    await page.setViewportSize({ width: 1440, height: 1000 });
    await dialog().getByRole('button', { name: `Simpan ${blanks} transaksi`, exact: true }).click();
    await dialog().getByText(`Selesai. ${blanks} transaksi tersimpan.`, { exact: true }).waitFor({ timeout: 90000 });
    await dialog().getByRole('button', { name: 'Tutup', exact: true }).click(); assert.equal(await visibleRows().count(), 0);
    await table().getByRole('button', { name: 'Reset', exact: true }).click(); await filteredSearch(marker);
    assert.equal(await visibleRows().count(), 40); assert.equal(await table().locator('tbody input:checked').count(), 0);
    assert(!await visibleRows().filter({ hasText: 'Pembayaran kosong' }).count());
    // A stale row stops after prior successes and leaves subsequent rows unchanged.
    const trio = await visibleRows().evaluateAll(elements => elements.slice(0, 3).map(el => el.dataset.transactionId));
    for (const id of trio) await table().locator(`tr[data-transaction-id="${id}"] input[type=checkbox]`).check();
    await page.getByRole('button', { name: 'Edit batch', exact: true }).click();
    await dialog().getByLabel('Pembayaran baru', { exact: true }).selectOption('');
    const stale = await api('GET', '/transactions/' + trio[1]);
    const changed = { ...stale, description: marker + '-changed elsewhere' }; delete changed.transactionId; delete changed.revision;
    await api('PUT', '/transactions/' + trio[1], { expectedRevision: stale.revision, input: changed });
    await dialog().getByRole('button', { name: 'Simpan 3 transaksi', exact: true }).click();
    await dialog().getByText(/1 tersimpan; 2 belum disimpan/).waitFor();
    assert.equal((await api('GET', '/transactions/' + trio[0])).paymentMethod, '');
    assert.equal((await api('GET', '/transactions/' + trio[1])).paymentMethod, 'Bank Uji');
    assert.equal((await api('GET', '/transactions/' + trio[2])).paymentMethod, 'Bank Uji');
    await dialog().getByRole('button', { name: 'Tutup', exact: true }).click();
    await page.getByRole('button', { name: 'Batal pilih', exact: true }).click();
    // A response lost after acceptance is reconciled before proceeding, without another PUT.
    await filteredSearch(marker + '-locked');
    if (await visibleRows().count() === 0) await filteredSearch(marker + '-unlocked');
    const lostId = await visibleRows().first().getAttribute('data-transaction-id');
    const before = await api('GET', '/transactions/' + lostId);
    await visibleRows().first().locator('input[type=checkbox]').check(); await page.getByRole('button', { name: 'Edit batch', exact: true }).click();
    await dialog().getByLabel('Pembayaran baru', { exact: true }).selectOption('');
    let putCount = 0;
    await page.route(`**/web/books/2026/transactions/${lostId}`, async route => {
      if (route.request().method() === 'PUT') { putCount++; await route.fetch(); await route.abort('failed'); }
      else await route.continue();
    });
    await dialog().getByRole('button', { name: 'Simpan 1 transaksi', exact: true }).click();
    await dialog().getByRole('button', { name: 'Periksa hasil', exact: true }).waitFor();
    assert(await dialog().getByRole('button', { name: 'Tutup', exact: true }).isDisabled());
    await dialog().getByRole('button', { name: 'Periksa hasil', exact: true }).click();
    await dialog().getByText('Selesai. 1 transaksi tersimpan.', { exact: true }).waitFor();
    assert.equal(putCount, 1); assert.equal((await api('GET', '/transactions/' + lostId)).revision, before.revision + 1);
    await page.unroute(`**/web/books/2026/transactions/${lostId}`); await dialog().getByRole('button', { name: 'Tutup', exact: true }).click();
    await filteredSearch(marker);
    // Selection clears when the filter changes; export is still all filtered rows.
    await page.getByRole('checkbox', { name: 'Pilih semua transaksi terfilter', exact: true }).check();
    await filteredSearch(marker + '-1'); assert.equal(await table().locator('tbody input:checked').count(), 0);
    await filteredSearch(marker);
    await visibleRows().first().locator('input[type=checkbox]').check();
    const expected = await visibleRows().evaluateAll(elements => elements.map(el => el.dataset.transactionId));
    fs.writeFileSync(path.join(output, 'export-expected.json'), JSON.stringify({ marker, expected, rows: await Promise.all(expected.map(id => api('GET', '/transactions/' + id))) }));
    for (const format of ['PDF', 'Excel', 'CSV']) {
      const download = page.waitForEvent('download', { timeout: 60000 });
      await page.getByRole('button', { name: 'Ekspor ' + format, exact: true }).click();
      const file = await download; await file.saveAs(path.join(output, 'report.' + ({ PDF: 'pdf', Excel: 'xlsx', CSV: 'csv' })[format]));
    }
    await page.screenshot({ path: path.join(output, 'table-desktop.png'), fullPage: false });
    await page.getByRole('combobox', { name: 'Tema tampilan', exact: true }).selectOption('dark');
    await page.setViewportSize({ width: 390, height: 844 });
    await visibleRows().first().scrollIntoViewIfNeeded();
    await page.screenshot({ path: path.join(output, 'table-mobile-dark.png'), fullPage: false });
    assert(!await page.evaluate(() => document.documentElement.scrollWidth > innerWidth));
    await page.getByRole('button', { name: 'Edit batch', exact: true }).click();
    await page.screenshot({ path: path.join(output, 'batch-mobile-dark.png'), fullPage: false });
    await page.keyboard.press('Escape');
    assert.equal(await dialog().count(), 0);
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.getByRole('combobox', { name: 'Tema tampilan', exact: true }).selectOption('light');
    // Category/date/description share the same reviewed batch boundary, including year view.
    const target = trio[2];
    await page.getByRole('button', { name: 'Batal pilih', exact: true }).click();
    await table().locator(`tr[data-transaction-id="${target}"] input[type=checkbox]`).check();
    await page.getByRole('button', { name: 'Edit batch', exact: true }).click();
    await dialog().getByLabel('Field yang diubah', { exact: true }).selectOption('category');
    const category = await dialog().getByLabel('Kategori baru', { exact: true }).selectOption({ index: 1 });
    await dialog().getByRole('button', { name: 'Simpan 1 transaksi', exact: true }).click();
    await dialog().getByText('Selesai. 1 transaksi tersimpan.', { exact: true }).waitFor();
    assert.equal((await api('GET', '/transactions/' + target)).category, category[0]);
    await dialog().getByRole('button', { name: 'Tutup', exact: true }).click();
    await table().locator(`tr[data-transaction-id="${target}"] input[type=checkbox]`).check();
    await page.getByRole('button', { name: 'Edit batch', exact: true }).click();
    await dialog().getByLabel('Field yang diubah', { exact: true }).selectOption('date');
    await dialog().getByLabel('Tanggal baru', { exact: true }).fill('2026-11-10');
    await dialog().getByRole('button', { name: 'Simpan 1 transaksi', exact: true }).click();
    await dialog().getByText('Selesai. 1 transaksi tersimpan.', { exact: true }).waitFor();
    assert.equal((await api('GET', '/transactions/' + target)).date, '2026-11-10');
    await dialog().getByRole('button', { name: 'Tutup', exact: true }).click();
    await page.getByRole('tab', { name: 'Semua transaksi', exact: true }).click(); await filteredSearch(marker);
    await table().locator(`tr[data-transaction-id="${target}"] input[type=checkbox]`).check();
    await page.getByRole('button', { name: 'Edit batch', exact: true }).click();
    await dialog().getByLabel('Field yang diubah', { exact: true }).selectOption('description');
    const literal = '=SUM(1,2) <script>window.shouldNeverRun = true</script> Café Ω';
    await dialog().getByLabel('Keterangan baru', { exact: true }).fill(literal);
    await dialog().getByRole('button', { name: 'Simpan 1 transaksi', exact: true }).click();
    await dialog().getByText('Selesai. 1 transaksi tersimpan.', { exact: true }).waitFor();
    assert.equal((await api('GET', '/transactions/' + target)).description, literal);
    await dialog().getByRole('button', { name: 'Tutup', exact: true }).click();
    assert.equal(await page.evaluate(() => window.shouldNeverRun), undefined);
    // Export precision and hostile text without adding huge amounts to the ledger.
    for (const format of ['pdf', 'xlsx']) {
      const download = page.waitForEvent('download', { timeout: 60000 });
      await page.evaluate(async ({ format, literal }) => {
        const { transactionReport, exportPdf, exportExcel } = await import('/js/transaction-report.js');
        const rows = [
          { date: '2026-01-01', type: 'Pengeluaran', paymentMethod: '', category: '=1+1', amountIdrText: '9223372036854775807', description: literal },
          { date: '2026-12-31', type: 'Pemasukan', paymentMethod: 'Bank Uji', category: 'Kategori Uji', amountIdrText: '1', description: 'Baris dua\n' + 'Keterangan sintetis '.repeat(23) }
        ];
        const model = transactionReport(rows, { year: 2026, name: 'Uji presisi', sourceUrl: location.href });
        await (format === 'pdf' ? exportPdf(model) : exportExcel(model));
      }, { format, literal });
      const file = await download; await file.saveAs(path.join(output, 'precision.' + format));
    }
    assert.deepEqual(errors, []);
    console.log('WEB_BATCH_EXPORT_SMOKE_OK date-lock/reset blank-payment-filter all-four-batch-fields year-view month-move stale-partial-stop lost-response-reconcile selection-scope PDF-XLSX-CSV exact-large-IDR literal-text desktop/mobile/dark no-page-errors');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });

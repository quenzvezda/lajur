import { reportDate } from './transaction-report.js';

// A batch is an explicit sequence of existing revisioned PUTs, never a blind overwrite.
export function openTransactionBatch({ h, rows, report, request, current, inputFrom, sameInput, setBusy, refresh, render, onSaved }) {
  const entries = rows.map(row => ({ row: { ...row }, input: null, status: 'pending' }));
  let started = false, running = false, unknown = null, blocked = false;
  const labels = { paymentMethod: 'Pembayaran', category: 'Kategori', date: 'Tanggal', description: 'Keterangan' };
  const field = h('select', { id: 'batch-field', class: 'input' }, Object.entries(labels).map(([value, text]) => h('option', { value, text })));
  const valueHost = h('div', { class: 'batch-value' });
  const preview = h('div', { class: 'table-scroll batch-preview', tabindex: '0', 'aria-label': 'Pratinjau perubahan transaksi' });
  const status = h('p', { role: 'status', class: 'editor-message' });
  const submit = h('button', { type: 'submit', class: 'button button--primary', text: `Simpan ${entries.length} transaksi` });
  const inspect = h('button', { type: 'button', class: 'button', text: 'Periksa hasil', hidden: true });
  const closeButton = h('button', { type: 'button', class: 'button button--ghost', text: 'Batal' });
  let valueInput;
  const form = h('form', { onsubmit: event => { event.preventDefault(); save(); } },
    h('div', { class: 'batch-fields' }, h('div', { class: 'editor-field' }, h('label', { for: 'batch-field', text: 'Field yang diubah' }), field), valueHost),
    h('p', { class: 'muted small', text: 'Hanya field ini yang diubah. Tanggal baru dapat memindahkan transaksi ke bulan lain dalam tahun buku ini.' }),
    preview, status, h('div', { class: 'editor-actions' }, submit, inspect, closeButton));
  const dialog = h('dialog', { class: 'transaction-batch', 'aria-labelledby': 'batch-title' },
    h('h2', { id: 'batch-title', text: 'Edit batch transaksi' }),
    h('p', { class: 'muted', text: `${entries.length} transaksi dipilih. Periksa perubahan sebelum menyimpan.` }), form);
  const saved = () => entries.filter(item => item.status === 'saved');
  const display = value => value || '(kosong)';
  function drawPreview() {
    const key = field.value;
    preview.replaceChildren(h('table', { class: 'grid' },
      h('thead', null, h('tr', null, ['Tanggal / kategori', 'Nilai saat ini', 'Nilai baru', 'Status'].map(text => h('th', { scope: 'col', text })))),
      h('tbody', null, entries.map(item => h('tr', null,
        h('td', null, h('strong', { text: reportDate(item.row.date) }), h('div', { text: item.row.category }),
          h('div', { class: 'muted small', text: `Rp ${BigInt(item.row.amountIdrText ?? String(item.row.amountIdr)).toLocaleString('id-ID')} · ${item.row.description || ''}` })),
        h('td', { text: display(key === 'date' ? reportDate(item.row[key]) : item.row[key]) }),
        h('td', { text: display(key === 'date' && valueInput.value ? reportDate(valueInput.value) : valueInput.value.trim()) }),
        h('td', { text: ({ pending: 'Belum disimpan', sending: 'Menyimpan…', saved: 'Tersimpan', uncertain: 'Perlu diperiksa', failed: 'Gagal', conflict: 'Data berubah' })[item.status] }))))));
  }
  function updateValue() {
    const key = field.value;
    valueInput = h(['paymentMethod', 'category'].includes(key) ? 'select' : 'input', {
      id: 'batch-value', class: 'input', type: key === 'date' ? 'date' : 'text', required: ['date', 'category'].includes(key),
      min: key === 'date' ? `${report.year}-01-01` : null, max: key === 'date' ? `${report.year}-12-31` : null,
      maxlength: key === 'description' ? 500 : 100, oninput: drawPreview
    });
    if (key === 'paymentMethod') valueInput.append(...['', ...report.paymentMethods].map(value => h('option', { value, text: value || '(kosong)' })));
    if (key === 'category') {
      const choices = report.categories.filter(category => rows.every(row => row.type === category.kind));
      valueInput.append(h('option', { value: '', text: choices.length ? 'Pilih kategori' : 'Pilih transaksi dengan tipe yang sama' }),
        ...choices.map(category => h('option', { value: category.name, text: category.name })));
    }
    valueHost.replaceChildren(h('label', { for: 'batch-value', text: `${labels[key]} baru` }), valueInput);
    drawPreview();
  }
  field.addEventListener('change', updateValue);
  async function finishRefresh() {
    const count = saved().length;
    onSaved(saved().map(item => item.row.transactionId));
    await refresh(`${count} dari ${entries.length} transaksi batch tersimpan.`);
    render();
  }
  function controls() {
    field.disabled = started; valueInput.disabled = started;
    closeButton.disabled = running || Boolean(unknown);
    closeButton.textContent = started ? 'Tutup' : 'Batal';
    submit.disabled = running || Boolean(unknown) || blocked || saved().length === entries.length;
    submit.textContent = started ? `Simpan ${entries.length - saved().length} transaksi tersisa` : `Simpan ${entries.length} transaksi`;
    inspect.hidden = !unknown; inspect.disabled = running;
    drawPreview();
  }
  async function save() {
    if (running || unknown || blocked || !form.reportValidity()) return;
    if (!started) {
      const key = field.value, value = valueInput.value.trim();
      for (const item of entries) item.input = { ...inputFrom(item.row), [key]: value };
      started = true;
    }
    running = true; setBusy(true); controls();
    try {
      for (const item of entries.filter(entry => entry.status !== 'saved')) {
        item.status = 'sending'; status.textContent = `Menyimpan ${saved().length + 1} dari ${entries.length}…`; controls();
        try {
          await request(`/transactions/${item.row.transactionId}`, { method: 'PUT',
            body: JSON.stringify({ expectedRevision: item.row.revision, input: item.input }) });
          item.status = 'saved';
        } catch (error) {
          if (!error.status || error.status >= 500) {
            unknown = item; item.status = 'uncertain';
            status.textContent = `${saved().length} tersimpan. Hasil transaksi berikutnya belum pasti. Periksa hasil sebelum melanjutkan.`;
          } else {
            blocked = true; item.status = error.status === 409 ? 'conflict' : 'failed';
            status.textContent = `${saved().length} tersimpan; ${entries.length - saved().length} belum disimpan. ${error.status === 409 ? 'Data berubah di server. Tutup, muat ulang data, lalu pilih ulang transaksi untuk meninjau nilai terbaru.' : error.message}`;
          }
          break;
        }
      }
      if (saved().length === entries.length) status.textContent = `Selesai. ${entries.length} transaksi tersimpan.`;
      await finishRefresh();
    } finally { running = false; setBusy(Boolean(unknown)); controls(); }
  }
  inspect.addEventListener('click', async () => {
    if (!unknown || running) return;
    running = true; controls();
    try {
      const server = await current(unknown.row.transactionId);
      if (server && sameInput(server, unknown.input)) {
        unknown.status = 'saved'; unknown = null;
        status.textContent = saved().length === entries.length ? `Selesai. ${entries.length} transaksi tersimpan.` : 'Perubahan sudah tersimpan. Lanjutkan transaksi tersisa dengan tombol Simpan.';
      } else if (server?.revision === unknown.row.revision) {
        unknown.status = 'pending'; unknown = null;
        status.textContent = 'Perubahan belum terlihat di server. Coba simpan transaksi tersisa; pemeriksaan revisi tetap berlaku.';
      } else {
        unknown.status = 'conflict'; unknown = null; blocked = true;
        status.textContent = `${saved().length} tersimpan. Data server berbeda atau sudah dihapus. Tutup dan pilih ulang setelah memeriksa data terbaru.`;
      }
      await finishRefresh();
    } catch (error) { status.textContent = error.message; }
    finally { running = false; setBusy(Boolean(unknown)); controls(); }
  });
  const close = () => {
    if (running || unknown) return;
    dialog.close(); dialog.remove(); setBusy(false); render();
  };
  closeButton.addEventListener('click', close);
  dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
  updateValue(); document.body.append(dialog); dialog.showModal(); field.focus();
}

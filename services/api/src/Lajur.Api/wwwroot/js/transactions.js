import { bindGroupedAmountInput, parseIdrAmount } from './money-input.js';
import { openTransactionBatch } from './transaction-batch.js';

// Browser transaction drafts are kept in this tab's memory. All canonical writes go through the API.
export function createTransactionEditor({ h, getReport, refreshReport, render, notify }) {
  const drafts = new Map();
  let busy = false;
  let uncertainDelete = false;
  const queue = [];
  let sending = false;
  const pendingQueue = () => queue.some(item => item.status !== 'saved');
  const queueHost = h('section', { class: 'transaction-queue', 'aria-label': 'Antrean transaksi', hidden: true });
  const queueToggle = h('summary', { class: 'button queue-toggle', 'aria-controls': 'transaction-queue-details' });
  const queuePanel = h('div', { id: 'transaction-queue-details', class: 'queue-panel', role: 'region', 'aria-label': 'Detail antrean transaksi' });
  const queueDisclosure = h('details', { class: 'queue-disclosure' }, queueToggle, queuePanel);
  queueHost.append(queueDisclosure);
  document.getElementById('app').before(queueHost);
  // Inspect the target before queue actions redraw and detach their clicked buttons.
  document.addEventListener('click', event => { if (!queueHost.contains(event.target)) queueDisclosure.open = false; }, true);
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && queueDisclosure.open) {
      event.preventDefault(); queueDisclosure.open = false;
      if (queueHost.contains(document.activeElement)) queueToggle.focus({ preventScroll: true });
    }
  });
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const exactMoney = value => 'Rp ' + BigInt(value || '0').toLocaleString('id-ID');
  const amountText = row => row.amountIdrText ?? String(row.amountIdr);
  const inputFrom = row => ({ date: row.date, type: row.type, paymentMethod: row.paymentMethod || '',
    category: row.category, amountIdr: amountText(row), description: row.description || '' });
  const newId = () => crypto.randomUUID();
  const groupedAmount = (value, editing = false) => {
    const source = editing && /^[0-9.]+$/.test(value) ? value.replaceAll('.', '') : String(value);
    const normalized = normalizeAmount(source);
    return normalized ? BigInt(normalized).toLocaleString('id-ID') : value;
  };
  const endpoint = () => `/web/books/${getReport().year}`;

  function initial(month, previous) {
    const report = getReport();
    const today = report.today;
    const date = Number(today.slice(5, 7)) === month && Number(today.slice(0, 4)) === report.year
      ? today : `${report.year}-${String(month).padStart(2, '0')}-01`;
    return { id: newId(), revision: null, base: null, input: { date, type: report.transactionTypes.includes('Pengeluaran')
      ? 'Pengeluaran' : report.transactionTypes[0], paymentMethod: '', category: '', amountIdr: '', description: '', ...previous },
      dirty: false, retain: { date: false, paymentMethod: false, category: false }, conflict: null, uncertain: null, message: '' };
  }
  function state(month) {
    if (!drafts.has(month)) drafts.set(month, initial(month));
    return drafts.get(month);
  }
  function normalizeAmount(text) {
    return parseIdrAmount(text)?.toString() ?? null;
  }
  function sameInput(row, input) {
    return row.date === input.date && row.type === input.type && row.paymentMethod === input.paymentMethod
      && row.category === input.category && String(row.amountIdr) === input.amountIdr
      && (row.description || '') === input.description;
  }
  async function request(path, options = {}) {
    const response = await fetch(endpoint() + path, { credentials: 'same-origin', cache: 'no-store',
      ...options, headers: { 'Content-Type': 'application/json', 'X-Lajur-CSRF': token, ...options.headers } });
    const body = response.status === 204 ? null : await response.json().catch(() => null);
    if (!response.ok) {
      const error = new Error(response.status === 401 ? 'Sesi berakhir. Masuk kembali di tab lain, lalu periksa hasil sebelum melanjutkan.'
        : body?.detail || (response.status === 400 ? 'Periksa isian. Jika halaman sudah lama terbuka, muat ulang setelah menyalin draft Anda.'
          : 'Permintaan tidak berhasil. Draft tetap tersedia.'));
      error.status = response.status;
      error.code = body?.code;
      error.fields = body?.errors;
      throw error;
    }
    return body;
  }
  async function current(id) {
    try { return await request(`/transactions/${id}`); }
    catch (error) { if (error.status === 404) return null; throw error; }
  }
  async function refresh(message, row) {
    try { await refreshReport(); notify(message, row); }
    catch { notify(`${message} Ringkasan belum diperbarui; gunakan Muat ulang data.`, row); }
  }
  function completed(month, saved) {
    const old = state(month);
    const retain = { ...old.retain };
    const next = initial(month, { date: retain.date ? old.input.date : initial(month).input.date, type: old.input.type,
      paymentMethod: retain.paymentMethod ? old.input.paymentMethod : '', category: retain.category ? old.input.category : '' });
    // A moved transaction must not leave the next new draft in another month's sheet.
    if (Number(next.input.date.slice(5, 7)) !== month) next.input.date = initial(month).input.date;
    next.retain = retain;
    next.message = '';
    drafts.set(month, next);
  }
  function drawQueue() {
    queueHost.hidden = queue.length === 0;
    if (!queue.length) queueDisclosure.open = false;
    const labels = { waiting: 'Menunggu', sending: 'Mengirim', saved: 'Tersimpan', failed: 'Gagal', uncertain: 'Perlu diperiksa', checking: 'Memeriksa', conflict: 'Data server berbeda' };
    const unfinished = queue.filter(item => item.status !== 'saved').length;
    const attention = queue.filter(item => ['failed', 'uncertain', 'conflict'].includes(item.status)).length;
    const status = attention ? 'Perlu diperiksa' : unfinished ? 'Mengirim' : 'Tersimpan';
    const paths = attention ? ['M12 8v5', 'M12 17h.01', 'M10.3 3.5 2 18a2 2 0 0 0 1.7 3h16.6a2 2 0 0 0 1.7-3L13.7 3.5a2 2 0 0 0-3.4 0Z']
      : unfinished ? ['M20 7v5h-5', 'M4 17v-5h5', 'M6 7a7 7 0 0 1 12-2l2 2', 'M18 17a7 7 0 0 1-12 2l-2-2'] : ['M5 12l4 4L19 6'];
    queueToggle.className = `button queue-toggle ${attention ? 'queue-toggle--attention' : unfinished ? '' : 'queue-toggle--saved'}`;
    queueToggle.setAttribute('aria-label', `Antrean transaksi: ${unfinished} belum selesai, ${queue.length - unfinished} tersimpan${attention ? `, ${attention} perlu diperiksa` : ''}`);
    queueToggle.title = 'Klik untuk melihat detail antrean transaksi';
    queueToggle.replaceChildren(h('svg:svg', { viewBox: '0 0 24 24', width: 16, height: 16, fill: 'none', stroke: 'currentColor', 'stroke-width': 1.8, 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true' }, paths.map(d => h('svg:path', { d }))),
      h('span', { role: 'status', text: `${status} · ${attention || unfinished || queue.length}` }));
    const focused = queuePanel.contains(document.activeElement) ? document.activeElement : null;
    const focusId = focused?.closest('[data-queue-id]')?.dataset.queueId;
    const focusText = focused?.textContent;
    queuePanel.replaceChildren(h('div', { class: 'editor-heading' },
      h('h2', { text: 'Antrean transaksi' }),
      h('button', { type: 'button', class: 'button button--sm button--ghost', text: 'Tutup', onclick: () => { queueDisclosure.open = false; queueToggle.focus(); } })),
      h('div', { class: 'editor-heading' }, h('span', { class: 'muted small', text: `${unfinished} belum selesai · ${queue.length - unfinished} tersimpan` }),
      h('button', { type: 'button', class: 'button button--sm button--ghost', text: 'Bersihkan yang tersimpan',
        disabled: !queue.some(item => item.status === 'saved'), onclick: () => {
          for (let i = queue.length - 1; i >= 0; i--) if (queue[i].status === 'saved') queue.splice(i, 1);
          drawQueue();
        } })),
      h('ol', { class: 'queue-list' }, queue.map(item => h('li', { class: 'queue-item', dataset: { queueId: item.id, queueStatus: item.status } },
        h('div', null, h('strong', { text: `${item.input.date} · ${item.input.category} · ${exactMoney(item.input.amountIdr)}` }),
          h('p', { class: 'muted small', text: `${item.input.type} · ${item.input.paymentMethod || 'Belum diisi'}${item.input.description ? ' · ' + item.input.description : ''}` }),
          item.message ? h('p', { class: 'small', text: item.message }) : null),
        h('span', { class: `queue-status queue-status--${item.status}`, role: 'status', text: labels[item.status] }),
        h('div', { class: 'editor-actions' },
          item.status === 'uncertain' ? h('button', { type: 'button', class: 'button button--sm', text: 'Periksa hasil', onclick: () => inspectQueued(item) }) : null,
          item.status === 'failed' ? [
            h('button', { type: 'button', class: 'button button--sm', text: 'Coba lagi', onclick: () => { item.status = 'waiting'; item.message = ''; drawQueue(); pumpQueue(); } }),
            h('button', { type: 'button', class: 'button button--sm', text: 'Perbaiki', onclick: () => {
              const previous = state(item.month);
              if (busy || previous.uncertain || (previous.dirty && !confirm('Buang draft aktif untuk memperbaiki transaksi dalam antrean?'))) return;
              const next = initial(item.month, item.input); next.id = item.id; next.dirty = true; next.retain = { ...previous.retain };
              queueDisclosure.open = false;
              drafts.set(item.month, next); location.hash = `${['jan','feb','mar','apr','mei','jun','jul','agu','sep','okt','nov','des'][item.month - 1]}`; render();
              document.querySelector(`#transaction-editor-${item.month} [name="amountIdr"]`)?.focus();
            } })] : null,
          ['waiting', 'failed', 'conflict'].includes(item.status) ? h('button', { type: 'button', class: 'button button--sm button--ghost', text: 'Buang dari antrean', onclick: () => {
            if (!['waiting', 'failed', 'conflict'].includes(item.status)) return;
            if (!confirm('Buang draft ini dari antrean? Data yang sudah tersimpan di server tidak dihapus.')) return;
            const index = queue.indexOf(item);
            if (index < 0) return;
            queue.splice(index, 1); drawQueue(); render(); pumpQueue();
          } }) : null)))));
    if (focused) {
      const candidates = focusId ? [...queuePanel.querySelectorAll('[data-queue-id]')].find(row => row.dataset.queueId === focusId)?.querySelectorAll('button') : queuePanel.querySelectorAll('button');
      ([...(candidates || [])].find(button => button.textContent === focusText) || queueToggle).focus({ preventScroll: true });
    }
  }
  async function queuedSaved(item, saved) {
    item.message = 'Diterima server. Memperbarui tabel…'; drawQueue();
    await refresh(`Transaksi tersimpan · ${exactMoney(saved.amountIdr)}.`, saved);
    item.status = 'saved'; item.message = ''; drawQueue();
    render();
  }
  async function pumpQueue() {
    if (sending || busy) return;
    const item = queue.find(candidate => candidate.status !== 'saved');
    if (!item || item.status !== 'waiting') return;
    sending = true; item.status = 'sending'; item.message = ''; drawQueue();
    try {
      const saved = await request('/transactions', { method: 'POST', body: JSON.stringify({ transactionId: item.id, input: item.input }) });
      await queuedSaved(item, saved);
    } catch (error) {
      item.status = !error.status || error.status >= 500 || error.status === 409 ? 'uncertain' : 'failed';
      item.message = item.status === 'uncertain' ? 'Hasil belum pasti. Periksa sebelum mencoba lagi; transaksi berikutnya menunggu.' : error.message;
    } finally { sending = false; drawQueue(); pumpQueue(); }
  }
  async function inspectQueued(item) {
    if (sending || busy || item.status !== 'uncertain') return;
    sending = true; item.status = 'checking'; drawQueue();
    try {
      const server = await current(item.id);
      if (server && sameInput(server, item.input)) await queuedSaved(item, server);
      else if (!server) { item.status = 'failed'; item.message = 'Belum ditemukan di server. Coba lagi memakai ID yang sama atau perbaiki isian.'; }
      else { item.status = 'conflict'; item.message = 'ID ini sudah ada dengan isi berbeda. Draft tidak ditimpa dan tidak dikirim sebagai transaksi baru.'; await refresh('Data server berbeda dari antrean. Tinjau transaksi yang ada.', server); render(); }
    } catch (error) { item.status = 'uncertain'; item.message = error.message; }
    finally { sending = false; drawQueue(); pumpQueue(); }
  }
  function validate(s, month, fields) {
    Object.values(fields).forEach(({ input, error }) => { input.removeAttribute('aria-invalid'); error.textContent = ''; });
    const input = { ...s.input, type: s.input.type.trim(), category: s.input.category.trim(),
      paymentMethod: s.input.paymentMethod.trim(), description: s.input.description.trim(), amountIdr: normalizeAmount(s.input.amountIdr) };
    const report = getReport();
    const errors = {};
    if (!/^\d{4}-\d{2}-\d{2}$/.test(input.date) || Number(input.date.slice(0, 4)) !== report.year) errors.date = 'Pilih tanggal dalam tahun buku ini.';
    else if (!s.base && Number(input.date.slice(5, 7)) !== month) errors.date = 'Transaksi baru harus berada di bulan yang sedang dibuka.';
    if (!report.transactionTypes.includes(input.type)) errors.type = 'Pilih tipe dari Master.';
    if (input.paymentMethod && !report.paymentMethods.includes(input.paymentMethod)) errors.paymentMethod = 'Pilih pembayaran dari Master atau kosongkan.';
    if (!report.categories.some(c => c.name === input.category && c.kind === input.type)) errors.category = 'Pilih kategori yang sesuai tipe.';
    if (!input.amountIdr) errors.amountIdr = 'Masukkan rupiah bulat positif, misalnya 15000 atau 15.000.';
    if (input.description.length > 500) errors.description = 'Maksimal 500 karakter.';
    for (const [key, text] of Object.entries(errors)) { fields[key].input.setAttribute('aria-invalid', 'true'); fields[key].error.textContent = text; }
    if (Object.keys(errors).length) { fields[Object.keys(errors)[0]].input.focus(); return null; }
    return input;
  }
  async function save(month, fields) {
    const s = state(month);
    if (busy || s.conflict || s.uncertain) return;
    const input = validate(s, month, fields);
    if (!input) return;
    if (!s.base) {
      const existing = queue.find(item => item.id === s.id);
      if (existing && existing.status !== 'failed') { s.message = 'Transaksi ini masih dalam antrean. Periksa statusnya terlebih dahulu.'; render(); return; }
      if (!existing && queue.filter(item => item.status !== 'saved').length >= 50) { s.message = 'Antrean mencapai 50 transaksi. Selesaikan antrean sebelum menambah lagi.'; render(); return; }
      if (existing) { existing.input = input; existing.status = 'waiting'; existing.message = ''; }
      else queue.push({ id: s.id, month, input, status: 'waiting', message: '' });
      completed(month); drawQueue(); render();
      document.querySelector(`#transaction-editor-${month} [name="${state(month).input.category ? 'amountIdr' : 'category'}"]`)?.focus();
      pumpQueue(); return;
    }
    if (pendingQueue()) { notify('Selesaikan antrean transaksi baru sebelum menyimpan perubahan.'); return; }
    if (s.base && input.date.slice(5, 7) !== s.base.date.slice(5, 7)
      && !confirm(`Pindahkan transaksi dari ${s.base.date} ke ${input.date}? Baris akan berpindah ke bulan tujuan.`)) return;
    const operation = { input, id: s.id, revision: s.revision, mode: s.base ? 'update' : 'create' };
    busy = true; s.message = 'Menyimpan…'; render();
    try {
      const saved = await request(operation.mode === 'create' ? '/transactions' : `/transactions/${s.id}`, {
        method: operation.mode === 'create' ? 'POST' : 'PUT',
        body: JSON.stringify(operation.mode === 'create' ? { transactionId: s.id, input }
          : { expectedRevision: s.revision, input }) });
      completed(month, saved);
      await refresh(`Transaksi tersimpan · ${exactMoney(saved.amountIdr)}.`, saved);
    } catch (error) {
      s.message = error.message;
      if (error.status === 409 || error.status === 404) {
        try { s.conflict = { server: await current(s.id) }; }
        catch { s.uncertain = operation; s.message += ' Periksa hasil saat koneksi tersedia.'; }
      } else if (!error.status || error.status >= 500) {
        s.uncertain = operation;
        s.message = 'Hasil penyimpanan belum diketahui. Draft ditahan; periksa hasil sebelum mencoba lagi.';
      }
    } finally {
      busy = false; render(); pumpQueue();
      if (!s.conflict && !s.uncertain) document.querySelector(`#transaction-editor-${month} [name="${state(month).input.category ? 'amountIdr' : 'category'}"]`)?.focus({ preventScroll: true });
    }
  }
  async function reconcile(month) {
    const s = state(month), op = s.uncertain;
    if (!op || busy) return;
    busy = true; render();
    try {
      const server = await current(op.id);
      if (server && sameInput(server, op.input)) {
        completed(month, server);
        await refresh('Isi transaksi sudah sesuai dengan data server.', server);
      } else if ((!server && op.mode === 'create') || (server?.revision === op.revision && op.mode === 'update')) {
        s.uncertain = null; s.message = 'Perubahan belum terlihat di server. Coba Simpan lagi; identitas transaksi tetap sama.';
      } else { s.uncertain = null; s.conflict = { server }; s.message = 'Data server berbeda. Tinjau perbandingan sebelum menyimpan.'; }
    } catch (error) { s.message = error.message; }
    finally { busy = false; render(); }
  }
  function edit(row, month, duplicate = false) {
    const previous = state(month);
    if (busy || previous.uncertain || (!duplicate && pendingQueue())) { notify('Selesaikan antrean atau pemeriksaan penyimpanan terlebih dahulu.'); return; }
    if (previous.dirty && !confirm('Buang perubahan draft saat ini untuk membuka transaksi ini?')) return;
    const next = initial(month, inputFrom(row));
    next.base = duplicate ? null : { ...row, amountIdr: amountText(row) };
    next.id = duplicate ? newId() : row.transactionId;
    next.revision = duplicate ? null : row.revision;
    next.dirty = duplicate;
    next.retain = { ...previous.retain };
    next.message = duplicate ? 'Duplikat sebagai draft baru. Periksa tanggal dan nominal sebelum menyimpan.' : 'Belum disimpan · perubahan berlaku pada transaksi ini.';
    drafts.set(month, next); render();
    document.getElementById(`transaction-editor-${month}`)?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    document.querySelector(`#transaction-editor-${month} [name="amountIdr"]`)?.focus({ preventScroll: true });
  }
  function conflictPanel(month, s) {
    const server = s.conflict.server;
    const fields = [['date', 'Tanggal'], ['type', 'Tipe'], ['paymentMethod', 'Pembayaran'], ['category', 'Kategori'], ['amountIdr', 'Nominal'], ['description', 'Keterangan']];
    const cell = (row, key) => !row ? 'Tidak tersedia' : key === 'amountIdr' ? exactMoney(normalizeAmount(String(row[key])) || '0') : row[key] || '—';
    return h('section', { class: 'tx-conflict', 'aria-label': 'Konflik transaksi' },
      h('h3', { text: server ? `Data server berubah (revisi ${server.revision}). Draft Anda tetap tersedia.` : 'Transaksi sudah tidak tersedia di server. Draft Anda tetap tersedia.' }),
      h('div', { class: 'table-scroll' }, h('table', { class: 'grid' },
        h('thead', null, h('tr', null, ['Field', 'Saat mulai edit', 'Server', 'Draft Anda'].map(text => h('th', { scope: 'col', text })))),
        h('tbody', null, fields.map(([key, label]) => h('tr', null, h('th', { scope: 'row', text: label }),
          ...[s.base, server, s.input].map(row => h('td', { text: cell(row, key) }))))))),
      h('div', { class: 'editor-actions' },
        server ? h('button', { type: 'button', class: 'button', onclick: () => {
          if (!confirm('Ganti draft Anda dengan versi server yang ditampilkan?')) return;
          s.input = inputFrom({ ...server, amountIdrText: server.amountIdr }); s.base = { ...server }; s.id = server.transactionId;
          s.revision = server.revision; s.conflict = null; s.dirty = false; s.message = 'Versi server dimuat; belum ada perubahan baru.'; render();
        }, text: 'Gunakan versi server' }) : null,
        server && s.base ? h('button', { type: 'button', class: 'button', onclick: () => {
          if (!confirm('Pertahankan draft Anda untuk mengganti versi server yang ditampilkan? Tinjau isian lalu tekan Simpan perubahan.')) return;
          s.revision = server.revision; s.base = { ...server }; s.conflict = null; s.message = 'Draft dipertahankan. Tinjau lalu Simpan perubahan untuk menerapkannya.'; render();
        }, text: 'Pertahankan draft untuk revisi terbaru' }) : null,
        !server ? h('button', { type: 'button', class: 'button', onclick: () => {
          if (!confirm('Buat draft ini sebagai transaksi baru?')) return;
          s.id = newId(); s.base = null; s.revision = null; s.conflict = null; s.dirty = true; s.message = 'Draft baru; belum disimpan.'; render();
        }, text: 'Jadikan draft transaksi baru' }) : null));
  }
  function editor(month) {
    const s = state(month), report = getReport(), prefix = `transaction-editor-${month}`;
    const fieldset = h('fieldset', { disabled: busy || Boolean(s.uncertain) || Boolean(s.conflict) });
    const fields = {};
    const specs = [['date', 'Tanggal', 'date'], ['type', 'Tipe', 'select'], ['paymentMethod', 'Pembayaran', 'select'],
      ['category', 'Kategori', 'text'], ['amountIdr', 'Nominal (Rp)', 'text'], ['description', 'Keterangan', 'text']];
    for (const [key, label, type] of specs) {
      const id = `${prefix}-${key}`;
      const input = h(type === 'select' ? 'select' : 'input', { id, name: key, class: 'input',
        type: type === 'select' ? null : type, value: key === 'amountIdr' ? groupedAmount(s.input[key]) : s.input[key], required: ['date', 'type', 'category', 'amountIdr'].includes(key),
        maxlength: key === 'description' ? 500 : key === 'amountIdr' ? 32 : 100,
        min: key === 'date' ? `${report.year}-${s.base ? '01' : String(month).padStart(2, '0')}-01` : null,
        max: key === 'date' ? `${report.year}-${s.base ? '12-31' : `${String(month).padStart(2, '0')}-${new Date(report.year, month, 0).getDate()}`}` : null,
        inputmode: key === 'amountIdr' ? 'numeric' : null,
        autocomplete: 'off', 'aria-describedby': `${id}-error` });
      if (type === 'select') {
        const values = key === 'type' ? report.transactionTypes : ['', ...report.paymentMethods];
        input.append(...values.map(value => h('option', { value, text: value || 'Belum diisi' })));
        input.value = s.input[key];
      }
      const error = h('span', { id: `${id}-error`, class: 'field-error' });
      fields[key] = { input, error };
      if (key === 'amountIdr') bindGroupedAmountInput(input);
      input.addEventListener('input', event => {
        s.input[key] = input.value; s.dirty = true;
        input.removeAttribute('aria-invalid'); error.textContent = '';
        if (key === 'type') {
          if (!report.categories.some(c => c.name === s.input.category && c.kind === input.value)) {
            s.input.category = ''; fields.category.input.value = '';
          }
          updateCategories();
        }
        draftStatus.textContent = 'Belum disimpan';
        s.message = '';
        form.querySelector('.editor-message')?.remove();
      });
      const lock = ['date', 'paymentMethod', 'category'].includes(key) ? h('button', { id: `${id}-lock`, type: 'button', class: 'button field-lock',
        'aria-label': `Pertahankan ${label.toLocaleLowerCase('id-ID')} setelah simpan`, 'aria-pressed': String(s.retain[key]),
        onclick: () => { s.retain[key] = !s.retain[key]; updateLock(); } }) : null;
      function updateLock() {
        lock.setAttribute('aria-pressed', String(s.retain[key]));
        lock.title = `${s.retain[key] ? 'Buka kunci' : 'Kunci'} ${label.toLocaleLowerCase('id-ID')}: ${s.retain[key] ? key === 'date' ? 'gunakan tanggal bawaan bulan' : 'kosongkan' : 'pertahankan'} setelah simpan`;
        lock.replaceChildren(h('svg:svg', { viewBox: '0 0 24 24', width: 16, height: 16, fill: 'none', stroke: 'currentColor', 'stroke-width': 1.8, 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true' },
          h('svg:path', { d: 'M5 10h14v11H5Z' }), h('svg:path', { d: s.retain[key] ? 'M8 10V6a4 4 0 0 1 8 0v4' : 'M8 10V6a4 4 0 0 1 8 0' }), h('svg:path', { d: 'M12 14v3' })));
      }
      if (lock) updateLock();
      fieldset.append(h('div', { class: ['editor-field', key === 'category' && 'category-field'] },
        h('div', { class: 'editor-field-heading' }, h('label', { for: id, text: label }), lock), input, error));
    }
    const categoryInput = fields.category.input;
    const categoryList = h('div', { id: `${prefix}-categories`, class: 'category-options', role: 'listbox', 'aria-label': 'Kategori dari Master', hidden: true });
    categoryInput.setAttribute('role', 'combobox'); categoryInput.setAttribute('aria-autocomplete', 'list');
    categoryInput.setAttribute('aria-controls', categoryList.id); categoryInput.setAttribute('aria-expanded', 'false');
    categoryInput.parentElement.append(categoryList);
    let categoryMatches = [], activeCategory = -1;
    const closeCategories = () => { categoryList.hidden = true; categoryInput.setAttribute('aria-expanded', 'false'); categoryInput.removeAttribute('aria-activedescendant'); };
    function updateCategories() {
      const query = categoryInput.value.trim().toLocaleLowerCase('id-ID');
      categoryMatches = report.categories.filter(c => c.kind === s.input.type && c.name.toLocaleLowerCase('id-ID').includes(query));
      activeCategory = -1;
      categoryInput.removeAttribute('aria-activedescendant');
      categoryList.replaceChildren(...categoryMatches.map((category, index) => h('div', {
        id: `${prefix}-category-${index}`, role: 'option', 'aria-selected': 'false', class: 'category-option',
        text: category.name, onmousedown: event => event.preventDefault(), onclick: () => selectCategory(index)
      })));
      if (!categoryMatches.length) categoryList.append(h('p', { class: 'muted small', text: 'Tidak ada kategori yang sesuai di Master.' }));
    }
    const openCategories = () => { updateCategories(); categoryList.hidden = false; categoryInput.setAttribute('aria-expanded', 'true'); };
    function selectCategory(index) {
      categoryInput.value = categoryMatches[index].name;
      categoryInput.dispatchEvent(new Event('input', { bubbles: true }));
      closeCategories(); categoryInput.focus();
    }
    categoryInput.addEventListener('focus', openCategories);
    categoryInput.addEventListener('click', openCategories);
    categoryInput.addEventListener('input', openCategories);
    categoryInput.addEventListener('blur', closeCategories);
    categoryInput.addEventListener('keydown', event => {
      if (event.key === 'Escape') { event.preventDefault(); closeCategories(); }
      else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault(); if (categoryList.hidden) openCategories();
        if (!categoryMatches.length) return;
        activeCategory = activeCategory < 0 ? (event.key === 'ArrowDown' ? 0 : categoryMatches.length - 1)
          : (activeCategory + (event.key === 'ArrowDown' ? 1 : -1) + categoryMatches.length) % categoryMatches.length;
        [...categoryList.children].forEach((option, index) => option.setAttribute('aria-selected', String(index === activeCategory)));
        const option = categoryList.children[activeCategory]; categoryInput.setAttribute('aria-activedescendant', option.id); option.scrollIntoView({ block: 'nearest' });
      } else if (event.key === 'Enter' && !event.ctrlKey && !categoryList.hidden && activeCategory >= 0) {
        event.preventDefault(); selectCategory(activeCategory);
      }
    });
    const draftStatus = h('span', { class: 'muted small', text: busy ? 'Menyimpan…' : s.dirty ? 'Belum disimpan' : s.base ? 'Transaksi dipilih' : 'Draft baru' });
    const form = h('form', { id: prefix, class: 'transaction-editor', 'aria-label': s.base ? 'Edit transaksi' : 'Tambah transaksi',
      onsubmit: event => { event.preventDefault(); save(month, fields); }, onkeydown: event => {
        if (event.ctrlKey && event.key === 'Enter') { event.preventDefault(); form.requestSubmit(); }
      } },
      h('div', { class: 'editor-heading' }, h('h2', { text: s.base ? 'Edit transaksi' : 'Tambah transaksi' }), draftStatus),
      fieldset,
      h('div', { class: 'editor-actions' },
        h('button', { type: 'submit', class: 'button button--primary', disabled: busy || Boolean(s.conflict) || Boolean(s.uncertain), text: s.base ? 'Simpan perubahan' : 'Simpan & lanjut' }),
        h('button', { type: 'button', class: 'button button--ghost', disabled: busy || Boolean(s.uncertain), text: 'Batal', onclick: () => {
          if (s.dirty && !confirm('Buang perubahan draft yang belum disimpan?')) return;
          drafts.set(month, initial(month)); render();
        } })),
      h('p', { class: 'muted small', text: 'Tab pindah field · Ctrl+Enter simpan · Pembayaran boleh kosong' }),
      s.message ? h('p', { class: 'editor-message', role: 'status', text: s.message }) : null,
      s.uncertain ? h('button', { type: 'button', class: 'button', disabled: busy, text: 'Periksa hasil penyimpanan', onclick: () => reconcile(month) }) : null,
      s.conflict ? conflictPanel(month, s) : null);
    return form;
  }
  function remove(row) {
    if (busy || pendingQueue()) return;
    let snapshot = { ...row, amountIdr: amountText(row) };
    let unknown = false;
    const detail = h('p'); const status = h('p', { role: 'status', class: 'editor-message' });
    const cancel = h('button', { type: 'button', class: 'button', text: 'Batal' });
    const submit = h('button', { type: 'button', class: 'button button--danger', text: 'Hapus transaksi' });
    const inspect = h('button', { type: 'button', class: 'button', text: 'Periksa hasil', hidden: true });
    const dialog = h('dialog', { class: 'transaction-delete', 'aria-labelledby': 'delete-title' },
      h('h2', { id: 'delete-title', text: 'Hapus transaksi?' }), detail,
      h('p', { class: 'muted small', text: 'Baris dihapus dari tabel bulan ini. Riwayat perubahan tetap tercatat.' }), status,
      h('div', { class: 'editor-actions' }, cancel, submit, inspect));
    const describe = () => { detail.textContent = `${snapshot.date} · ${snapshot.type} · ${snapshot.category} · ${exactMoney(snapshot.amountIdr)} · ${snapshot.paymentMethod || 'Belum diisi'}${snapshot.description ? ' · ' + snapshot.description : ''}`; };
    const close = () => { if (!busy && !unknown) { dialog.close(); dialog.remove(); } };
    const setPending = value => { busy = value; uncertainDelete = unknown; cancel.disabled = value || unknown; submit.disabled = value || unknown; inspect.disabled = value; };
    const done = async () => {
      unknown = false; uncertainDelete = false; dialog.close(); dialog.remove();
      const s = state(Number(row.date.slice(5, 7)));
      if (s.id === row.transactionId) { s.conflict = { server: null }; s.message = 'Transaksi yang sedang diedit telah dihapus. Draft tetap tersedia.'; }
      await refresh('Transaksi dihapus.');
    };
    submit.addEventListener('click', async () => {
      if (busy || unknown) return;
      setPending(true); status.textContent = 'Menghapus…';
      try { await request(`/transactions/${snapshot.transactionId}?expectedRevision=${snapshot.revision}`, { method: 'DELETE' }); await done(); }
      catch (error) {
        status.textContent = error.message;
        if (error.status === 409 || error.status === 404) {
          try { const server = await current(snapshot.transactionId); if (!server) await done(); else {
            snapshot = server; describe(); status.textContent = 'Transaksi berubah. Nilai terbaru ditampilkan; periksa sebelum menghapus versi ini.';
            submit.textContent = 'Hapus versi terbaru ini';
          } } catch { unknown = true; }
        } else if (!error.status || error.status >= 500) unknown = true;
        if (unknown) { status.textContent = 'Hasil penghapusan belum diketahui. Periksa hasil sebelum melanjutkan.'; inspect.hidden = false; }
      } finally { setPending(false); render(); }
    });
    inspect.addEventListener('click', async () => {
      if (busy) return;
      setPending(true);
      try { const server = await current(snapshot.transactionId); if (!server) await done(); else {
        snapshot = server; unknown = false; describe(); inspect.hidden = true;
        status.textContent = 'Transaksi masih ada di server. Periksa nilai terbaru sebelum mencoba hapus lagi.';
      } } catch (error) { status.textContent = error.message; }
      finally { setPending(false); render(); }
    });
    cancel.addEventListener('click', close);
    dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
    describe(); document.body.append(dialog); dialog.showModal(); cancel.focus();
  }
  function actions(row, month) {
    const editable = Boolean(row.transactionId && row.revision);
    const paths = {
      Edit: ['M12 20h9', 'M16.5 3.5a2.12 2.12 0 0 1 3 3L9 17l-4 1 1-4Z'],
      Duplikat: ['M9 9h11v11H9Z', 'M15 5V3H3v12h2'],
      Hapus: ['M3 6h18', 'M9 6V3h6v3', 'M5 6l1 15h12l1-15', 'M10 10v7', 'M14 10v7']
    };
    const action = (text, callback, disabled = false) => h('button', {
      type: 'button', class: ['button', 'transaction-action', text === 'Hapus' && 'transaction-action--delete'],
      disabled: disabled || busy, title: text, 'aria-label': `${text} transaksi ${row.date} ${row.category}`,
      onclick: event => { event.stopPropagation(); callback(); }
    }, h('svg:svg', { viewBox: '0 0 24 24', width: 18, height: 18, fill: 'none', stroke: 'currentColor',
      'stroke-width': 1.8, 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true', focusable: 'false' },
      paths[text].map(d => h('svg:path', { d }))));
    return h('div', { class: 'transaction-actions' },
      action('Edit', () => edit(row, month), !editable || pendingQueue()), action('Duplikat', () => edit(row, month, true)),
      action('Hapus', () => remove(row), !editable || pendingQueue()),
      !editable ? h('span', { class: 'muted small', text: 'Impor ulang buku untuk mengaktifkan edit baris lama.' }) : null);
  }
  window.addEventListener('beforeunload', event => {
    if (busy || uncertainDelete || pendingQueue() || [...drafts.values()].some(s => s.dirty || s.uncertain)) { event.preventDefault(); event.returnValue = ''; }
  });
  function batchEdit(rows, onSaved) {
    if (busy || pendingQueue()) { notify('Selesaikan antrean atau pemeriksaan penyimpanan terlebih dahulu.'); return; }
    if (!rows.length || rows.some(row => !row.transactionId || !row.revision)) return;
    openTransactionBatch({ h, rows, report: getReport(), request, current, inputFrom, sameInput,
      setBusy: value => { busy = value; }, refresh, render, onSaved });
  }
  return { editor, actions, batchEdit, isBusy: () => busy || sending };
}

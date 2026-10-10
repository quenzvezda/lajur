import { bindGroupedAmountInput, parseIdrAmount } from './money-input.js';
import { PAYMENT_ICONS, paymentIcon } from './payment-icons.js';

export function createMasterEditor({ h, getReport, refreshReport, render, notify }) {
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const drafts = new Map();
  const entries = state => state.entity === 'category' ? state.snapshot.categories : state.snapshot.payments;
  const money = text => text === null ? 'Belum diatur' : 'Rp ' + BigInt(text).toLocaleString('id-ID');
  async function request(path, options = {}) {
    const response = await fetch(`/web/books/${getReport().year}/${path}`, { credentials: 'same-origin', cache: 'no-store', ...options,
      headers: { 'Content-Type': 'application/json', 'X-Lajur-CSRF': token } });
    const body = await response.json().catch(() => null);
    if (!response.ok) { const error = new Error(response.status === 401 ? 'Sesi berakhir. Masuk kembali dan periksa hasil penyimpanan.' : body?.detail || 'Master belum dapat dimuat. Periksa koneksi.'); error.status = response.status; throw error; }
    return body;
  }
  function close(state) {
    if (state.busy) return;
    state.dialog.close(); state.dialog.remove(); state.dialog = null;
  }
  const info = entry => [entry.transactionCount ? `${entry.transactionCount} transaksi` : null, entry.historyCount ? `${entry.historyCount} riwayat` : null,
    entry.dependencyCount ? `${entry.dependencyCount} referensi workbook` : null].filter(Boolean).join(' · ') || 'Belum dipakai';
  function begin(state, entry = null, action = 'create') {
    if (state.form && state.dirty && !confirm('Buang draft Master aktif untuk membuka data lain?')) return;
    state.form = { action, originalName: entry?.name ?? null, name: entry?.name ?? '', kind: entry?.kind ?? (state.entity === 'category' ? 'Pengeluaran' : null),
      budgetIdr: entry?.budgetIdr === null || entry?.budgetIdr === undefined ? '' : BigInt(entry.budgetIdr).toLocaleString('id-ID'), icon: entry?.icon ?? '' };
    state.dirty = false; state.preview = null; state.operation = null; state.message = ''; draw(state);
    state.dialog.querySelector('[name=name]')?.focus();
  }
  function review(state) {
    if (!state.dialog.querySelector('form').reportValidity()) return;
    const f = state.form;
    const budget = f.action === 'delete' || f.budgetIdr.trim() === '' ? null : parseIdrAmount(f.budgetIdr, true)?.toString();
    if (state.entity === 'category' && budget === undefined) { state.message = 'Masukkan budget sebagai rupiah bulat nonnegatif.'; draw(state); return; }
    state.preview = { operationId: crypto.randomUUID(), expectedVersion: state.snapshot.version, entity: state.entity, action: f.action,
      originalName: f.originalName, name: f.action === 'delete' ? null : f.name.trim(), kind: state.entity === 'category' && f.action !== 'delete' ? f.kind : null,
      budgetIdr: state.entity === 'category' && f.action !== 'delete' ? budget : null, icon: state.entity === 'payment' && f.action !== 'delete' ? f.icon || null : null };
    state.operation = null; state.message = ''; draw(state);
  }
  async function accepted(state, snapshot) {
    state.snapshot = snapshot; state.form = null; state.preview = null; state.operation = null;
    state.uncertain = false; state.conflict = false; state.dirty = false; state.message = 'Perubahan Master tersimpan.';
    try { await refreshReport(); render(); notify('Master diperbarui. Pilihan transaksi dan laporan sudah dimuat ulang.'); }
    catch { notify('Master tersimpan. Gunakan Muat ulang data untuk memperbarui pilihan dan laporan.'); }
  }
  async function save(state) {
    if (state.busy || state.uncertain || state.conflict) return;
    state.operation ??= state.preview;
    state.busy = true; state.message = 'Menyimpan…'; draw(state);
    try { await accepted(state, await request('master', { method: 'PUT', body: JSON.stringify(state.operation) })); }
    catch (error) {
      state.message = error.message;
      if (!error.status || error.status >= 500) { state.uncertain = true; state.message = 'Hasil belum pasti. Periksa operasi sebelum mencoba lagi.'; }
      else if (error.status === 409) { state.conflict = true; state.operation = null; }
      else {
        state.preview = null; state.operation = null;
        try { state.snapshot = await request('master'); } catch { /* Keep the draft and error if refresh is unavailable. */ }
      }
    } finally { state.busy = false; draw(state); }
  }
  async function inspect(state) {
    if (state.busy) return;
    state.busy = true; state.message = 'Memeriksa hasil…'; draw(state);
    try { await request(`settings/operations/${state.operation.operationId}`); await accepted(state, await request('master')); }
    catch (error) {
      if (error.status === 404) { state.uncertain = false; state.message = 'Operasi belum ditemukan. Coba lagi dengan ID yang sama.'; }
      else state.message = error.message;
    } finally { state.busy = false; draw(state); }
  }
  async function latest(state) {
    state.busy = true; draw(state);
    try { state.snapshot = await request('master'); state.conflict = false; state.preview = null; state.operation = null; state.message = 'Versi terbaru dimuat. Draft tetap ada; tinjau ulang sebelum menyimpan.'; }
    catch (error) { state.message = error.message; }
    finally { state.busy = false; draw(state); }
  }
  function drawList(state, host) {
    const query = (state.query || '').toLocaleLowerCase('id-ID');
    host.replaceChildren(...entries(state).filter(entry => entry.name.toLocaleLowerCase('id-ID').includes(query)).map(entry =>
      h('div', { class: 'master-entry', dataset: { masterName: entry.name } },
        h('div', { class: 'master-entry-info' }, h('div', { class: 'payment-label' }, state.entity === 'payment' ? paymentIcon(h, entry.icon) : null, h('strong', { text: entry.name })),
          h('p', { class: 'muted small', text: [entry.kind, info(entry)].filter(Boolean).join(' · ') }),
          entry.reason && !entry.transactionCount && !entry.historyCount && !entry.dependencyCount ? h('span', { class: 'muted small', text: entry.reason }) : null),
        h('div', { class: 'editor-actions' },
          h('button', { type: 'button', class: 'button button--sm', text: 'Edit', disabled: state.busy, 'aria-label': `Edit ${entry.name}`, onclick: () => begin(state, entry, 'update') }),
          h('button', { type: 'button', class: 'button button--sm button--danger', text: 'Hapus', disabled: state.busy || !entry.canDelete, title: entry.reason || 'Hapus Master yang belum dipakai', 'aria-label': `Hapus ${entry.name}`, onclick: () => begin(state, entry, 'delete') })))));
    if (!host.children.length) host.append(h('p', { class: 'muted', text: 'Tidak ada data yang cocok.' }));
  }
  function draw(state) {
    if (!state.dialog) return;
    const category = state.entity === 'category';
    const canAdd = category ? state.snapshot.canAddCategory : state.snapshot.canAddPayment;
    const addReason = category ? state.snapshot.categoryReason : state.snapshot.paymentReason;
    const heading = h('header', { class: 'settings-heading' }, h('div', null,
      h('h2', { id: 'master-title', text: category ? 'Kelola kategori' : 'Kelola pembayaran' }),
      h('p', { class: 'muted small', text: `Buku ${getReport().year} · ${entries(state).length}/${category ? state.snapshot.categoryCapacity : state.snapshot.paymentCapacity} ${category ? 'kategori' : 'metode'}` })),
      h('button', { type: 'button', class: 'button button--ghost', 'aria-label': 'Tutup pengelola Master', text: '×', disabled: state.busy, onclick: () => close(state) }));
    const body = h('div', { class: 'settings-content stack' });
    const footer = h('div', { class: 'editor-actions settings-footer' });
    const button = (text, callback, primary = false, danger = false) => h('button', { type: 'button', class: ['button', primary && 'button--primary', danger && 'button--danger'], text, disabled: state.busy, onclick: callback });
    if (state.form) {
      const f = state.form;
      const existing = entries(state).find(e => e.name === f.originalName);
      if (state.conflict) {
        body.append(h('p', { class: 'alert alert--warn', text: 'Master berubah sejak dibuka. Draft ditahan; muat versi terbaru sebelum meninjau ulang.' }));
        body.append(h('p', { text: `Draft: ${f.name}${f.kind ? ' · ' + f.kind : ''}${category ? ' · budget ' + f.budgetIdr : ' · ikon ' + (f.icon || 'otomatis')}` }));
        footer.append(button('Muat Master terbaru', () => latest(state), true));
      } else if (state.preview) {
        const command = state.preview;
        body.append(h('h3', { text: command.action === 'delete' ? 'Hapus Master ini?' : 'Tinjau perubahan' }),
          h('dl', { class: 'facts' },
            h('div', { class: 'fact' }, h('dt', { text: 'Nama' }), h('dd', { text: command.action === 'create' ? command.name : command.action === 'delete' ? f.originalName : `${f.originalName} → ${command.name}` })),
            category && command.action !== 'delete' ? h('div', { class: 'fact' }, h('dt', { text: 'Jenis · budget / bulan' }), h('dd', { text: `${existing ? `${existing.kind} · ${money(existing.budgetIdr)} → ` : ''}${command.kind} · ${money(command.budgetIdr)}` })) : null,
            !category && command.action !== 'delete' ? h('div', { class: 'fact' }, h('dt', { text: 'Ikon' }), h('dd', { class: 'payment-label' }, paymentIcon(h, command.icon || existing?.icon || 'other'), command.icon || 'Otomatis')) : null));
        if (existing) body.append(h('p', { class: 'muted small', text: info(existing) }));
        body.append(h('p', { class: 'muted small', text: 'Berlaku pada buku tahun ini. Transaksi dan riwayat tidak dipindahkan atau dihapus.' }));
        if (state.uncertain) footer.append(button('Periksa hasil', () => inspect(state), true));
        else footer.append(button('Kembali mengedit', () => { state.preview = null; state.operation = null; draw(state); }),
          button(command.action === 'delete' ? 'Hapus Master' : 'Simpan Master', () => save(state), command.action !== 'delete', command.action === 'delete'));
      } else {
        const absent = f.action !== 'create' && !existing;
        if (absent) body.append(h('p', { class: 'alert alert--warn', text: 'Nama asal sudah berubah atau dihapus. Draft tetap ditampilkan; pilih data terbaru dari daftar untuk operasi berikutnya.' }));
        if (f.action === 'delete') {
          body.append(h('form', null, h('h3', { text: f.originalName }), h('p', { class: 'muted small', text: existing ? info(existing) : '' })));
        } else {
          const fieldset = h('fieldset', { class: 'settings-fields master-form-fields', disabled: state.busy });
          const field = (name, label, input) => h('div', { class: 'settings-field' }, h('label', { for: `master-${name}`, text: label }), input);
          const nameInput = h('input', { id: 'master-name', class: 'input', name: 'name', type: 'text', value: f.name, required: true, maxlength: 100,
            readonly: existing && !existing.canRename, oninput: event => { f.name = event.target.value; state.dirty = true; } });
          fieldset.append(field('name', 'Nama', nameInput));
          if (category) {
            const kind = h('select', { id: 'master-kind', name: 'kind', class: 'input', disabled: existing && !existing.canRename }, ['Pengeluaran', 'Pemasukan'].map(value => h('option', { value, text: value })));
            kind.value = f.kind;
            const amount = h('input', { id: 'master-budget', name: 'budgetIdr', class: 'input', type: 'text', inputmode: 'numeric', value: f.budgetIdr, maxlength: 32, placeholder: 'Belum diatur', disabled: f.kind === 'Pemasukan' });
            bindGroupedAmountInput(amount, true);
            amount.addEventListener('input', () => { f.budgetIdr = amount.value; state.dirty = true; amount.setCustomValidity(amount.value.trim() && parseIdrAmount(amount.value, true) === null ? 'Masukkan rupiah bulat nonnegatif.' : ''); });
            kind.addEventListener('change', () => { f.kind = kind.value; state.dirty = true; amount.disabled = kind.value === 'Pemasukan'; if (amount.disabled) { f.budgetIdr = ''; amount.value = ''; amount.setCustomValidity(''); } });
            fieldset.append(field('kind', 'Jenis', kind), field('budget', 'Budget per bulan (Rp)', amount));
          } else {
            const icon = h('select', { id: 'master-icon', name: 'icon', class: 'input' }, h('option', { value: '', text: 'Otomatis dari nama' }), PAYMENT_ICONS.map(([value, text]) => h('option', { value, text })));
            icon.value = f.icon;
            const preview = h('div', { class: 'payment-label', 'aria-label': 'Preview ikon' }, paymentIcon(h, f.icon || existing?.icon || 'other'));
            icon.addEventListener('change', () => { f.icon = icon.value; state.dirty = true; preview.replaceChildren(paymentIcon(h, f.icon || 'other')); });
            fieldset.append(field('icon', 'Ikon pembayaran', icon), preview);
          }
          body.append(h('form', { onsubmit: event => { event.preventDefault(); review(state); } }, fieldset));
          if (existing && !existing.canRename) body.append(h('p', { class: 'muted small', text: `Nama/jenis dikunci. ${existing.reason || 'Master masih dipakai.'} ${category ? 'Budget tetap dapat diedit.' : 'Ikon tetap dapat diedit.'}` }));
          if (existing && !existing.canRename && (f.name !== existing.name || f.kind !== existing.kind)) body.append(button('Gunakan nama/jenis saat ini', () => { f.name = existing.name; f.kind = existing.kind; draw(state); }));
        }
        footer.append(button('Kembali ke daftar', () => { if (state.dirty && !confirm('Buang draft Master ini?')) return; state.form = null; state.dirty = false; state.preview = null; draw(state); }));
        footer.append(h('button', { type: 'button', class: 'button button--primary', text: 'Tinjau perubahan', disabled: state.busy || absent || (f.action === 'delete' && !existing?.canDelete), onclick: () => review(state) }));
      }
    } else {
      const list = h('div', { class: 'master-list' });
      const search = h('input', { class: 'input', type: 'search', placeholder: category ? 'Cari kategori…' : 'Cari metode pembayaran…', 'aria-label': 'Cari Master', value: state.query || '', oninput: event => { state.query = event.target.value; drawList(state, list); } });
      body.append(h('div', { class: 'master-toolbar' }, search, h('button', { type: 'button', class: 'button button--primary', text: category ? 'Tambah kategori' : 'Tambah pembayaran', disabled: state.busy || !canAdd, title: addReason || '', onclick: () => begin(state) })), list);
      if (addReason) body.append(h('p', { class: 'muted small', text: addReason }));
      drawList(state, list);
      footer.append(button('Tutup', () => close(state)));
    }
    body.append(h('p', { class: 'settings-message', role: 'status', text: state.message || '' }));
    state.dialog.replaceChildren(heading, body, footer);
  }
  async function open(entity) {
    let state = drafts.get(entity);
    if (state?.dialog) return;
    if (!state) { state = { entity, dirty: false, busy: false }; drafts.set(entity, state); }
    const dialog = h('dialog', { class: 'settings-dialog master-dialog', 'aria-labelledby': 'master-title' });
    state.dialog = dialog; document.body.append(dialog); dialog.showModal();
    dialog.addEventListener('cancel', event => { event.preventDefault(); close(state); });
    dialog.append(h('h2', { id: 'master-title', text: 'Memuat Master…' }));
    if (state.snapshot && state.form) { draw(state); return; }
    try { const snapshot = await request('master'); if (state.dialog !== dialog) return; state.snapshot = snapshot; draw(state); }
    catch (error) {
      if (state.dialog !== dialog) return;
      dialog.replaceChildren(h('h2', { id: 'master-title', text: 'Master belum tersedia' }), h('p', { text: error.message }),
        h('button', { type: 'button', class: 'button', text: 'Coba lagi', onclick: () => { close(state); open(entity); } }), h('button', { type: 'button', class: 'button', text: 'Tutup', onclick: () => close(state) }));
    }
  }
  window.addEventListener('beforeunload', event => { if ([...drafts.values()].some(s => s.dirty || s.busy || s.uncertain)) { event.preventDefault(); event.returnValue = ''; } });
  return { button: entity => h('button', { type: 'button', class: 'button button--sm', text: entity === 'category' ? 'Kelola kategori' : 'Kelola pembayaran', onclick: () => open(entity) }) };
}

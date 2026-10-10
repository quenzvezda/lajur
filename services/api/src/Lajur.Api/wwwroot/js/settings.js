import { bindGroupedAmountInput, parseIdrAmount } from './money-input.js';

export function createWorkbookSettingsEditor({ h, getReport, refreshReport, render, notify, monthNames }) {
  const drafts = new Map();
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const money = value => value === null ? 'Belum diatur' : 'Rp ' + BigInt(value).toLocaleString('id-ID');
  const grouped = value => value === null ? '' : BigInt(value).toLocaleString('id-ID');
  const keyOf = row => row.category ?? row.id;
  const rowsOf = state => state.snapshot.categories ?? state.snapshot.sources;
  const pathOf = (scope, month) => scope === 'budget' ? 'budget' : `projection/${month}`;
  const valuesOf = row => row.category !== undefined ? { category: row.category, amountIdr: row.amountIdr }
    : { id: row.id, payoutDay: row.payoutDay, estimateIdr: row.isMealAllowance ? null : row.estimateIdr,
      workDays: row.workDays, dailyRateIdr: row.dailyRateIdr };
  const reset = (state, snapshot) => {
    state.snapshot = snapshot;
    state.values = new Map((snapshot.categories ?? snapshot.sources).map(row => [keyOf(row), valuesOf(row)]));
    state.dirty = false; state.operation = null; state.uncertain = false; state.conflict = null; state.preview = null;
  };
  async function request(path, options = {}) {
    const response = await fetch(`/web/books/${getReport().year}/settings/${path}`, {
      credentials: 'same-origin', cache: 'no-store', ...options,
      headers: { 'Content-Type': 'application/json', 'X-Lajur-CSRF': token }
    });
    const body = await response.json().catch(() => null);
    if (!response.ok) {
      const error = new Error(response.status === 401 ? 'Sesi berakhir. Masuk kembali, lalu periksa hasil penyimpanan.' : body?.detail || 'Pengaturan belum dapat dimuat. Periksa koneksi.');
      error.status = response.status; throw error;
    }
    return body;
  }
  function close(state) {
    if (state.busy) return;
    state.dialog.close(); state.dialog.remove(); state.dialog = null;
  }
  function changes(state) {
    return rowsOf(state).filter(row => row.editable).map(row => state.values.get(keyOf(row)))
      .filter(value => JSON.stringify(value) !== JSON.stringify(valuesOf(rowsOf(state).find(row => keyOf(row) === keyOf(value)))));
  }
  function estimate(row, value) {
    return row.isMealAllowance ? value.workDays === null || value.dailyRateIdr === null ? null : (BigInt(value.workDays) * BigInt(value.dailyRateIdr)).toString() : value.estimateIdr;
  }
  function describe(row, value) {
    if (row.category !== undefined) return money(value.amountIdr);
    const amount = row.isMealAllowance ? `${value.workDays ?? '—'} hari × ${money(value.dailyRateIdr)} = ${money(estimate(row, value))}` : money(value.estimateIdr);
    return `${amount} · cair tgl ${value.payoutDay ?? '—'}`;
  }
  function inputField(state, row, name, label, numeric = false) {
    const value = state.values.get(keyOf(row));
    const id = `settings-${state.path.replace('/', '-')}-${keyOf(row)}-${name}`;
    const input = h('input', { id, name, class: 'input', type: 'text', inputmode: 'numeric', autocomplete: 'off', maxlength: numeric ? 2 : 32,
      value: numeric ? value[name] ?? '' : grouped(value[name]), placeholder: 'Belum diatur', disabled: !row.editable });
    if (!numeric) bindGroupedAmountInput(input, true);
    input.addEventListener('input', () => {
      input.setCustomValidity('');
      const raw = input.value.trim();
      const parsed = raw === '' ? null : numeric ? /^[0-9]+$/.test(raw) ? Number(raw) : NaN : parseIdrAmount(raw, true)?.toString();
      const valid = raw === '' || (numeric ? Number.isInteger(parsed) && parsed >= (name === 'payoutDay' ? 1 : 0) && parsed <= 31 : parsed !== undefined);
      if (!valid) input.setCustomValidity(numeric ? 'Masukkan bilangan bulat dalam rentang yang diizinkan.' : 'Masukkan rupiah bulat nonnegatif.');
      if (valid) value[name] = parsed;
      state.dirty = !valid || changes(state).length > 0; state.operation = null; state.preview = null;
      state.message = ''; state.dialog?.querySelector('.settings-message')?.replaceChildren();
      if (row.isMealAllowance) {
        const text = estimate(row, value);
        state.dialog.querySelector(`[data-estimate-id="${row.id}"]`).textContent = money(text);
      }
    });
    return h('div', { class: 'settings-field' }, h('label', { for: id, text: label }), input);
  }
  function review(state) {
    if (!state.dialog.querySelector('form').reportValidity()) return;
    const changed = changes(state);
    if (!changed.length) { state.dirty = false; state.message = 'Belum ada perubahan.'; draw(state); return; }
    state.preview = changed; state.message = ''; draw(state);
  }
  async function accepted(state, snapshot) {
    snapshot ??= await request(state.path);
    reset(state, snapshot); state.message = 'Pengaturan tersimpan.';
    try { await refreshReport(); render(); notify('Pengaturan tersimpan. Ringkasan diperbarui.'); }
    catch { notify('Pengaturan tersimpan. Gunakan Muat ulang data untuk memperbarui ringkasan.'); }
  }
  async function save(state) {
    if (state.busy || state.uncertain || state.conflict) return;
    state.operation ??= { operationId: crypto.randomUUID(), expectedVersion: state.snapshot.version, changes: state.preview };
    state.busy = true; state.message = 'Menyimpan…'; draw(state);
    try {
      const snapshot = await request(state.path, { method: 'PUT', body: JSON.stringify(state.operation) });
      await accepted(state, snapshot);
    } catch (error) {
      if (error.status === 409) {
        state.message = error.message; state.operation = null;
        try { state.conflict = await request(state.path); } catch { state.message += ' Versi terbaru belum dapat dimuat.'; state.conflict = true; }
      } else if (!error.status || error.status >= 500) {
        state.uncertain = true; state.message = 'Hasil penyimpanan belum pasti. Periksa hasil sebelum mencoba lagi.';
      } else { state.message = error.message; state.operation = null; state.preview = null; }
    } finally { state.busy = false; draw(state); }
  }
  async function inspect(state) {
    if (state.busy) return;
    state.busy = true; state.message = 'Memeriksa hasil…'; draw(state);
    try {
      await request(`operations/${state.operation.operationId}`);
      await accepted(state);
    } catch (error) {
      if (error.status === 404) { state.uncertain = false; state.message = 'Operasi belum ditemukan. Coba lagi dengan identitas yang sama.'; }
      else state.message = error.message;
    } finally { state.busy = false; draw(state); }
  }
  async function latest(state, keep) {
    if (state.busy) return;
    state.busy = true; draw(state);
    try {
      const snapshot = await request(state.path);
      if (keep) {
        const modified = changes(state).map(value => {
          const original = rowsOf(state).find(row => keyOf(row) === keyOf(value));
          return { value, original, before: valuesOf(original) };
        });
        const latestRows = snapshot.categories ?? snapshot.sources;
        if (modified.some(({ value, original }) => !latestRows.some(row => row.editable && keyOf(row) === keyOf(value) && row.isMealAllowance === original.isMealAllowance))) {
          state.message = 'Struktur pengaturan berubah. Draft tetap tersedia, tetapi belum bisa diterapkan pada versi terbaru.';
          return;
        }
        reset(state, snapshot);
        for (const { value, before } of modified) {
          const target = state.values.get(keyOf(value));
          for (const field of Object.keys(value)) if (value[field] !== before[field]) target[field] = value[field];
        }
        state.dirty = true; state.message = 'Draft dipertahankan. Tinjau ulang terhadap versi terbaru sebelum menyimpan.';
      } else if (confirm('Buang draft pengaturan dan gunakan versi terbaru?')) reset(state, snapshot);
    } catch (error) { state.message = error.message; }
    finally { state.busy = false; draw(state); }
  }
  async function copyPrevious(state) {
    if (state.busy || state.month <= 1) return;
    state.busy = true; draw(state);
    try {
      const previous = await request(`projection/${state.month - 1}`);
      for (const row of rowsOf(state).filter(row => row.editable)) {
        const source = previous.sources.find(source => source.editable && source.id === row.id && source.label === row.label && source.isMealAllowance === row.isMealAllowance);
        if (source) state.values.set(row.id, valuesOf(source));
      }
      state.dirty = true; state.operation = null; state.message = `Disalin dari ${monthNames[state.month - 2]}. Periksa nilai sebelum menyimpan.`;
    } catch (error) { state.message = error.message; }
    finally { state.busy = false; draw(state); }
  }
  function draw(state) {
    if (!state.dialog) return;
    const budget = state.path === 'budget';
    const title = budget ? `Budget bulanan — buku ${getReport().year}` : `Proyeksi pemasukan — ${monthNames[state.month - 1]} ${getReport().year}`;
    const actions = h('div', { class: 'editor-actions settings-footer' });
    const button = (text, callback, primary = false, disabled = false) => h('button', { type: 'button', class: ['button', primary && 'button--primary'], text, disabled: state.busy || disabled, onclick: callback });
    const heading = h('header', { class: 'settings-heading' }, h('div', null, h('h2', { id: 'settings-title', text: title }),
      h('p', { class: 'muted small', text: budget ? 'Target berlaku untuk Jan–Des. Kosong berarti belum diatur; 0 berarti Rp0.' : 'Estimasi saja. Pemasukan aktual tetap dicatat melalui transaksi.' })),
      h('button', { type: 'button', class: 'button button--ghost', text: '×', 'aria-label': 'Tutup pengaturan', disabled: state.busy, onclick: () => close(state) }));
    const content = h('div', { class: 'settings-content stack' });
    if (state.conflict) {
      content.append(h('p', { class: 'alert alert--warn', text: 'Pengaturan berubah. Draft Anda tetap tersedia; tinjau nilai server sebelum menyimpan lagi.' }));
      if (state.conflict !== true) content.append(h('div', { class: 'table-scroll' }, h('table', { class: 'grid grid--report' },
        h('thead', null, h('tr', null, ['Sumber / kategori', 'Versi server', 'Draft Anda'].map(text => h('th', { text })))),
        h('tbody', null, (state.conflict.categories ?? state.conflict.sources).map(row => h('tr', null,
          h('th', { scope: 'row', text: row.category ?? row.label }), h('td', { text: describe(row, valuesOf(row)) }),
          h('td', { text: state.values.has(keyOf(row)) ? describe(row, state.values.get(keyOf(row))) : 'Tidak tersedia' })))))));
      actions.append(button('Gunakan data terbaru', () => latest(state, false)), button('Tinjau ulang draft', () => latest(state, true), true));
    } else if (state.preview) {
      content.append(h('h3', { text: 'Tinjau perubahan' }), h('div', { class: 'table-scroll' }, h('table', { class: 'grid grid--report' },
        h('thead', null, h('tr', null, ['Sumber / kategori', 'Sebelum', 'Sesudah'].map(text => h('th', { text })))),
        h('tbody', null, state.preview.map(value => { const row = rowsOf(state).find(row => keyOf(row) === keyOf(value)); return h('tr', null,
          h('th', { scope: 'row', text: row.category ?? row.label }), h('td', { text: describe(row, valuesOf(row)) }), h('td', { text: describe(row, value) })); })))));
      if (budget) {
        const sum = rowsOf(state).reduce((total, row) => total + BigInt(state.values.get(keyOf(row)).amountIdr || '0'), 0n);
        const oldSum = rowsOf(state).reduce((total, row) => total + BigInt(row.amountIdr || '0'), 0n);
        content.append(h('p', { class: 'strong', text: `Total budget: ${money(oldSum.toString())} → ${money(sum.toString())}` }));
      }
      if (state.uncertain) actions.append(button('Periksa hasil', () => inspect(state), true));
      else actions.append(button('Kembali mengedit', () => { state.preview = null; state.operation = null; draw(state); }), button('Simpan perubahan', () => save(state), true));
    } else {
      const fieldset = h('fieldset', { class: 'settings-fields', disabled: state.busy });
      if (budget) {
        fieldset.append(h('div', { class: 'settings-budget-list' }, rowsOf(state).map(row => h('div', { class: 'settings-budget-row' },
          h('div', null, h('strong', { text: row.category }), h('p', { class: 'muted small', text: row.editable ? `Saat ini: ${money(row.amountIdr)}` : row.reason })),
          inputField(state, row, 'amountIdr', `Budget ${row.category}`)))));
      } else {
        for (const row of rowsOf(state)) {
          const source = h('section', { class: 'settings-source' }, h('h3', { text: row.label }));
          if (!row.editable) source.append(h('p', { class: 'muted small', text: row.reason }));
          const fields = h('div', { class: 'settings-source-fields' }, inputField(state, row, 'payoutDay', 'Cair tanggal (1–31)', true));
          if (row.isMealAllowance) fields.append(inputField(state, row, 'workDays', 'Jumlah hari kerja (0–31)', true), inputField(state, row, 'dailyRateIdr', 'Tarif per hari (Rp)'));
          else fields.append(inputField(state, row, 'estimateIdr', 'Estimasi (Rp)'));
          source.append(fields);
          if (row.isMealAllowance) source.append(h('p', { class: 'muted small' }, 'Estimasi uang makan: ', h('strong', { dataset: { estimateId: row.id }, text: money(estimate(row, state.values.get(row.id))) })));
          fieldset.append(source);
        }
        if (!rowsOf(state).length) fieldset.append(h('p', { class: 'muted', text: 'Blok proyeksi yang didukung tidak ditemukan pada bulan ini.' }));
      }
      const form = h('form', { onsubmit: event => { event.preventDefault(); review(state); } }, fieldset);
      content.append(form);
      if (!budget) actions.append(button('Salin bulan sebelumnya', () => copyPrevious(state), false, state.month === 1 || !rowsOf(state).some(row => row.editable)));
      actions.append(button('Tinjau perubahan', () => review(state), true, !rowsOf(state).some(row => row.editable)));
    }
    content.append(h('p', { class: 'settings-message', role: 'status', text: state.message || '' }));
    state.dialog.replaceChildren(heading, content, actions);
  }
  async function open(scope, month) {
    const path = pathOf(scope, month);
    let state = drafts.get(path);
    if (state?.dialog) return;
    if (!state) { state = { path, month, dirty: false, busy: false }; drafts.set(path, state); }
    const dialog = h('dialog', { class: 'settings-dialog', 'aria-labelledby': 'settings-title' });
    state.dialog = dialog;
    dialog.addEventListener('cancel', event => { event.preventDefault(); close(state); });
    dialog.append(h('h2', { id: 'settings-title', text: 'Memuat pengaturan…' }));
    document.body.append(dialog); dialog.showModal();
    if (state.snapshot && (state.dirty || state.uncertain || state.conflict)) { draw(state); return; }
    try {
      const snapshot = await request(path);
      if (state.dialog !== dialog) return;
      reset(state, snapshot); state.message = ''; draw(state);
    }
    catch (error) {
      if (state.dialog !== dialog) return;
      dialog.replaceChildren(h('h2', { id: 'settings-title', text: 'Pengaturan belum tersedia' }), h('p', { role: 'status', text: error.message }),
        h('button', { type: 'button', class: 'button', text: 'Coba lagi', onclick: () => { close(state); open(scope, month); } }),
        h('button', { type: 'button', class: 'button', text: 'Tutup', onclick: () => close(state) }));
    }
  }
  window.addEventListener('beforeunload', event => {
    if ([...drafts.values()].some(state => state.dirty || state.busy || state.uncertain)) { event.preventDefault(); event.returnValue = ''; }
  });
  return { button: (scope, month) => h('button', { type: 'button', class: 'button button--sm', text: scope === 'budget' ? 'Edit budget' : 'Atur proyeksi', onclick: () => open(scope, month) }) };
}

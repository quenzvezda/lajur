// Lajur read-only workbook viewer.
// Renders the server-computed YearReport (embedded as JSON). Headline totals always come from the
// server; anything computed here is either presentation (sorting, filtering) or explicitly labelled
// as "filtered". Nothing here writes back to the server.
(() => {
  'use strict';

  const dataElement = document.getElementById('report-data');
  const app = document.getElementById('app');
  const tabsHost = document.getElementById('sheet-tabs');
  const popoverRoot = document.getElementById('popover-root');
  if (!dataElement || !app || !tabsHost) return;

  const report = JSON.parse(dataElement.textContent);
  const INCOME = 'Pemasukan';
  const EXPENSE = 'Pengeluaran';
  const MONTH_KEYS = ['jan', 'feb', 'mar', 'apr', 'mei', 'jun', 'jul', 'agu', 'sep', 'okt', 'nov', 'des'];
  const MONTH_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'Mei', 'Jun', 'Jul', 'Agu', 'Sep', 'Okt', 'Nov', 'Des'];
  const MONTH_LONG = ['Januari', 'Februari', 'Maret', 'April', 'Mei', 'Juni', 'Juli', 'Agustus', 'September',
    'Oktober', 'November', 'Desember'];
  const DAY_SHORT = ['Min', 'Sen', 'Sel', 'Rab', 'Kam', 'Jum', 'Sab'];

  // ---------- formatting ----------
  const numberFormat = new Intl.NumberFormat('id-ID');
  const fmt = (value) => numberFormat.format(value);
  const rupiah = (value) => (value < 0 ? '−Rp ' : 'Rp ') + fmt(Math.abs(value));
  const signedRupiah = (value) => (value > 0 ? '+' : '') + rupiah(value);
  const percent = (ratio, digits = 1) =>
    ratio === null || ratio === undefined || !Number.isFinite(ratio)
      ? '—'
      : (ratio * 100).toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: digits }) + '%';
  const compact = (value) => {
    const abs = Math.abs(value);
    const sign = value < 0 ? '−' : '';
    if (abs >= 1e9) return sign + (abs / 1e9).toLocaleString('id-ID', { maximumFractionDigits: 1 }) + ' M';
    if (abs >= 1e6) return sign + (abs / 1e6).toLocaleString('id-ID', { maximumFractionDigits: 1 }) + ' jt';
    if (abs >= 1e3) return sign + (abs / 1e3).toLocaleString('id-ID', { maximumFractionDigits: 0 }) + ' rb';
    return sign + fmt(abs);
  };
  const parseDate = (iso) => {
    const [y, m, d] = iso.split('-').map(Number);
    return { y, m, d, weekday: new Date(Date.UTC(y, m - 1, d)).getUTCDay() };
  };
  const formatDate = (iso) => {
    const p = parseDate(iso);
    return `${DAY_SHORT[p.weekday]}, ${String(p.d).padStart(2, '0')} ${MONTH_SHORT[p.m - 1]}`;
  };
  const daysInMonth = (year, month) => new Date(Date.UTC(year, month, 0)).getUTCDate();
  const collator = new Intl.Collator('id-ID', { sensitivity: 'base', numeric: true });

  // ---------- DOM helper (no innerHTML: all text goes through textContent) ----------
  function h(tag, props, ...children) {
    const svg = tag.startsWith('svg:');
    const el = svg ? document.createElementNS('http://www.w3.org/2000/svg', tag.slice(4)) : document.createElement(tag);
    if (props) {
      for (const [key, value] of Object.entries(props)) {
        if (value === null || value === undefined || value === false) continue;
        if (key === 'class') el.setAttribute('class', Array.isArray(value) ? value.filter(Boolean).join(' ') : value);
        else if (key === 'text') el.textContent = value;
        else if (key === 'style') Object.entries(value).forEach(([k, v]) => el.style.setProperty(k, v));
        else if (key === 'dataset') Object.assign(el.dataset, value);
        else if (key.startsWith('on') && typeof value === 'function') el.addEventListener(key.slice(2), value);
        else if (value === true) el.setAttribute(key, '');
        else el.setAttribute(key, String(value));
      }
    }
    for (const child of children.flat(Infinity)) {
      if (child === null || child === undefined || child === false) continue;
      el.append(child instanceof Node ? child : document.createTextNode(String(child)));
    }
    return el;
  }

  const typeClass = (type) => (type === INCOME ? 'is-income' : type === EXPENSE ? 'is-expense' : 'is-other');
  const typePill = (type) => h('span', { class: ['pill', typeClass(type)], text: type });

  // ---------- tooltip ----------
  const tooltip = h('div', { class: 'tooltip', role: 'status', hidden: true });
  document.body.append(tooltip);
  function showTooltip(event, lines) {
    tooltip.replaceChildren(...lines.map((line) =>
      typeof line === 'string' ? h('div', { text: line }) : line));
    tooltip.hidden = false;
    const pad = 14;
    const rect = tooltip.getBoundingClientRect();
    let x = event.clientX + pad;
    let y = event.clientY + pad;
    if (x + rect.width > window.innerWidth - 8) x = event.clientX - rect.width - pad;
    if (y + rect.height > window.innerHeight - 8) y = event.clientY - rect.height - pad;
    tooltip.style.setProperty('left', `${Math.max(8, x)}px`);
    tooltip.style.setProperty('top', `${Math.max(8, y)}px`);
  }
  const hideTooltip = () => { tooltip.hidden = true; };

  // ---------- state & routing ----------
  const allRows = report.months.flatMap((month) =>
    month.rows.map((row, index) => ({ ...row, month: month.month, line: index + 1 })));
  const views = [
    { key: 'rekap', label: 'Rekap', kind: 'rekap' },
    ...report.months.map((m) => ({ key: MONTH_KEYS[m.month - 1], label: MONTH_SHORT[m.month - 1], kind: 'month', month: m.month, count: m.transactionCount })),
    { key: 'semua', label: 'Semua transaksi', kind: 'all' },
    { key: 'master', label: 'Master', kind: 'master' }
  ];
  const tableStates = new Map();
  let currentView = null;
  let resizeHandlers = [];

  function defaultViewKey() {
    const now = new Date();
    if (now.getFullYear() === report.year) {
      const month = report.months[now.getMonth()];
      if (month && month.transactionCount > 0) return MONTH_KEYS[now.getMonth()];
    }
    return 'rekap';
  }

  function viewFromHash() {
    const key = decodeURIComponent(location.hash.replace(/^#/, '')).toLowerCase();
    return views.find((view) => view.key === key) || views.find((view) => view.key === defaultViewKey());
  }

  function go(key) {
    if (location.hash !== `#${key}`) location.hash = key; else render();
  }

  function renderTabs() {
    tabsHost.replaceChildren(...views.map((view) => {
      const active = view === currentView;
      const empty = view.kind === 'month' && view.count === 0;
      return h('a', {
        class: ['sheet-tab', active && 'is-active', empty && 'is-empty', view.kind !== 'month' && 'is-special'],
        href: `#${view.key}`,
        role: 'tab',
        'aria-selected': active ? 'true' : 'false',
        title: view.kind === 'month' ? `${MONTH_LONG[view.month - 1]} — ${view.count} transaksi` : view.label
      }, view.label, view.kind === 'month' && view.count > 0 ? h('span', { class: 'tab-count', text: view.count }) : null);
    }));
    tabsHost.querySelector('.is-active')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }

  function render() {
    closePopover();
    hideTooltip();
    resizeHandlers = [];
    const previous = currentView;
    currentView = viewFromHash();
    renderTabs();
    const scroll = previous === currentView ? window.scrollY : 0;
    if (currentView.kind === 'rekap') app.replaceChildren(renderRekap());
    else if (currentView.kind === 'month') app.replaceChildren(renderMonth(report.months[currentView.month - 1]));
    else if (currentView.kind === 'all') app.replaceChildren(renderAll());
    else app.replaceChildren(renderMaster());
    document.title = `${currentView.kind === 'month' ? MONTH_LONG[currentView.month - 1] : currentView.label} ${report.year} · Lajur`;
    runResizeHandlers();
    window.scrollTo({ top: scroll });
  }

  function runResizeHandlers() { resizeHandlers.forEach((handler) => handler()); }
  let resizeTimer = 0;
  window.addEventListener('resize', () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(runResizeHandlers, 120);
  });
  window.addEventListener('hashchange', render);

  // ---------- shared building blocks ----------
  function kpi(label, value, options = {}) {
    return h('div', { class: ['kpi', options.tone && `kpi--${options.tone}`] },
      h('div', { class: 'kpi-label' }, label, options.typeLabel ? h('span', { class: ['pill', 'pill--sm', options.tone === 'income' ? 'is-income' : 'is-expense'], text: options.typeLabel }) : null),
      h('div', { class: 'kpi-value', text: value }),
      options.hint ? h('div', { class: 'kpi-hint', text: options.hint }) : null);
  }

  function panel(title, body, options = {}) {
    return h('section', { class: ['panel', options.className] },
      h('header', { class: 'panel-head' },
        h('h2', { text: title }),
        options.aside ? h('div', { class: 'panel-aside' }, options.aside) : null),
      h('div', { class: 'panel-body' }, body));
  }

  function unavailable(title, reason) {
    return h('div', { class: 'unavailable' },
      h('div', { class: 'unavailable-title' }, h('span', { class: 'badge badge--muted', text: 'Belum tersedia di web' }), ' ', title),
      h('p', { text: reason }));
  }

  function barCell(ratio, tone) {
    const width = Math.max(0, Math.min(1, ratio || 0));
    return h('div', { class: 'bar-track' }, h('div', { class: ['bar-fill', tone && `bar-fill--${tone}`], style: { width: `${(width * 100).toFixed(2)}%` } }));
  }

  // ---------- month view ----------
  function renderMonth(month) {
    const name = `${MONTH_LONG[month.month - 1]} ${report.year}`;
    const tableKey = `m${month.month}`;
    const rows = month.rows.map((row, index) => ({ ...row, month: month.month, line: index + 1 }));
    const table = transactionTable(tableKey, rows, { showMonth: false });

    const head = h('div', { class: 'view-head' },
      h('div', null,
        h('h1', { text: name }),
        h('p', { class: 'muted', text: month.transactionCount === 0 ? 'Belum ada transaksi di bulan ini.' : `${month.transactionCount} transaksi di tabel ${MONTH_SHORT[month.month - 1]}.` })),
      monthStepper(month.month));

    const kpis = h('div', { class: 'kpi-row' },
      kpi('Pemasukan', rupiah(month.incomeIdr), { tone: 'income', typeLabel: INCOME }),
      kpi('Pengeluaran', rupiah(month.expenseIdr), { tone: 'expense', typeLabel: EXPENSE }),
      kpi('Selisih', signedRupiah(month.netIdr), { tone: month.netIdr < 0 ? 'negative' : 'neutral', hint: 'Pemasukan − pengeluaran (bukan saldo rekening)' }),
      kpi('Saving rate', percent(month.savingRate), { hint: month.savingRate === null ? 'Kosong: belum ada pemasukan' : 'Selisih ÷ pemasukan' }),
      kpi('Transaksi', fmt(month.transactionCount), { hint: `Kumulatif selisih s.d. bulan ini: ${signedRupiah(month.cumulativeNetIdr)}` }));

    const missing = 'Belum diisi';
    const balances = h('div', { class: 'balance-strip' },
      balanceItem('Opening balance', month.openingBalanceIdr === null ? missing : rupiah(month.openingBalanceIdr), 'Input manual di sheet (D1)'),
      h('span', { class: 'balance-op', text: '+' }),
      balanceItem('Pemasukan', rupiah(month.incomeIdr)),
      h('span', { class: 'balance-op', text: '−' }),
      balanceItem('Pengeluaran', rupiah(month.expenseIdr)),
      h('span', { class: 'balance-op', text: '=' }),
      balanceItem('Closing (estimasi)', month.estimatedClosingIdr === null ? '—' : rupiah(month.estimatedClosingIdr), month.estimatedClosingIdr === null ? 'Perlu opening balance' : null),
      h('span', { class: 'balance-sep' }),
      balanceItem('Saldo aktual', month.actualBalanceIdr === null || month.actualBalanceIdr === 0 ? missing : rupiah(month.actualBalanceIdr), 'Input manual di sheet (B1)'),
      balanceItem('Selisih saldo', month.balanceDifferenceIdr === null ? '—' : signedRupiah(month.balanceDifferenceIdr),
        month.balanceDifferenceIdr === null ? 'Perlu saldo aktual & opening' : 'Saldo aktual − closing estimasi', month.balanceDifferenceIdr !== null && month.balanceDifferenceIdr !== 0 ? 'warn' : null));

    const side = h('div', { class: 'side-stack' },
      categoryPivot(month, tableKey),
      ratioPanel(month),
      topExpenses(rows));

    const daily = panel('Pengeluaran harian & kumulatif', h('div', { class: 'chart-host' }), {
      aside: legend([['expense', 'Harian'], ['line', 'Kumulatif'], ['budget', 'Total budget Master']])
    });
    const dailyHost = daily.querySelector('.chart-host');
    resizeHandlers.push(() => drawDailyChart(dailyHost, month));

    return h('div', { class: 'view' },
      head, kpis, balances,
      h('div', { class: 'split' },
        h('div', { class: 'split-main' }, panel('Tabel transaksi', table, { className: 'panel--table' })),
        h('aside', { class: 'split-side' }, side)),
      daily,
      h('div', { class: 'unavailable-grid' },
        unavailable('Proyeksi kas & income belum cair', 'Blok ini memakai input estimasi (sel kuning) dan TODAY() di Excel. Web belum memetakan rumusnya, jadi tidak ditampilkan agar tidak muncul angka palsu.'),
        unavailable('Laju belanja & proyeksi akhir bulan', 'Membutuhkan aturan tanggal hari ini/zona waktu yang sama persis dengan workbook. Dijadwalkan untuk tahap berikutnya.')));
  }

  function balanceItem(label, value, hint, tone) {
    return h('div', { class: ['balance-item', tone && `is-${tone}`], title: hint || null },
      h('div', { class: 'balance-label', text: label }),
      h('div', { class: 'balance-value', text: value }));
  }

  function monthStepper(month) {
    const prev = month > 1 ? MONTH_KEYS[month - 2] : null;
    const next = month < 12 ? MONTH_KEYS[month] : null;
    return h('div', { class: 'stepper' },
      h('a', { class: ['button', 'button--ghost', !prev && 'is-disabled'], href: prev ? `#${prev}` : null, 'aria-disabled': prev ? null : 'true', text: '‹ Sebelumnya' }),
      h('a', { class: ['button', 'button--ghost', !next && 'is-disabled'], href: next ? `#${next}` : null, 'aria-disabled': next ? null : 'true', text: 'Berikutnya ›' }));
  }

  function categoryPivot(month, tableKey) {
    const total = month.expenseIdr;
    const state = tableState(tableKey);
    const activeCategory = state.filters.category && state.filters.category.size === 1 ? [...state.filters.category][0] : null;
    if (month.expenseByCategory.length === 0) {
      return panel('Pengeluaran per kategori', h('p', { class: 'muted', text: 'Belum ada pengeluaran.' }));
    }
    const body = h('table', { class: 'pivot' },
      h('thead', null, h('tr', null,
        h('th', { text: 'Kategori' }), h('th', { class: 'num', text: 'Jumlah' }), h('th', { class: 'num', text: '%' }), h('th', { class: 'num', text: 'Budget' }))),
      h('tbody', null, month.expenseByCategory.map((item) => {
        const share = total > 0 ? item.amountIdr / total : 0;
        const usage = item.monthlyBudgetIdr ? item.amountIdr / item.monthlyBudgetIdr : null;
        return h('tr', {
          class: ['is-clickable', activeCategory === item.category && 'is-selected'],
          tabindex: '0',
          title: `Klik untuk memfilter tabel: ${item.category}`,
          onclick: () => toggleCategoryFilter(tableKey, item.category),
          onkeydown: (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggleCategoryFilter(tableKey, item.category); } }
        },
        h('td', null, h('div', { class: 'pivot-label', text: item.category }), barCell(share, 'expense')),
        h('td', { class: 'num', text: fmt(item.amountIdr) }),
        h('td', { class: 'num muted', text: percent(share) }),
        h('td', { class: ['num', usage !== null && usage > 1 && 'is-over'], title: item.monthlyBudgetIdr ? `Budget ${rupiah(item.monthlyBudgetIdr)} — terpakai ${percent(usage)}` : 'Tidak ada budget di Master' },
          item.monthlyBudgetIdr ? percent(usage, 0) : '—'));
      })),
      h('tfoot', null, h('tr', null,
        h('th', { text: 'Total pengeluaran' }), h('th', { class: 'num', text: fmt(total) }), h('th', { class: 'num', text: '100%' }), h('th'))));
    return panel('Pengeluaran per kategori', body, {
      aside: h('span', { class: 'muted small', text: 'Klik baris → filter tabel' })
    });
  }

  function toggleCategoryFilter(tableKey, category) {
    const state = tableState(tableKey);
    const same = state.filters.category && state.filters.category.size === 1 && state.filters.category.has(category);
    if (same) {
      delete state.filters.category;
      delete state.filters.type;
    } else {
      state.filters.category = new Set([category]);
      state.filters.type = new Set([EXPENSE]);
    }
    render();
  }

  function ratioPanel(month) {
    const sum = month.incomeIdr + month.expenseIdr;
    if (sum === 0) return panel('Rasio pemasukan : pengeluaran', h('p', { class: 'muted', text: 'Belum ada data.' }));
    const incomeShare = month.incomeIdr / sum;
    const expenseShare = month.expenseIdr / sum;
    return panel('Rasio pemasukan : pengeluaran', h('div', null,
      h('div', { class: 'ratio-bar', role: 'img', 'aria-label': `Pemasukan ${percent(incomeShare)}, pengeluaran ${percent(expenseShare)}` },
        h('div', { class: 'ratio-seg is-income', style: { flex: `${incomeShare} 1 0` } }),
        h('div', { class: 'ratio-seg is-expense', style: { flex: `${expenseShare} 1 0` } })),
      h('div', { class: 'ratio-legend' },
        h('span', null, typePill(INCOME), ' ', percent(incomeShare)),
        h('span', null, typePill(EXPENSE), ' ', percent(expenseShare))),
      month.incomeIdr > 0 ? h('p', { class: 'muted small', text: `Pengeluaran = ${percent(month.expenseIdr / month.incomeIdr)} dari pemasukan.` }) : null));
  }

  function topExpenses(rows) {
    const top = rows.filter((row) => row.type === EXPENSE)
      .sort((a, b) => b.amountIdr - a.amountIdr || a.date.localeCompare(b.date) || a.line - b.line)
      .slice(0, 5);
    if (top.length === 0) return panel('5 belanja terbesar', h('p', { class: 'muted', text: 'Belum ada pengeluaran.' }));
    return panel('5 belanja terbesar', h('ol', { class: 'rank' }, top.map((row) =>
      h('li', null,
        h('div', { class: 'rank-main' },
          h('div', { class: 'rank-title', text: row.description || row.category }),
          h('div', { class: 'rank-sub muted small', text: `${formatDate(row.date)} · ${row.category}${row.paymentMethod ? ' · ' + row.paymentMethod : ''}` })),
        h('div', { class: 'rank-value is-expense', text: fmt(row.amountIdr) })))));
  }

  // ---------- transaction table (Excel-like autofilter) ----------
  const COLUMNS = [
    { key: 'line', label: '#', numeric: true, sortOnly: true, className: 'col-line' },
    { key: 'month', label: 'Bulan', filter: 'values', allOnly: true, display: (r) => MONTH_SHORT[r.month - 1], sortValue: (r) => r.month },
    { key: 'date', label: 'Tanggal', filter: 'values', display: (r) => formatDate(r.date), className: 'col-date' },
    { key: 'type', label: 'Tipe', filter: 'values' },
    { key: 'paymentMethod', label: 'Pembayaran', filter: 'values' },
    { key: 'category', label: 'Kategori', filter: 'values' },
    { key: 'amountIdr', label: 'Jumlah (Rp)', filter: 'range', numeric: true, className: 'num' },
    { key: 'description', label: 'Keterangan', filter: 'text', className: 'col-desc' }
  ];

  function tableState(key) {
    if (!tableStates.has(key)) tableStates.set(key, { search: '', sort: null, filters: {}, selected: null });
    return tableStates.get(key);
  }

  const cellText = (row, column) => {
    const value = row[column.key];
    return value === null || value === undefined ? '' : String(value);
  };

  function applyFilters(rows, state, columns, except) {
    const query = state.search.trim().toLocaleLowerCase('id-ID');
    return rows.filter((row) => {
      for (const column of columns) {
        if (column.key === except) continue;
        const filter = state.filters[column.key];
        if (!filter) continue;
        if (filter instanceof Set) {
          if (!filter.has(cellText(row, column))) return false;
        } else if (column.filter === 'range') {
          if (filter.min !== null && row[column.key] < filter.min) return false;
          if (filter.max !== null && row[column.key] > filter.max) return false;
        } else if (column.filter === 'text') {
          if (!cellText(row, column).toLocaleLowerCase('id-ID').includes(filter.text)) return false;
        }
      }
      if (!query) return true;
      return [row.type, row.paymentMethod, row.category, row.description || '', fmt(row.amountIdr), String(row.amountIdr), formatDate(row.date)]
        .some((text) => text.toLocaleLowerCase('id-ID').includes(query));
    });
  }

  function sortRows(rows, state, columns) {
    const sort = state.sort;
    if (!sort) return rows;
    const column = columns.find((c) => c.key === sort.key);
    if (!column) return rows;
    const direction = sort.dir === 'desc' ? -1 : 1;
    const value = column.sortValue || ((row) => row[column.key]);
    return [...rows].sort((a, b) => {
      const va = value(a); const vb = value(b);
      let result;
      if (typeof va === 'number' && typeof vb === 'number') result = va - vb;
      else result = collator.compare(va ?? '', vb ?? '');
      if (result === 0) result = (a.month - b.month) || (a.line - b.line);
      return result * direction;
    });
  }

  function transactionTable(key, rows, options) {
    const columns = COLUMNS.filter((column) => !column.allOnly || options.showMonth);
    const state = tableState(key);
    const search = h('input', {
      type: 'search', class: 'input', placeholder: 'Cari kategori, keterangan, nominal…', value: state.search,
      'aria-label': 'Cari transaksi'
    });
    let searchTimer = 0;
    search.addEventListener('input', () => {
      clearTimeout(searchTimer);
      searchTimer = setTimeout(() => { state.search = search.value; draw(); }, 120);
    });
    const toolbarExtras = h('div', { class: 'tx-toolbar-rest' });
    const content = h('div', { class: 'tx-content' });
    const host = h('div', { class: 'tx' }, h('div', { class: 'tx-toolbar' }, search, toolbarExtras), content);

    const draw = () => {
      if (search.value !== state.search) search.value = state.search;
      const filtered = sortRows(applyFilters(rows, state, columns), state, columns);
      const active = Object.keys(state.filters).length > 0 || state.search.trim() !== '';
      toolbarExtras.replaceChildren(...toolbar(filtered.length));
      content.replaceChildren(grid(filtered), footer(filtered, active));
    };

    const toolbar = (visible) => {
      const chips = columns.filter((c) => state.filters[c.key]).map((column) =>
        h('button', {
          type: 'button', class: 'chip', title: 'Hapus filter ini',
          onclick: () => { delete state.filters[column.key]; draw(); }
        }, `${column.label}: ${describeFilter(column, state.filters[column.key])}`, h('span', { class: 'chip-x', 'aria-hidden': 'true', text: '×' })));
      const hasAny = chips.length > 0 || state.search || state.sort;
      return [
        h('div', { class: 'chips' }, chips),
        h('div', { class: 'tx-toolbar-end' },
          h('span', { class: 'muted small', text: visible === rows.length ? `${rows.length} baris` : `${visible} dari ${rows.length} baris` }),
          hasAny ? h('button', { type: 'button', class: 'button button--ghost button--sm', text: 'Reset', onclick: () => { state.filters = {}; state.search = ''; state.sort = null; draw(); } }) : null)];
    };

    const grid = (filtered) => {
      const headRow = h('tr', null, columns.map((column) => {
        const sorted = state.sort && state.sort.key === column.key ? state.sort.dir : null;
        const isFiltered = Boolean(state.filters[column.key]);
        return h('th', {
          class: [column.className, column.numeric && 'num', sorted && 'is-sorted', isFiltered && 'is-filtered'],
          'aria-sort': sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : 'none',
          scope: 'col'
        },
        h('div', { class: 'th-inner' },
          h('button', {
            type: 'button', class: 'th-sort', title: 'Urutkan',
            onclick: () => {
              state.sort = !sorted ? { key: column.key, dir: 'asc' } : sorted === 'asc' ? { key: column.key, dir: 'desc' } : null;
              draw();
            }
          }, column.label, h('span', { class: 'sort-mark', 'aria-hidden': 'true', text: sorted === 'asc' ? '▲' : sorted === 'desc' ? '▼' : '' })),
          column.sortOnly ? null : h('button', {
            type: 'button', class: ['th-filter', isFiltered && 'is-on'], title: `Filter ${column.label}`, 'aria-label': `Filter ${column.label}`,
            onclick: (event) => { event.stopPropagation(); openFilter(event.currentTarget, column, rows, state, columns, draw); }
          }, h('span', { 'aria-hidden': 'true', text: isFiltered ? '⏷' : '▾' }))));
      }));

      const body = filtered.length === 0
        ? h('tr', null, h('td', { class: 'tx-empty', colspan: String(columns.length), text: rows.length === 0 ? 'Belum ada transaksi.' : 'Tidak ada baris yang cocok dengan filter.' }))
        : filtered.map((row) => {
          const id = `${row.month}:${row.line}`;
          return h('tr', {
            class: [state.selected === id && 'is-selected'],
            onclick: () => { state.selected = state.selected === id ? null : id; draw(); }
          }, columns.map((column) => {
            if (column.key === 'type') return h('td', null, typePill(row.type));
            if (column.key === 'amountIdr') return h('td', { class: ['num', 'amount', typeClass(row.type)], text: fmt(row.amountIdr) });
            if (column.key === 'paymentMethod' && !row.paymentMethod) return h('td', { class: 'muted', title: 'Pembayaran kosong — tetap dihitung dalam total', text: '—' });
            if (column.key === 'description') return h('td', { class: 'col-desc' }, h('div', { class: 'desc', text: row.description || '' }));
            if (column.key === 'line') return h('td', { class: 'col-line', text: row.line });
            return h('td', { class: column.className, text: column.display ? column.display(row) : cellText(row, column) });
          }));
        });

      return h('div', { class: 'tx-scroll' }, h('table', { class: 'grid' }, h('thead', null, headRow), h('tbody', null, body)));
    };

    const footer = (filtered, active) => {
      let income = 0; let expense = 0;
      for (const row of filtered) {
        if (row.type === INCOME) income += row.amountIdr;
        else if (row.type === EXPENSE) expense += row.amountIdr;
      }
      return h('div', { class: ['tx-footer', active && 'is-filtered'] },
        h('span', { class: 'tx-footer-label', text: active ? 'Total baris terfilter' : 'Total tabel' }),
        h('span', null, typePill(INCOME), ' ', h('strong', { class: 'is-income', text: fmt(income) })),
        h('span', null, typePill(EXPENSE), ' ', h('strong', { class: 'is-expense', text: fmt(expense) })),
        h('span', null, 'Selisih ', h('strong', { text: signedRupiah(income - expense) })),
        active ? h('span', { class: 'muted small', text: 'Kartu ringkasan di atas tetap total penuh (tanpa filter).' }) : null);
    };

    draw();
    return host;
  }

  function describeFilter(column, filter) {
    if (filter instanceof Set) {
      const values = [...filter].map((v) => (v === '' ? '(kosong)' : column.key === 'date' ? formatDate(v) : column.key === 'month' ? MONTH_SHORT[Number(v) - 1] : v));
      return values.length <= 2 ? values.join(', ') : `${values.length} nilai`;
    }
    if (column.filter === 'range') {
      if (filter.min !== null && filter.max !== null) return `${fmt(filter.min)}–${fmt(filter.max)}`;
      return filter.min !== null ? `≥ ${fmt(filter.min)}` : `≤ ${fmt(filter.max)}`;
    }
    return `“${filter.text}”`;
  }

  // ---------- filter popover ----------
  let openPopover = null;
  function closePopover() {
    if (!openPopover) return;
    openPopover.remove();
    openPopover = null;
    document.removeEventListener('mousedown', outsideClose, true);
    document.removeEventListener('keydown', escClose, true);
  }
  function outsideClose(event) { if (openPopover && !openPopover.contains(event.target)) closePopover(); }
  function escClose(event) { if (event.key === 'Escape') closePopover(); }

  function openFilter(anchor, column, rows, state, columns, redraw) {
    closePopover();
    const sortButtons = h('div', { class: 'pop-sort' },
      h('button', { type: 'button', class: 'pop-item', onclick: () => { state.sort = { key: column.key, dir: 'asc' }; closePopover(); redraw(); } },
        column.numeric ? 'Urutkan terkecil → terbesar' : column.key === 'date' || column.key === 'month' ? 'Urutkan terlama → terbaru' : 'Urutkan A → Z'),
      h('button', { type: 'button', class: 'pop-item', onclick: () => { state.sort = { key: column.key, dir: 'desc' }; closePopover(); redraw(); } },
        column.numeric ? 'Urutkan terbesar → terkecil' : column.key === 'date' || column.key === 'month' ? 'Urutkan terbaru → terlama' : 'Urutkan Z → A'));

    let body;
    let apply;
    if (column.filter === 'values') {
      // Like Excel: the list reflects rows left by the *other* active filters.
      const source = applyFilters(rows, state, columns, column.key);
      const counts = new Map();
      for (const row of source) {
        const value = cellText(row, column);
        counts.set(value, (counts.get(value) || 0) + 1);
      }
      const current = state.filters[column.key];
      const values = [...counts.keys()].sort((a, b) =>
        column.key === 'month' ? Number(a) - Number(b) : column.key === 'date' ? a.localeCompare(b) : collator.compare(a, b));
      const checked = new Set(current ? values.filter((v) => current.has(v)) : values);
      const label = (value) => (value === '' ? '(kosong)' : column.key === 'date' ? formatDate(value) : column.key === 'month' ? MONTH_LONG[Number(value) - 1] : value);
      const list = h('div', { class: 'pop-list' });
      const find = h('input', { type: 'search', class: 'input input--sm', placeholder: 'Cari nilai…', 'aria-label': 'Cari nilai filter' });
      const all = h('input', { type: 'checkbox' });
      const drawList = () => {
        const needle = find.value.trim().toLocaleLowerCase('id-ID');
        const shown = values.filter((v) => !needle || label(v).toLocaleLowerCase('id-ID').includes(needle));
        all.checked = shown.length > 0 && shown.every((v) => checked.has(v));
        all.indeterminate = !all.checked && shown.some((v) => checked.has(v));
        list.replaceChildren(...shown.map((value) => {
          const box = h('input', { type: 'checkbox', checked: checked.has(value) });
          box.addEventListener('change', () => { if (box.checked) checked.add(value); else checked.delete(value); drawList(); });
          return h('label', { class: 'pop-check' }, box, h('span', { class: 'pop-check-label', text: label(value) }), h('span', { class: 'muted small', text: counts.get(value) }));
        }));
        if (shown.length === 0) list.append(h('p', { class: 'muted small', text: 'Tidak ada nilai.' }));
      };
      find.addEventListener('input', drawList);
      all.addEventListener('change', () => {
        const needle = find.value.trim().toLocaleLowerCase('id-ID');
        values.filter((v) => !needle || label(v).toLocaleLowerCase('id-ID').includes(needle))
          .forEach((v) => (all.checked ? checked.add(v) : checked.delete(v)));
        drawList();
      });
      drawList();
      body = h('div', null, find, h('label', { class: 'pop-check pop-check--all' }, all, h('span', { text: '(Pilih semua)' })), list);
      apply = () => {
        if (checked.size === values.length) delete state.filters[column.key];
        else state.filters[column.key] = new Set(checked);
      };
    } else if (column.filter === 'range') {
      const current = state.filters[column.key] || { min: null, max: null };
      const min = h('input', { type: 'number', class: 'input input--sm', inputmode: 'numeric', placeholder: 'Minimum', value: current.min ?? '' });
      const max = h('input', { type: 'number', class: 'input input--sm', inputmode: 'numeric', placeholder: 'Maksimum', value: current.max ?? '' });
      body = h('div', { class: 'pop-range' }, h('label', null, 'Dari', min), h('label', null, 'Sampai', max));
      apply = () => {
        const lo = min.value === '' ? null : Number(min.value);
        const hi = max.value === '' ? null : Number(max.value);
        if (lo === null && hi === null) delete state.filters[column.key];
        else state.filters[column.key] = { min: lo, max: hi };
      };
    } else {
      const current = state.filters[column.key];
      const input = h('input', { type: 'search', class: 'input input--sm', placeholder: 'Mengandung teks…', value: current ? current.text : '' });
      body = h('div', null, input);
      apply = () => {
        const text = input.value.trim().toLocaleLowerCase('id-ID');
        if (!text) delete state.filters[column.key]; else state.filters[column.key] = { text };
      };
    }

    const popover = h('div', { class: 'popover', role: 'dialog', 'aria-label': `Filter ${column.label}` },
      sortButtons,
      h('div', { class: 'pop-divider' }),
      body,
      h('div', { class: 'pop-actions' },
        h('button', { type: 'button', class: 'button button--ghost button--sm', text: 'Hapus filter', onclick: () => { delete state.filters[column.key]; closePopover(); redraw(); } }),
        h('button', { type: 'button', class: 'button button--primary button--sm', text: 'Terapkan', onclick: () => { apply(); closePopover(); redraw(); } })));
    popover.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && event.target.tagName === 'INPUT' && event.target.type !== 'checkbox') { apply(); closePopover(); redraw(); }
    });
    popoverRoot.append(popover);
    openPopover = popover;
    const rect = anchor.getBoundingClientRect();
    const width = popover.offsetWidth;
    const left = Math.min(Math.max(8, rect.right - width), window.innerWidth - width - 8);
    popover.style.setProperty('left', `${left + window.scrollX}px`);
    popover.style.setProperty('top', `${rect.bottom + window.scrollY + 4}px`);
    popover.querySelector('input:not([type=checkbox])')?.focus();
    setTimeout(() => {
      document.addEventListener('mousedown', outsideClose, true);
      document.addEventListener('keydown', escClose, true);
    });
  }

  // ---------- charts (inline SVG, sized to container) ----------
  function niceTicks(min, max, count = 4) {
    if (min === max) { max = min + 1; }
    const span = max - min;
    const raw = span / count;
    const magnitude = Math.pow(10, Math.floor(Math.log10(raw)));
    const step = [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => span / s <= count) || 10 * magnitude;
    const start = Math.floor(min / step) * step;
    const end = Math.ceil(max / step) * step;
    const ticks = [];
    for (let v = start; v <= end + step / 2; v += step) ticks.push(Math.round(v));
    return ticks;
  }

  function legend(items) {
    return h('div', { class: 'legend' }, items.map(([kind, label]) =>
      h('span', { class: 'legend-item' }, h('span', { class: ['legend-swatch', `legend-swatch--${kind}`] }), label)));
  }

  function chartFrame(host, height) {
    const width = Math.max(280, host.clientWidth);
    const svg = h('svg:svg', { width, height, viewBox: `0 0 ${width} ${height}`, class: 'chart', role: 'img' });
    host.replaceChildren(svg);
    return { svg, width, height };
  }

  function yAxis(svg, ticks, y, left, right) {
    for (const tick of ticks) {
      svg.append(h('svg:line', { class: tick === 0 ? 'axis-zero' : 'gridline', x1: left, x2: right, y1: y(tick), y2: y(tick) }));
      svg.append(h('svg:text', { class: 'axis-label', x: left - 8, y: y(tick) + 4, 'text-anchor': 'end' }, compact(tick)));
    }
  }

  function drawDailyChart(host, month) {
    if (!host.isConnected) return;
    const days = daysInMonth(report.year, month.month);
    const daily = new Array(days).fill(0);
    for (const row of month.rows) {
      if (row.type !== EXPENSE) continue;
      const day = parseDate(row.date).d;
      if (day >= 1 && day <= days) daily[day - 1] += row.amountIdr;
    }
    const cumulative = [];
    daily.reduce((sum, value, index) => (cumulative[index] = sum + value), 0);
    const budget = report.categories
      .filter((c) => c.kind === EXPENSE && c.monthlyBudgetIdr)
      .reduce((sum, c) => sum + c.monthlyBudgetIdr, 0);

    const { svg, width, height } = chartFrame(host, 260);
    svg.setAttribute('aria-label', `Pengeluaran harian dan kumulatif ${MONTH_LONG[month.month - 1]}`);
    const margin = { top: 16, right: 16, bottom: 28, left: 64 };
    // Only bring the budget line into scale when it is close enough to be informative.
    const spent = cumulative[days - 1] || 0;
    const budgetInScale = budget > 0 && budget <= Math.max(spent, 1) * 1.6;
    const max = Math.max(1, spent, budgetInScale ? budget : 0);
    const ticks = niceTicks(0, max);
    const top = ticks[ticks.length - 1];
    const plotW = width - margin.left - margin.right;
    const plotH = height - margin.top - margin.bottom;
    const band = plotW / days;
    const x = (i) => margin.left + band * i + band / 2;
    const y = (v) => margin.top + plotH - (v / top) * plotH;
    yAxis(svg, ticks, y, margin.left, width - margin.right);

    daily.forEach((value, i) => {
      if (value <= 0) return;
      const barW = Math.max(2, Math.min(18, band - 3));
      svg.append(h('svg:rect', { class: 'mark-expense', x: x(i) - barW / 2, y: y(value), width: barW, height: Math.max(1, y(0) - y(value)), rx: 2 }));
    });
    if (budgetInScale) {
      svg.append(h('svg:line', { class: 'mark-budget', x1: margin.left, x2: width - margin.right, y1: y(budget), y2: y(budget) }));
    } else if (budget > 0) {
      svg.append(h('svg:text', { class: 'axis-label', x: width - margin.right, y: 11, 'text-anchor': 'end' },
        `Total budget Master ${rupiah(budget)} · terpakai ${percent(spent / budget)}`));
    }
    const path = cumulative.map((v, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(' ');
    svg.append(h('svg:path', { class: 'mark-line', d: path }));

    const labelEvery = days > 20 && width < 640 ? 5 : days > 20 ? 2 : 1;
    for (let i = 0; i < days; i++) {
      if ((i + 1) % labelEvery !== 0 && i !== 0) continue;
      svg.append(h('svg:text', { class: 'axis-label', x: x(i), y: height - 8, 'text-anchor': 'middle' }, String(i + 1)));
    }
    const marker = h('svg:circle', { class: 'mark-dot', r: 4, cx: -10, cy: -10 });
    svg.append(marker);
    for (let i = 0; i < days; i++) {
      svg.append(h('svg:rect', {
        class: 'hit', x: margin.left + band * i, y: margin.top, width: band, height: plotH,
        onmousemove: (event) => {
          marker.setAttribute('cx', x(i)); marker.setAttribute('cy', y(cumulative[i]));
          showTooltip(event, [
            h('strong', { text: `${formatDate(`${report.year}-${String(month.month).padStart(2, '0')}-${String(i + 1).padStart(2, '0')}`)}` }),
            `Harian: ${rupiah(daily[i])}`,
            `Kumulatif: ${rupiah(cumulative[i])}`,
            budget > 0 ? `Budget terpakai: ${percent(cumulative[i] / budget)}` : ''
          ].filter(Boolean));
        },
        onmouseleave: () => { hideTooltip(); marker.setAttribute('cx', -10); }
      }));
    }
  }

  function drawYearChart(host) {
    if (!host.isConnected) return;
    const months = report.months;
    const { svg, width, height } = chartFrame(host, 300);
    svg.setAttribute('aria-label', `Pemasukan, pengeluaran dan kumulatif selisih per bulan ${report.year}`);
    const margin = { top: 16, right: 16, bottom: 30, left: 64 };
    const values = months.flatMap((m) => [m.incomeIdr, m.expenseIdr, m.cumulativeNetIdr]);
    const ticks = niceTicks(Math.min(0, ...values), Math.max(1, ...values));
    const lo = ticks[0]; const hi = ticks[ticks.length - 1];
    const plotW = width - margin.left - margin.right;
    const plotH = height - margin.top - margin.bottom;
    const band = plotW / 12;
    const x = (i) => margin.left + band * i + band / 2;
    const y = (v) => margin.top + plotH - ((v - lo) / (hi - lo)) * plotH;
    yAxis(svg, ticks, y, margin.left, width - margin.right);

    const barW = Math.max(3, Math.min(22, band / 2 - 4));
    months.forEach((m, i) => {
      const bar = (value, cls, offset) => {
        if (value <= 0) return;
        svg.append(h('svg:rect', { class: cls, x: x(i) + offset, y: y(value), width: barW, height: Math.max(1, y(0) - y(value)), rx: 2 }));
      };
      bar(m.incomeIdr, 'mark-income', -barW - 1);
      bar(m.expenseIdr, 'mark-expense', 1);
      svg.append(h('svg:text', { class: 'axis-label', x: x(i), y: height - 10, 'text-anchor': 'middle' }, MONTH_SHORT[i]));
    });
    const lastFilled = months.reduce((last, m, i) => (m.transactionCount > 0 ? i : last), -1);
    if (lastFilled >= 0) {
      const points = months.slice(0, lastFilled + 1);
      svg.append(h('svg:path', { class: 'mark-line', d: points.map((m, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(m.cumulativeNetIdr).toFixed(1)}`).join(' ') }));
      points.forEach((m, i) => svg.append(h('svg:circle', { class: 'mark-point', r: 3, cx: x(i), cy: y(m.cumulativeNetIdr) })));
    }
    months.forEach((m, i) => {
      svg.append(h('svg:rect', {
        class: 'hit is-clickable', x: margin.left + band * i, y: margin.top, width: band, height: plotH,
        onclick: () => go(MONTH_KEYS[i]),
        onmousemove: (event) => showTooltip(event, [
          h('strong', { text: `${MONTH_LONG[i]} ${report.year}` }),
          `Pemasukan: ${rupiah(m.incomeIdr)}`,
          `Pengeluaran: ${rupiah(m.expenseIdr)}`,
          `Selisih: ${signedRupiah(m.netIdr)}`,
          `Kumulatif: ${signedRupiah(m.cumulativeNetIdr)}`,
          h('span', { class: 'muted', text: 'Klik untuk membuka bulan' })
        ]),
        onmouseleave: hideTooltip
      }));
    });
  }

  // ---------- Rekap ----------
  function renderRekap() {
    const head = h('div', { class: 'view-head' },
      h('div', null,
        h('h1', { text: `Rekap ${report.year}` }),
        h('p', { class: 'muted', text: 'Ringkasan tahunan, setara sheet Rekap — dihitung ulang dari semua tabel bulanan.' })));

    const kpis = h('div', { class: 'kpi-row' },
      kpi('Pemasukan setahun', rupiah(report.incomeIdr), { tone: 'income', typeLabel: INCOME }),
      kpi('Pengeluaran setahun', rupiah(report.expenseIdr), { tone: 'expense', typeLabel: EXPENSE }),
      kpi('Selisih', signedRupiah(report.netIdr), { tone: report.netIdr < 0 ? 'negative' : 'neutral' }),
      kpi('Saving rate', percent(report.savingRate), { hint: report.savingRate === null ? 'Kosong: belum ada pemasukan' : null }),
      kpi('Rata-rata pengeluaran', rupiah(report.averageExpensePerFilledMonthIdr), { hint: `per bulan terisi (${report.filledMonthCount} bulan)` }),
      kpi('Transaksi', fmt(report.transactionCount)));

    const chart = panel('Arus per bulan', h('div', { class: 'chart-host' }), {
      aside: legend([['income', 'Pemasukan'], ['expense', 'Pengeluaran'], ['line', 'Kumulatif selisih']])
    });
    const chartHost = chart.querySelector('.chart-host');
    resizeHandlers.push(() => drawYearChart(chartHost));

    return h('div', { class: 'view' },
      head, kpis, chart,
      panel('Rekap bulanan', monthlyTable()),
      panel('Pengeluaran per kategori × bulan', categoryMatrix(), { className: 'panel--table' }),
      h('div', { class: 'two-col' },
        panel('10 kategori terbesar', topCategories()),
        panel('Pengeluaran per metode pembayaran', paymentBreakdown())));
  }

  function monthlyTable() {
    const max = Math.max(1, ...report.months.map((m) => Math.max(m.incomeIdr, m.expenseIdr)));
    const rows = report.months.map((m, i) => h('tr', {
      class: ['is-clickable', m.transactionCount === 0 && 'is-dim'], tabindex: '0',
      onclick: () => go(MONTH_KEYS[i]),
      onkeydown: (e) => { if (e.key === 'Enter') go(MONTH_KEYS[i]); }
    },
    h('th', { scope: 'row', text: MONTH_LONG[i] }),
    h('td', { class: 'num is-income' }, fmt(m.incomeIdr), barCell(m.incomeIdr / max, 'income')),
    h('td', { class: 'num is-expense' }, fmt(m.expenseIdr), barCell(m.expenseIdr / max, 'expense')),
    h('td', { class: ['num', m.netIdr < 0 && 'is-negative'], text: signedRupiah(m.netIdr) }),
    h('td', { class: 'num', text: percent(m.savingRate) }),
    h('td', { class: 'num', text: fmt(m.transactionCount) }),
    h('td', { class: ['num', m.cumulativeNetIdr < 0 && 'is-negative'], text: signedRupiah(m.cumulativeNetIdr) })));
    return h('div', { class: 'table-scroll' }, h('table', { class: 'grid grid--report' },
      h('thead', null, h('tr', null, ['Bulan', 'Pemasukan', 'Pengeluaran', 'Selisih', 'Saving rate', 'Jml trx', 'Kumulatif']
        .map((label, i) => h('th', { class: i ? 'num' : null, scope: 'col', text: label })))),
      h('tbody', null, rows),
      h('tfoot', null,
        h('tr', null,
          h('th', { scope: 'row', text: 'TOTAL' }),
          h('th', { class: 'num is-income', text: fmt(report.incomeIdr) }),
          h('th', { class: 'num is-expense', text: fmt(report.expenseIdr) }),
          h('th', { class: ['num', report.netIdr < 0 && 'is-negative'], text: signedRupiah(report.netIdr) }),
          h('th', { class: 'num', text: percent(report.savingRate) }),
          h('th', { class: 'num', text: fmt(report.transactionCount) }),
          h('th')),
        h('tr', { class: 'is-note' },
          h('td', { colspan: '7', class: 'muted small', text: `Bulan terisi (pengeluaran > 0): ${report.filledMonthCount}. Rata-rata pengeluaran per bulan terisi: ${rupiah(report.averageExpensePerFilledMonthIdr)}.` })))));
  }

  const matrixState = { hideEmpty: true, sort: null };
  function categoryMatrix() {
    const host = h('div');
    const draw = () => {
      let rows = report.expenseMatrix.filter((row) => !matrixState.hideEmpty || row.totalIdr > 0);
      if (matrixState.sort) {
        const { key, dir } = matrixState.sort;
        const value = (row) => (key === 'name' ? row.category : key === 'total' ? row.totalIdr : key === 'avg' ? row.averagePerFilledMonthIdr
          : key === 'budget' ? (row.monthlyBudgetIdr ?? -1) : key === 'gap' ? (row.monthlyBudgetIdr ? row.averagePerFilledMonthIdr - row.monthlyBudgetIdr : -Infinity) : row.monthlyAmountsIdr[key]);
        rows = [...rows].sort((a, b) => {
          const va = value(a); const vb = value(b);
          const r = typeof va === 'number' ? va - vb : collator.compare(va, vb);
          return dir === 'desc' ? -r : r;
        });
      }
      const maxCell = Math.max(1, ...report.expenseMatrix.flatMap((row) => row.monthlyAmountsIdr));
      const header = (label, key, numeric = true) => {
        const sorted = matrixState.sort && matrixState.sort.key === key ? matrixState.sort.dir : null;
        return h('th', { class: [numeric && 'num', sorted && 'is-sorted'], scope: 'col' },
          h('button', {
            type: 'button', class: 'th-sort', onclick: () => {
              matrixState.sort = !sorted ? { key, dir: numeric ? 'desc' : 'asc' } : sorted === (numeric ? 'desc' : 'asc') ? { key, dir: numeric ? 'asc' : 'desc' } : null;
              draw();
            }
          }, label, h('span', { class: 'sort-mark', text: sorted === 'asc' ? '▲' : sorted === 'desc' ? '▼' : '' })));
      };
      const columnTotals = new Array(12).fill(0);
      report.expenseMatrix.forEach((row) => row.monthlyAmountsIdr.forEach((v, i) => { columnTotals[i] += v; }));
      const budgetTotal = report.expenseMatrix.reduce((sum, row) => sum + (row.monthlyBudgetIdr || 0), 0);

      const toggle = h('label', { class: 'toggle' },
        h('input', { type: 'checkbox', checked: matrixState.hideEmpty, onchange: (e) => { matrixState.hideEmpty = e.target.checked; draw(); } }),
        'Sembunyikan kategori tanpa pengeluaran');
      const table = h('table', { class: 'grid grid--matrix' },
        h('thead', null, h('tr', null,
          header('Kategori', 'name', false),
          MONTH_SHORT.map((label, i) => header(label, i)),
          header('Total', 'total'), header('Rata²/bln', 'avg'), header('Budget/bln', 'budget'), header('Rata² − budget', 'gap'))),
        h('tbody', null, rows.length === 0
          ? h('tr', null, h('td', { colspan: '17', class: 'tx-empty', text: 'Belum ada pengeluaran.' }))
          : rows.map((row) => {
            const gap = row.monthlyBudgetIdr ? row.averagePerFilledMonthIdr - row.monthlyBudgetIdr : null;
            return h('tr', null,
              h('th', { scope: 'row', class: 'sticky-col' }, row.category,
                row.inMaster ? null : h('span', { class: 'badge badge--warn', title: 'Kategori dipakai di transaksi Pengeluaran tetapi tidak terdaftar sebagai Pengeluaran di Master. Excel Rekap tidak menghitungnya di matriks.', text: 'di luar Master' })),
              row.monthlyAmountsIdr.map((value, i) => h('td', {
                class: ['num', 'heat', value === 0 && 'is-zero', row.monthlyBudgetIdr && value > row.monthlyBudgetIdr && 'is-over'],
                style: value > 0 ? { '--heat': (0.08 + 0.5 * (value / maxCell)).toFixed(3) } : null,
                title: `${row.category} · ${MONTH_LONG[i]}: ${rupiah(value)}${row.monthlyBudgetIdr ? ` (budget ${rupiah(row.monthlyBudgetIdr)})` : ''}`
              }, value === 0 ? '·' : compact(value))),
              h('td', { class: 'num strong', text: fmt(row.totalIdr) }),
              h('td', { class: 'num', text: fmt(row.averagePerFilledMonthIdr) }),
              h('td', { class: 'num muted', text: row.monthlyBudgetIdr ? fmt(row.monthlyBudgetIdr) : '—' }),
              h('td', { class: ['num', gap !== null && gap > 0 && 'is-over', gap !== null && gap <= 0 && 'is-under'], text: gap === null ? '—' : signedRupiah(gap) }));
          })),
        h('tfoot', null, h('tr', null,
          h('th', { scope: 'row', class: 'sticky-col', text: 'TOTAL' }),
          columnTotals.map((v) => h('th', { class: 'num', text: v === 0 ? '·' : compact(v), title: rupiah(v) })),
          h('th', { class: 'num', text: fmt(report.expenseMatrix.reduce((s, r) => s + r.totalIdr, 0)) }),
          h('th', { class: 'num', text: fmt(report.averageExpensePerFilledMonthIdr), title: 'Total pengeluaran ÷ bulan terisi' }),
          h('th', { class: 'num', text: budgetTotal ? fmt(budgetTotal) : '—' }),
          h('th'))));
      host.replaceChildren(
        h('div', { class: 'matrix-toolbar' }, toggle,
          h('span', { class: 'muted small', text: 'Rata² dibagi jumlah bulan terisi, sama seperti Excel. Sel merah: melebihi budget bulanan.' })),
        h('div', { class: 'table-scroll' }, table));
    };
    draw();
    return host;
  }

  function topCategories() {
    const top = report.expenseMatrix.filter((row) => row.totalIdr > 0)
      .sort((a, b) => b.totalIdr - a.totalIdr || collator.compare(a.category, b.category))
      .slice(0, 10);
    if (top.length === 0) return h('p', { class: 'muted', text: 'Belum ada pengeluaran.' });
    const max = top[0].totalIdr;
    return h('ol', { class: 'barlist' }, top.map((row) => h('li', null,
      h('div', { class: 'barlist-head' }, h('span', { text: row.category }), h('span', { class: 'num', text: fmt(row.totalIdr) })),
      barCell(row.totalIdr / max, 'expense'),
      h('div', { class: 'muted small', text: `${percent(report.expenseIdr ? row.totalIdr / report.expenseIdr : 0)} dari total pengeluaran` }))));
  }

  function paymentBreakdown() {
    const items = report.expenseByPaymentMethod;
    if (items.every((item) => item.amountIdr === 0)) return h('p', { class: 'muted', text: 'Belum ada pengeluaran.' });
    const max = Math.max(1, ...items.map((item) => item.amountIdr));
    return h('ol', { class: 'barlist' }, items.map((item) => h('li', null,
      h('div', { class: 'barlist-head' }, h('span', { class: item.paymentMethod ? null : 'muted', text: item.paymentMethod || '(pembayaran kosong)' }), h('span', { class: 'num', text: fmt(item.amountIdr) })),
      barCell(item.amountIdr / max, 'neutral'),
      h('div', { class: 'muted small', text: `${percent(report.expenseIdr ? item.amountIdr / report.expenseIdr : 0)} · ${item.transactionCount} transaksi` }))));
  }

  // ---------- all transactions ----------
  function renderAll() {
    return h('div', { class: 'view' },
      h('div', { class: 'view-head' }, h('div', null,
        h('h1', { text: `Semua transaksi ${report.year}` }),
        h('p', { class: 'muted', text: 'Gabungan 12 tabel bulanan. Gunakan ▾ di judul kolom untuk filter seperti AutoFilter Excel.' }))),
      panel('Tabel transaksi setahun', transactionTable('all', allRows, { showMonth: true }), { className: 'panel--table' }));
  }

  // ---------- master ----------
  function renderMaster() {
    const expense = report.categories.filter((c) => c.kind === EXPENSE);
    const other = report.categories.filter((c) => c.kind !== EXPENSE);
    const categoryTable = (items) => h('div', { class: 'table-scroll' }, h('table', { class: 'grid grid--report' },
      h('thead', null, h('tr', null, h('th', { scope: 'col', text: 'Kategori' }), h('th', { scope: 'col', text: 'Jenis' }), h('th', { scope: 'col', class: 'num', text: 'Budget / bulan' }))),
      h('tbody', null, items.map((c) => h('tr', null,
        h('td', { text: c.name }), h('td', null, typePill(c.kind)), h('td', { class: 'num', text: c.monthlyBudgetIdr ? fmt(c.monthlyBudgetIdr) : '—' }))))));
    return h('div', { class: 'view' },
      h('div', { class: 'view-head' }, h('div', null,
        h('h1', { text: 'Master' }),
        h('p', { class: 'muted', text: 'Daftar milik workbook: sumber dropdown kategori, tipe dan metode pembayaran. Ubah lewat Excel.' }))),
      h('div', { class: 'two-col' },
        panel(`Kategori pengeluaran (${expense.length})`, categoryTable(expense)),
        h('div', { class: 'side-stack' },
          panel(`Kategori lain (${other.length})`, other.length ? categoryTable(other) : h('p', { class: 'muted', text: 'Tidak ada.' })),
          panel('Tipe transaksi', h('div', { class: 'chips' }, report.transactionTypes.map(typePill))),
          panel('Metode pembayaran', h('div', { class: 'chips' }, report.paymentMethods.map((m) => h('span', { class: 'chip chip--static', text: m })))))));
  }

  // ---------- boot ----------
  const updated = document.getElementById('last-updated');
  if (updated) {
    const when = new Date(report.lastModifiedAtUtc);
    updated.textContent = `Workbook diperbarui ${when.toLocaleString('id-ID', { dateStyle: 'medium', timeStyle: 'short' })}`;
    updated.title = when.toISOString();
  }
  render();
})();

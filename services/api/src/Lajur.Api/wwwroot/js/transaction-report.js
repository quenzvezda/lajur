// Standalone, read-only reports of the current filtered/sorted table snapshot.
// All dependencies/fonts are served locally; transaction data never leaves this origin.
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'Mei', 'Jun', 'Jul', 'Agu', 'Sep', 'Okt', 'Nov', 'Des'];
export const reportDate = iso => {
  const [year, month, day] = iso.split('-');
  return `${day}-${MONTHS[Number(month) - 1]}-${year}`;
};
const money = value => 'Rp ' + BigInt(value).toLocaleString('id-ID');
const amount = row => BigInt(row.amountIdrText ?? String(row.amountIdr));
export function transactionReport(rows, { year, name, sourceUrl, now = new Date() }) {
  const dates = rows.map(row => row.date).sort();
  return {
    rows: rows.map(row => ({ ...row })),
    title: 'Laporan Transaksi',
    subtitle: `${name} · ${year}`,
    exportedAt: new Intl.DateTimeFormat('id-ID', { dateStyle: 'long', timeStyle: 'long' }).format(now),
    created: now,
    sourceUrl,
    count: rows.length,
    total: rows.reduce((sum, row) => sum + amount(row), 0n),
    range: dates.length ? `${reportDate(dates[0])} - ${reportDate(dates.at(-1))}` : '-',
    filename: `Lajur_${year}_${name.replace(/[^a-zA-Z0-9_-]/g, '_')}_laporan`
  };
}

const loaded = new Map();
function script(src) {
  if (!loaded.has(src)) loaded.set(src, new Promise((resolve, reject) => {
    const el = document.createElement('script');
    el.src = src; el.onload = resolve;
    el.onerror = () => { el.remove(); loaded.delete(src); reject(new Error('Pustaka ekspor belum berhasil dimuat. Coba lagi.')); };
    document.head.append(el);
  }));
  return loaded.get(src);
}
async function font(filename) {
  const response = await fetch(`/vendor/noto-sans/${filename}`, { cache: 'force-cache' });
  if (!response.ok) throw new Error('Font laporan belum berhasil dimuat. Coba lagi.');
  const bytes = new Uint8Array(await response.arrayBuffer());
  let binary = '';
  for (let i = 0; i < bytes.length; i += 8192) binary += String.fromCharCode(...bytes.subarray(i, i + 8192));
  return btoa(binary);
}
export async function exportPdf(model) {
  await script('/vendor/jspdf-4.2.1/jspdf.umd.min.js');
  await script('/vendor/jspdf-autotable-5.0.9/jspdf.plugin.autotable.min.js');
  const fonts = await Promise.all([font('NotoSans-Regular.ttf'), font('NotoSans-Bold.ttf')]);
  const doc = new window.jspdf.jsPDF({ orientation: 'landscape', unit: 'mm', format: 'a4', compress: true });
  ['normal', 'bold'].forEach((style, index) => {
    const name = `NotoSans-${style}.ttf`;
    doc.addFileToVFS(name, fonts[index]); doc.addFont(name, 'NotoSans', style);
  });
  doc.setProperties({ title: model.title, subject: model.subtitle, author: 'Lajur', creator: 'Lajur' });
  doc.setCreationDate(model.created);
  const navy = [23, 50, 77], teal = [0, 119, 112], muted = [92, 112, 133];
  const text = (value, x, y, size = 9, color = navy, bold = false) => {
    doc.setFont('NotoSans', bold ? 'bold' : 'normal'); doc.setFontSize(size); doc.setTextColor(...color);
    doc.text(Array.isArray(value) ? value : String(value), x, y);
  };
  doc.setFillColor(...navy); doc.rect(0, 0, 297, 2.5, 'F');
  text(model.title, 12, 16, 20, navy, true);
  text(model.subtitle, 12, 23, 9, muted);
  text(`Diekspor ${model.exportedAt}`, 12, 29, 8, muted);
  const blocks = [ ['JUMLAH TRANSAKSI', `${model.count} transaksi`, 63], ['TOTAL JUMLAH', money(model.total), 92], ['RENTANG TANGGAL', model.range, 110] ];
  let x = 12;
  for (const [label, value, width] of blocks) {
    doc.setFillColor(...(label === 'TOTAL JUMLAH' ? [231, 245, 242] : [245, 248, 251]));
    doc.roundedRect(x, 35, width, 20, 2, 2, 'F');
    text(label, x + 4, 42, 7, muted);
    text(value, x + 4, 50, 11, label === 'TOTAL JUMLAH' ? teal : navy, true);
    x += width + 4;
  }
  text('DAFTAR TRANSAKSI', 12, 63, 8, teal, true);
  doc.autoTable({
    startY: 67, margin: { left: 12, right: 12, top: 18, bottom: 19 },
    head: [['No.', 'Tanggal', 'Tipe', 'Pembayaran', 'Kategori', 'Jumlah (Rp)', 'Keterangan']],
    body: model.rows.map((row, index) => [index + 1, reportDate(row.date), row.type,
      row.paymentMethod || '-', row.category, amount(row).toLocaleString('id-ID'), row.description || '']),
    foot: [[{ content: 'TOTAL JUMLAH', colSpan: 5 }, model.total.toLocaleString('id-ID'), '']],
    showFoot: 'lastPage', rowPageBreak: 'avoid',
    styles: { font: 'NotoSans', fontSize: 8, cellPadding: 2.4, textColor: navy, lineColor: [222, 230, 237], lineWidth: { bottom: .15 }, overflow: 'linebreak' },
    headStyles: { fillColor: navy, textColor: 255, fontStyle: 'bold', fontSize: 7.5 },
    footStyles: { fillColor: [231, 245, 242], textColor: teal, fontStyle: 'bold' },
    alternateRowStyles: { fillColor: [245, 248, 251] },
    columnStyles: { 0: { cellWidth: 12 }, 1: { cellWidth: 30 }, 2: { cellWidth: 29 },
      3: { cellWidth: 32 }, 4: { cellWidth: 40 }, 5: { cellWidth: 43, halign: 'right' }, 6: { cellWidth: 87 } },
    didParseCell: data => { if (data.column.index === 5) data.cell.styles.halign = 'right'; },
    willDrawPage: data => { if (data.pageNumber > 1) text(`${model.title} · ${model.subtitle}`, 12, 11, 9, muted); }
  });
  const count = doc.getNumberOfPages();
  // URLs wrap above a fixed page-number line and remain clickable in the PDF.
  for (let page = 1; page <= count; page++) {
    doc.setPage(page);
    doc.setDrawColor(222, 230, 237); doc.line(12, 193, 285, 193);
    doc.setFont('NotoSans', 'normal'); doc.setFontSize(6.5);
    const source = doc.splitTextToSize(`Sumber: ${model.sourceUrl}`, 230);
    // Browser locations are normally short; reserve more footer space if this ever grows.
    text(source.slice(0, 2), 12, 198, 6.5, muted);
    doc.link(12, 194, 230, 9, { url: model.sourceUrl });
    text(`Halaman ${page} dari ${count}`, 256, 202, 7, muted);
  }
  doc.save(model.filename + '.pdf');
}

export async function exportExcel(model) {
  await script('/vendor/exceljs-4.4.0/exceljs.min.js');
  const workbook = new window.ExcelJS.Workbook();
  workbook.creator = 'Lajur'; workbook.created = model.created; workbook.modified = model.created;
  workbook.title = model.title; workbook.subject = model.subtitle;
  const sheet = workbook.addWorksheet('Laporan Transaksi', {
    views: [{ state: 'frozen', ySplit: 9, showGridLines: false }],
    pageSetup: { paperSize: 9, orientation: 'landscape', fitToPage: true, fitToWidth: 1, fitToHeight: 0,
      printTitlesRow: '9:9', margins: { left: .3, right: .3, top: .4, bottom: .5, header: .2, footer: .2 } },
    headerFooter: { oddHeader: '&LLajur&R' + model.title,
      oddFooter: '&L&8Sumber: ' + model.sourceUrl.replaceAll('&', '&&') + '&R&8Halaman &P dari &N' }
  });
  const navy = '17324D', teal = '007770', muted = '5C7085';
  sheet.columns = [{ width: 7 }, { width: 19 }, { width: 19 }, { width: 23 }, { width: 28 }, { width: 27 }, { width: 57 }];
  const merged = (range, value, size, color, bold = false) => {
    sheet.mergeCells(range);
    const cell = sheet.getCell(range.split(':')[0]); cell.value = value;
    cell.font = { name: 'Calibri', size, color: { argb: color }, bold };
    cell.alignment = { vertical: 'middle', wrapText: true };
  };
  merged('A1:G1', model.title, 23, navy, true); sheet.getRow(1).height = 38;
  merged('A2:G2', model.subtitle, 11, muted); sheet.getRow(2).height = 22;
  merged('A3:G3', `Diekspor ${model.exportedAt}`, 10, muted); sheet.getRow(3).height = 22;
  merged('A4:G4', `Sumber: ${model.sourceUrl}`, 10, muted); sheet.getRow(4).height = 30;
  for (const [labelRange, valueRange, label, value] of [
    ['A6:B6', 'A7:B7', 'JUMLAH TRANSAKSI', `${model.count} transaksi`],
    ['C6:D6', 'C7:D7', 'TOTAL JUMLAH', money(model.total)],
    ['E6:G6', 'E7:G7', 'RENTANG TANGGAL', model.range]
  ]) {
    merged(labelRange, label, 9, muted); merged(valueRange, value, 13, teal, true);
  }
  sheet.getRow(6).height = 22; sheet.getRow(7).height = 30;
  const header = sheet.getRow(9); header.values = ['No.', 'Tanggal', 'Tipe', 'Pembayaran', 'Kategori', 'Jumlah (Rp)', 'Keterangan']; header.height = 27;
  header.eachCell(cell => {
    cell.font = { name: 'Calibri', size: 10, bold: true, color: { argb: 'FFFFFF' } };
    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: navy } };
    cell.alignment = { vertical: 'middle', horizontal: cell.col === 6 ? 'right' : 'left' };
  });
  for (const [index, item] of model.rows.entries()) {
    const value = amount(item);
    // Excel stores only 15 significant decimal digits. Larger IDR stays exact text.
    const row = sheet.addRow([index + 1, new Date(item.date + 'T00:00:00Z'), item.type,
      item.paymentMethod || '', item.category, value <= 999999999999999n ? Number(value) : value.toString(), item.description || '']);
    row.getCell(2).numFmt = 'dd-"' + MONTHS[Number(item.date.slice(5, 7)) - 1] + '"-yyyy';
    row.getCell(6).numFmt = '#,##0';
    row.eachCell({ includeEmpty: true }, cell => {
      cell.font = { name: 'Calibri', size: 11, color: { argb: navy } };
      cell.alignment = { vertical: 'top', wrapText: true, horizontal: cell.col === 6 ? 'right' : 'left' };
      cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: index % 2 ? 'F4F7FA' : 'FFFFFF' } };
      cell.border = { bottom: { style: 'hair', color: { argb: 'DEE6ED' } } };
    });
    const lines = (value, width) => String(value || '').split('\n').reduce((sum, part) => sum + Math.max(1, Math.ceil(part.length / width)), 0);
    row.height = Math.min(409, Math.max(27, 16 * Math.max(lines(item.description, 50), lines(item.category, 24), lines(item.paymentMethod, 19), lines(item.type, 16)) + 10));
  }
  const lastData = 9 + model.rows.length;
  sheet.autoFilter = `A9:G${lastData}`;
  const total = sheet.addRow(['TOTAL JUMLAH', '', '', '', '', model.total <= 999999999999999n
    ? { formula: model.rows.length ? `SUM(F10:F${lastData})` : '0', result: Number(model.total) } : model.total.toString(), '']);
  sheet.mergeCells(`A${total.number}:E${total.number}`); total.height = 30;
  total.getCell(6).numFmt = '#,##0';
  total.eachCell({ includeEmpty: true }, cell => {
    cell.font = { name: 'Calibri', size: 11, bold: true, color: { argb: teal } };
    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'E7F5F2' } };
    cell.alignment = { vertical: 'middle', horizontal: cell.col === 6 ? 'right' : 'left' };
  });
  sheet.pageSetup.printArea = `A1:G${total.number}`;
  const bytes = await workbook.xlsx.writeBuffer();
  const url = URL.createObjectURL(new Blob([bytes], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
  const link = document.createElement('a'); link.href = url; link.download = model.filename + '.xlsx';
  document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}

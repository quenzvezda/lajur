export const PAYMENT_ICONS = [ ['cash', 'Tunai'], ['bank', 'Bank / transfer'], ['wallet', 'Dompet digital'], ['qr', 'QR'], ['shopping', 'Belanja'], ['card', 'Kartu'], ['other', 'Lainnya'] ];
const paths = {
  cash: ['M2 5h20v14H2Z', 'M2 9c3 0 4-1 4-4', 'M18 5c0 3 1 4 4 4', 'M2 15c3 0 4 1 4 4', 'M18 19c0-3 1-4 4-4', 'M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6'],
  bank: ['M3 9h18', 'M12 3l9 6H3Z', 'M5 10v8', 'M10 10v8', 'M14 10v8', 'M19 10v8', 'M3 21h18'],
  wallet: ['M3 7V5a2 2 0 0 1 2-2h14v4', 'M3 7h18v14H5a2 2 0 0 1-2-2Z', 'M16 11h5v6h-5Z'],
  qr: ['M3 3h6v6H3Z', 'M15 3h6v6h-6Z', 'M3 15h6v6H3Z', 'M13 13h4v4h4v4h-8Z', 'M21 13h.01'],
  shopping: ['M4 7h16l1 14H3Z', 'M8 7V6a4 4 0 0 1 8 0v1'],
  card: ['M2 5h20v14H2Z', 'M2 10h20', 'M6 15h4'],
  other: ['M4 5h16v14H4Z', 'M8 9h8', 'M8 13h5']
};
export function defaultPaymentIcon(name) {
  const text = name.toLocaleLowerCase('id-ID');
  return /cash|tunai/.test(text) ? 'cash' : /qris|qr/.test(text) ? 'qr' : /shopee|belanja/.test(text) ? 'shopping'
    : /bank|transfer/.test(text) ? 'bank' : /wallet|dompet|gopay|dana|ovo/.test(text) ? 'wallet' : /kartu|card/.test(text) ? 'card' : 'other';
}
export function paymentIcon(h, icon) {
  return h('svg:svg', { class: 'payment-icon', dataset: { icon: paths[icon] ? icon : 'other' }, viewBox: '0 0 24 24', width: 18, height: 18, fill: 'none', stroke: 'currentColor',
    'stroke-width': 1.7, 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true', focusable: 'false' }, (paths[icon] || paths.other).map(d => h('svg:path', { d })));
}
export function paymentLabel(h, name, icons = {}) {
  return h('span', { class: 'payment-label' }, paymentIcon(h, icons[name] || defaultPaymentIcon(name)), h('span', { text: name }));
}

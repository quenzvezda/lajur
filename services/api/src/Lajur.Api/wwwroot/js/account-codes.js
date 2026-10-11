'use strict';
(() => {
  const codes = document.querySelector('[data-recovery-codes]');
  if (!codes) return;
  const feedback = document.querySelector('[data-code-feedback]');
  let timer;
  function show(text) {
    feedback.textContent = text;
    clearTimeout(timer);
    timer = setTimeout(() => { feedback.textContent = ''; }, 5000);
  }
  document.querySelector('[data-copy-recovery]').addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(codes.value); show('Kode disalin. Simpan di tempat aman.'); }
    catch { codes.focus(); codes.select(); show('Tekan Ctrl+C atau pilih Salin pada perangkat Anda.'); }
  });
  document.querySelector('[data-download-recovery]').addEventListener('click', () => {
    const url = URL.createObjectURL(new Blob(['Kode pemulihan Lajur — simpan privat\n\n' + codes.value], {type: 'text/plain;charset=utf-8'}));
    const link = document.createElement('a');
    link.href = url;
    link.download = 'lajur-kode-pemulihan.txt';
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    show('File kode disiapkan. Jangan bagikan ke orang lain.');
  });
})();

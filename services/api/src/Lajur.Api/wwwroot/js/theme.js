(() => {
  const root = document.documentElement;
  let theme = 'system';
  try { theme = localStorage.getItem('lajur-theme') || 'system'; } catch { /* Theme still works for this page. */ }
  if (!['system', 'light', 'dark'].includes(theme)) theme = 'system';
  const apply = value => {
    theme = value;
    if (value === 'system') delete root.dataset.theme;
    else root.dataset.theme = value;
  };
  apply(theme);
  document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('[data-theme-picker]').forEach(picker => {
      picker.value = theme;
      picker.addEventListener('change', () => {
        apply(picker.value);
        window.dispatchEvent(new Event('lajur-theme-change'));
        try { localStorage.setItem('lajur-theme', theme); } catch { /* Nonpersistent preference. */ }
      });
    });
  });
})();

// IDR is an integer. Group display text without converting through floating point.
export function parseIdrAmount(text, allowZero = false) {
  const value = String(text).trim();
  if (!/^(?:[0-9]+|[0-9]{1,3}(?:\.[0-9]{3})+)$/.test(value)) return null;
  const amount = BigInt(value.replaceAll('.', ''));
  return amount >= (allowZero ? 0n : 1n) && amount <= 9223372036854775807n ? amount : null;
}

export function bindGroupedAmountInput(input, allowZero = false) {
  input.classList.add('input--money');
  input.addEventListener('beforeinput', event => {
    const start = input.selectionStart, end = input.selectionEnd;
    const backward = event.inputType === 'deleteContentBackward';
    const forward = event.inputType === 'deleteContentForward';
    if (start !== end || (!backward && !forward)) return;
    const separator = backward ? start - 1 : start;
    if (input.value[separator] !== '.') return;
    event.preventDefault();
    const digit = backward ? separator - 1 : separator + 1;
    input.value = input.value.slice(0, digit) + input.value.slice(digit + 1);
    const caret = backward ? digit : start;
    input.setSelectionRange(caret, caret);
    input.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: event.inputType }));
  });
  input.addEventListener('input', event => {
    input.setCustomValidity('');
    if (event.isComposing) return;
    const digitsBefore = input.value.slice(0, input.selectionStart).replace(/\D/g, '').length;
    const editing = event.inputType?.startsWith('deleteContent') || (event.inputType === 'insertText' && /^[0-9]+$/.test(event.data || ''));
    const source = editing && /^[0-9.]+$/.test(input.value) ? input.value.replaceAll('.', '') : input.value;
    const amount = parseIdrAmount(source, allowZero);
    if (amount === null) return;
    const formatted = amount.toLocaleString('id-ID');
    if (formatted === input.value) return;
    input.value = formatted;
    let position = 0, digits = 0;
    while (position < formatted.length && digits < digitsBefore) { if (/\d/.test(formatted[position])) digits++; position++; }
    input.setSelectionRange(position, position);
  });
}

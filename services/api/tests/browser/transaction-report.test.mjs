import test from 'node:test';
import assert from 'node:assert/strict';
import { transactionReport, reportDate } from '../../src/Lajur.Api/wwwroot/js/transaction-report.js';

test('report preserves exact IDR beyond Number precision and filtered row order', () => {
  const rows = [
    { date: '2026-10-24', amountIdrText: '9007199254740993', amountIdr: 9007199254740992, description: '=1+1' },
    { date: '2026-01-02', amountIdrText: '10001', amountIdr: 10001 }
  ];
  const report = transactionReport(rows, { year: 2026, name: 'Semua', sourceUrl: 'https://example.test/buku/2026#semua', now: new Date('2026-10-24T03:04:05Z') });
  assert.equal(report.total, 9007199254750994n);
  assert.equal(report.range, '02-Jan-2026 - 24-Okt-2026');
  assert.equal(report.count, 2);
  assert.deepEqual(report.rows.map(row => row.date), ['2026-10-24', '2026-01-02']);
  rows[0].description = 'changed after export started';
  assert.equal(report.rows[0].description, '=1+1');
  assert.equal(report.sourceUrl, 'https://example.test/buku/2026#semua');
});

test('report dates preserve the transaction-local date without timezone conversion', () => {
  assert.equal(reportDate('2026-01-01'), '01-Jan-2026');
  assert.equal(reportDate('2026-12-31'), '31-Des-2026');
});

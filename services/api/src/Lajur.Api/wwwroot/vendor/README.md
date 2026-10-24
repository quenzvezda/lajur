# Local report dependencies

These pinned browser distributions load on demand from this origin. No CDN or
remote inference/service receives report content. Each upstream license is kept
beside its asset. `report-libraries.json` records npm tarball provenance/integrity.

- jsPDF 4.2.1 — https://github.com/parallax/jsPDF — MIT
- jsPDF-AutoTable 5.0.9 — https://github.com/simonbengtsson/jsPDF-AutoTable — MIT
- ExcelJS 4.4.0 — https://github.com/exceljs/exceljs — MIT
- Noto Sans regular/bold — https://github.com/notofonts/noto-fonts — SIL OFL 1.1

Noto Sans is embedded in PDF exports for Indonesian punctuation and non-ASCII
text. The report modules use text/cell APIs, never HTML-to-PDF or user formulas.
Excel exports type user strings as literal strings. Larger than 15-digit IDR
amounts/totals are stored as exact decimal text to avoid Excel numeric rounding.

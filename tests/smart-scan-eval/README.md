# Smart Scan extraction evaluation data

`cases.synthetic.json` contains synthetic OCR text only. It validates the
benchmark plumbing and provides a repeatable smoke baseline; it is not large or
diverse enough to select a production model.

Private/sanitized real-world evaluation data belongs under the ignored
`private/smart-scan-eval/` tree and must use the same schema. Never commit real
receipts, OCR text containing personal information, account identifiers, or
workbook Master values.

Every case must label all evaluated fields. Use JSON `null` when the correct
behavior is to abstain, especially for unsupported category inference. Do not
omit difficult cases merely because a candidate performs poorly.

The committed minimarket receipt case is synthetic and includes a local numeric
date, discount, subtotal, tax, total, QRIS payment, and zero change. It protects
the deterministic `QRIS` to Master `Bank` convention and prevents receipt labels
from being treated as descriptions.

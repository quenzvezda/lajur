# ADR 0013 — Opt-in OpenTelemetry export from the API

Status: accepted for implementation on 2026-11-08.

## Context

The owner runs a self-hosted Elastic stack on the same VPS and wants Lajur's
API traces, logs and metrics there, with a private admin view and a separate
public status page for other applications as well. Lajur handles personal
finance data, and the self-hosted Elastic Basic license has no document- or
field-level security, so any boundary between "public" and "private" telemetry
must be the signal type and data stream, not per-field access control.

## Decision

The API exports OTLP over HTTP/protobuf only when
`Lajur:Telemetry:OtlpEndpoint` is set to an absolute `http`/`https` base URL.
Empty means no OpenTelemetry SDK is registered: local development, tests,
CI and the offline-first Android flow are unaffected. A malformed endpoint
fails startup so the deploy health gate rejects it; an unreachable collector
only drops telemetry and never fails requests.

Telemetry is self-hosted infrastructure, not inference, so it is compatible
with [ADR 0003](0003-local-only-ocr-and-llm.md). Do not point the exporter at a
third-party SaaS backend without a new ADR.

Signals:

- **Traces:** ASP.NET Core server spans (health endpoints excluded) and spans
  from the `Lajur.Workbooks` activity source around workbook read/mutate
  operations. Workbook spans carry only operation, workbook year, change source,
  replay flag and lock wait.
- **Metrics:** ASP.NET Core meters (hosting, Kestrel, routing, diagnostics,
  rate limiting, authentication, authorization), the .NET `System.Runtime` meter,
  and the `Lajur.Workbooks` meter: `lajur.workbook.operation.duration` and
  `lajur.workbook.lock.wait.duration` histograms (seconds) with dimensions
  `lajur.workbook.kind` (read/mutate), `lajur.workbook.operation`,
  `lajur.workbook.change_source` (android/web/excelimport/system, mutations only),
  `lajur.workbook.replayed` (idempotent retry matched an applied operation) and
  `error.type` (exception type name). Workbook year is deliberately not a metric
  dimension. These are the only Lajur signals considered safe for a public status
  page: their dimensions are method, route template, status code, runtime values
  and the workbook dimensions above. Metrics are exported with **delta**
  temporality (`OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE=delta` unless
  the host sets it), because the Elasticsearch exporter drops cumulative
  histograms.
- **Logs:** existing `ILogger` events at the configured level, following the
  rules in [BACKEND.md §10](../architecture/BACKEND.md#10-observability). They
  may contain internal user/transaction UUIDs and are private-only. EF Core
  command text (`Microsoft.EntityFrameworkCore.Database.Command`) is logged only
  at Warning and above.

Privacy rules enforced in the API before export:

- request/response headers, query strings, exception details on spans,
  request bodies and client addresses are not exported on spans
  (`client.address`, `client.port`, `network.peer.*`, `url.query`,
  `user_agent.original`, `enduser.*`, `user.*`, `http.*.header.*` are removed);
- span status descriptions use the exception type name, never its message;
- usernames, workbook file names/paths, amounts, descriptions and OCR text are
  never added as span attributes or metric dimensions.

Collector/backend responsibilities (outside this repository): route Lajur
traces/logs to private data streams only, grant any public reader access to
metrics data streams only, and repeat attribute scrubbing as defense in depth.

## Consequences

- Telemetry needs four NuGet packages (`OpenTelemetry.Extensions.Hosting`,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
  `OpenTelemetry.Instrumentation.AspNetCore`, and the in-memory exporter for
  tests only).
- Android telemetry is out of scope: an APK cannot hold an ingest secret, and
  the app is offline-first. Revisit only through the API if needed.
- Adding a span attribute, metric dimension or log field that could carry
  financial or account data requires updating this ADR.

# ADR 0003 — OCR and LLM inference are local-only

**Status:** Accepted

## Context

The owner explicitly wants to avoid dependence on third-party cloud AI services and avoid sending financial screenshots/receipts to external inference providers.

## Decision

OCR and semantic LLM extraction execute on-device.

The primary scan architecture is:

`OCR → text → local LLM → structured extraction → validator → human-reviewed draft`

Cloud inference is prohibited unless this ADR is explicitly superseded.

## Consequences

- privacy and offline capability improve;
- APK/model footprint and device memory become design constraints;
- model quality must be benchmarked on target hardware;
- final model is intentionally selected by evaluation rather than precommitted.

# Android architecture

## 1. Stack

Baseline:

- Kotlin;
- Jetpack Compose;
- Navigation Compose;
- Room;
- WorkManager;
- DataStore;
- AndroidX Biometric;
- Android Keystore;
- CameraX and/or system Photo Picker where appropriate;
- Retrofit + OkHttp + kotlinx serialization as a reasonable networking default;
- Hilt is acceptable for dependency injection if it keeps boundaries explicit.

Do not adopt a large framework merely to satisfy “clean architecture” aesthetics.

Phase 5 uses a small application-scoped `AppContainer` instead of a DI
framework, and a focused OkHttp/kotlinx-serialization API client instead of
Retrofit. These are implementation choices, not new architectural constraints;
introduce a framework only when a later slice demonstrates enough repeated
wiring to justify it.

The signed-in UI currently uses explicit `AppScreen` routes rather than a
Navigation Compose dependency. `MainTab` covers Home/History/Scan/Insight/Settings;
focused routes record their originating list where needed. A shared Back action
handles toolbar/system navigation and the Home two-press exit gate, resetting on
route changes/backgrounding. Child modal handlers and the workbook transfer busy
guard retain priority. Finishing the Activity does not clear the server session.

## 2. Suggested modules/packages

A single Gradle app module is acceptable for the first vertical slice. Split only when build/ownership boundaries justify it.

Logical package boundaries should still exist:

```text
ui/
domain/
data/local/
data/remote/
sync/
security/
scan/ocr/
scan/llm/
scan/extraction/
settings/
update/
```

## 3. State ownership

### Room

Stores:

- cached transactions;
- outbox operations;
- cached Master data metadata if useful.

### DataStore

Stores normal preferences:

- auto-lock timeout;
- theme;
- sync options;
- last/default payment preference;
- smart-scan options.
- automatic app-update checks, last attempt and cached verified release metadata.

### Keystore-backed encrypted storage

Stores sensitive local secrets such as refresh credentials/keys.

Never put raw refresh tokens in normal DataStore/plain SharedPreferences.

The Phase 5 session store encrypts the persisted refresh session with AES-GCM
using a non-exportable Android Keystore key. Access tokens remain memory-only,
and Android backup is disabled for the application.

## 4. Offline-first save

Manual save path:

```text
validate form
  ↓
Room transaction:
  - upsert LocalTransaction(PENDING)
  - add OutboxOperation
  ↓
UI reports saved
  ↓
enqueue WorkManager sync
```

The UI must not wait for network success.

## 5. Sync worker

Worker behavior:

- require connectivity;
- acquire a single logical sync execution for the account;
- refresh access token if needed;
- process outbox deterministically;
- stop/retry with WorkManager backoff for transient errors;
- distinguish authentication, conflict, validation, and transient failures;
- update Room state atomically with results.

Do not create aggressive polling. Connectivity-triggered/queued work is sufficient.

## 5a. Data/Workbook transfer

Settings → Data / Workbook lists server years and exposes create, annual XLSM
export, all-years ZIP export and confirmed XLSM import. Onboarding also offers
import. The app copies opaque bytes through Android's system document picker;
it never edits workbook internals. Bounded private staging avoids loading large
packages into memory. Export includes server content only and warns about local
pending changes. Import requires an explicit year and separate replacement
confirmation, rechecks all Room outbox operations under a shared sync/pull gate,
and excludes local mutations during replacement. Ordinary sync does not block
offline entry. Selected-year canonical cache/Master are invalidated before upload
to survive ambiguous responses/process death; failed attempts also need reload.
No automatic retry of a connection-failed import. See
`development/WORKBOOK_MENU_SMOKE.md` for acceptance steps and limitations.

## 5b. Application distribution updates

An application-scoped update controller owns one check/download/install-preparation
job. It uses a separate credential-free OkHttp client to read the official public
GitHub Release manifest. Release builds check on foreground at most daily;
manual checks are available before login and in Settings. A persisted opt-out
disables automatic checks. Finance entry, synchronization and app lock remain
independent. No forced update or automatic APK download is implemented.

Version selection and package integrity rules live in a pure Kotlin policy.
Downloads stream to bounded private cache and publish only after exact size and
SHA-256 checks. PackageManager checks package/version/minimum SDK/signatures;
both archive and installed certificates must match the pinned release key.
Cached files are revalidated on restart and before sharing to the Android
installer through FileProvider. Unknown-source permission and final installation
require explicit system UI actions. Updates do not migrate or clear finance data.
See `../operations/ANDROID_RELEASE.md` for URLs and distribution policy.

## 6. LLM lifecycle

Hard requirement: normal app startup and manual entry do not load the LLM.

Smart Scan:

```text
image acquired
   ↓
local OCR + evidence rules → validated review draft
   ↓ (only when the user requests AI suggestions)
lazy model load → semantic suggestions → merge and revalidate
```

Model manager may keep the model resident for a configurable short idle timeout (default proposal: 60 seconds), then release it.

Memory/latency must be benchmarked on the target POCO F6-class device before locking the final model/runtime.

## 7. Share intent

Accept relevant image MIME types.

Flow:

1. capture incoming URI permission safely;
2. pass through app-lock gate if needed;
3. load image with bounded dimensions;
4. run scan pipeline;
5. open draft;
6. release temporary resources.

Do not automatically save from an incoming image.

## 8. Sensitive-screen behavior

Default:

- hide sensitive preview in Recents;
- app screenshots disabled by default, user-configurable if feasible;
- incoming shared image is not displayed before unlock when app is locked.

## 9. App lock

Maintain foreground session timestamps independent from server token expiration.

Suggested logic:

- app entering foreground checks elapsed background/idle policy;
- if expired, route to unlock gate;
- successful BiometricPrompt refreshes local unlocked timestamp;
- background WorkManager does not route through UI lock.

Phase 6 implements this as a pure timeout state machine wrapped by a DataStore
settings adapter. The Activity reports monotonic foreground/background times;
the signed-in Compose tree is not created while locked. AndroidX
`BiometricPrompt` performs biometric/device-credential authentication, while
WorkManager and the Keystore-backed refresh session remain independent of that
foreground gate. Screen protection is enabled by default but user-configurable:
`FLAG_SECURE` protects screenshots and, on Android 13+, the Activity Recents
screenshot API explicitly disables task snapshots. Both controls change
together when the user toggles the setting. This is the intended design, but
the API 37 AVD acceptance check currently fails; see `development/KNOWN_ISSUES.md`
AND-001.
Incoming image shares retain only an in-memory URI until unlock; image decoding
starts in Phase 7.

Phase 7 turns Gallery, system Camera, and image Share Intents into a single
in-memory Smart Scan request. A bounded preprocessor accepts granted
`content://` URIs, validates size/dimensions, downsamples to a 2048-pixel edge,
and applies EXIF orientation. A project-owned generic pipeline invokes the
bundled local OCR engine and releases bitmap memory before presenting OCR text.
Camera captures live only in private cache and are deleted after use. The Phase
7 result deliberately leaves financial fields missing; semantic extraction and
confidence assignment belong to Phase 8.

Phase 8B adds a local extraction module around a pinned LiteRT-LM Android
adapter. Since the 2026-10-03 UX iteration, OCR alone produces a review draft.
An explicit AI action acquires a model lease, performs structured extraction,
merges supported evidence, validates against the latest Master, and releases
the lease. The model remains resident for at most 60 idle seconds after the
last request. Missing/incompatible models or malformed output never fail OCR
or manual entry; the evidence draft remains usable.

An in-memory scan review session owns the optional job and immutable result.
Cancellation, image replacement, discard, or entry into the editor invalidates
late AI results. Native synchronous inference may finish before its resource
cleanup; cancellation does not promise an immediate native interrupt. Human
editor state is never updated by a late model response.

The model is not loaded by application startup, login, Home, image selection,
OCR-only review, manual entry, or sync. No model artifact is bundled or committed
while candidate benchmarking is open.

## 10. UI style

Minimal but not visually dead:

- strong typographic hierarchy;
- clear spacing;
- Material components where helpful;
- avoid excessive cards, gradients, decorative charts;
- prioritize one-handed entry;
- amount input and primary actions should be easy to reach.

The visual system can be iterated after a functional prototype.

The Lajur identity uses a teal tile with a white L and mint ledger strokes. The
same source produces adaptive/monochrome launcher layers, raster density icons,
and the logo shown on Login and Unlock. See `../product/branding/README.md` for
editable sources, exports, palette, and visual smoke checks.

The transaction editor uses integer-IDR input with visual thousands grouping,
a calendar date picker, field-width anchored menus for type/payment, and a
searchable category bottom sheet constrained to the selected type's Master
values. Missing scan dates/types stay empty rather than inheriting manual-entry
defaults. Validation blocks missing required, out-of-Master, non-integer, overflow, and
invalid date values, including cross-year edits of synchronized transactions.

Payment is optional for manual and scan-assisted entry, with an explicit clear
action and neutral missing-value text. Unknown nonblank methods are still
rejected. Home displays a non-blocking completeness reminder based on the full
cached selected-month list, not the search results. Its review filter composes
with text search and excludes deleted rows. Missing type/category/amount/date
can also be flagged defensively, but saving these fields empty remains invalid.
Neither reminders nor blank payments alter sync status or monthly totals.

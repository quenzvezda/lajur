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

### Keystore-backed encrypted storage

Stores sensitive local secrets such as refresh credentials/keys.

Never put raw refresh tokens in normal DataStore/plain SharedPreferences.

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

## 6. LLM lifecycle

Hard requirement: normal app startup and manual entry do not load the LLM.

Smart Scan:

```text
image acquired
   ├── start OCR
   └── start lazy model load
        ↓
extraction
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

## 10. UI style

Minimal but not visually dead:

- strong typographic hierarchy;
- clear spacing;
- Material components where helpful;
- avoid excessive cards, gradients, decorative charts;
- prioritize one-handed entry;
- amount input and primary actions should be easy to reach.

The visual system can be iterated after a functional prototype.

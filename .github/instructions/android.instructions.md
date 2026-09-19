---
applyTo: "apps/android/**"
---

# Android-specific instructions

- Use Kotlin and Jetpack Compose.
- Maintain offline-first semantics. A successful local save must not require network connectivity.
- Room is the local cache/outbox; it is not canonical after synchronization.
- WorkManager performs deferred sync.
- Do not initialize/load the local LLM during normal application startup or manual-entry flows.
- OCR and LLM inference must stay on-device.
- Receipt images are not uploaded to the server by default.
- App lock and server authentication are separate concerns. Background sync must not require a biometric prompt.
- Use `BiometricPrompt` for foreground unlock and Android Keystore-backed encryption for sensitive local credentials.
- Avoid adding UI screens or navigation structure not required by `docs/product/UX_FLOWS.md`.
- Prefer JVM/domain tests over brittle UI automation. UI E2E is not a v1 CI requirement.

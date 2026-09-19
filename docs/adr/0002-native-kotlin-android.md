# ADR 0002 — Native Kotlin Android

**Status:** Accepted

## Context

Core requirements depend heavily on Android-specific capabilities: Camera/Photo Picker, Share Intent, WorkManager, Room, BiometricPrompt, Keystore, and local edge inference.

The owner does not require a full-.NET stack.

## Decision

Build Android natively with Kotlin + Jetpack Compose.

Backend remains C#/ASP.NET Core.

## Consequences

- Android integration follows the platform's primary ecosystem.
- The project uses two main languages, which is acceptable.
- Backend still provides substantial .NET/C# work.

# External research notes

These notes explain why this documentation package is shaped for current agentic coding workflows. They are references, not runtime dependencies.

Research date: **2026-09-19**

## Agent repository guidance

OpenAI has described an approach where a short `AGENTS.md` acts as a map while structured `docs/` is the repository system of record, including design docs and execution plans. This package follows that approach instead of one enormous instruction file.

- https://openai.com/index/harness-engineering/
- https://openai.com/business/guides-and-resources/how-openai-uses-codex/

GitHub Copilot documentation also recognizes `AGENTS.md`, repository-wide `.github/copilot-instructions.md`, and path-specific `.github/instructions/*.instructions.md`, so this package includes compatible lightweight instructions.

- https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/add-custom-instructions/add-repository-instructions
- https://docs.github.com/en/copilot/reference/custom-instructions-support

## Backend runtime

As of the research date, .NET 10 is the active LTS release.

- https://dotnet.microsoft.com/en-us/platform/support/policy
- https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview

## FunctionGemma

FunctionGemma is a specialized Gemma 3 270M function-calling model. Google documents it as a base intended for task-specific fine-tuning and demonstrates on-device mobile deployment. It is therefore an attractive candidate for structured transaction extraction, but the project deliberately requires real-device evaluation before selection.

- https://ai.google.dev/gemma/docs/functiongemma
- https://ai.google.dev/gemma/docs/functiongemma/model_card
- https://ai.google.dev/gemma/docs/mobile-actions

## Android biometric/preferences

Android provides system BiometricPrompt APIs for biometric/device-credential authentication, and Jetpack DataStore is the preferred lightweight preference store. Exact library versions should be selected at implementation time from stable releases.

- https://developer.android.com/identity/sign-in/biometric-auth
- https://developer.android.com/topic/libraries/architecture/datastore

## Important qualification

No external source replaces the project's product requirements. When a framework/library changes, preserve the behavioral contracts in this repository and update implementation/version details through normal review.

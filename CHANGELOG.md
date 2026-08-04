# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]
### Changed
- `GenAI` now treats its `IAIExecutor` as an exclusively owned per-instance dependency.
- `SetExecutor` synchronously stops the previous executor and switches only after successful cleanup; failures preserve the old references, but executor side effects cannot be rolled back.
- Migration: create a separate executor for each `GenAI`; sharing one executor instance is discouraged because lifecycle operations can stop resources used by another `GenAI`.
- Generation and setup-initialization cancellation now propagate `OperationCanceledException`; the real-time request deadline is independent of Unity time scale, covers initialization/retry, propagates `TimeoutException`, and stops the active Executor on a best-effort basis.
- Executor failures are retried up to three times and then propagate `GenAIExecutionException` with the final cause in `InnerException`; initialization retry no longer depends on matching symbols in generated text.
- Migration: callers that previously inspected emoji-prefixed error strings should catch the typed exceptions instead.

## [3.1.1] - 2026-04-26
### Changed
- Updated the AISetup package and fixed an issue where scenes were not included.

## [3.1.0] - 2026-04-25
### Added
- Support for setting default arguments in the Executor
- Detection for mismatched AI configurations in the Executor
- Separate configuration options for Ollama-specific models and other models
- Retry logic for the generation process (up to 3 attempts)
- Behavior to apply default arguments on the `Executer` side when generating `GenAIConfig` with default settings

## [3.0.0] - 2026-03-14
### Added
- SampleGame #02: GuessTopicGame

### Changed
- Fixed an issue where some Executors would not restart when their process had already terminated

## [2.4.0] - 2026-03-13
### Added
- SampleGame #01: AI NPC Roleplay Chat
- Built-in executors for common LLM runtimes:
  - `OllamaHTTPExecutor`
  - `LlamaCliExecutor` (llama.cpp CLI)
  - `LlamaHTTPExecutor` (llama.cpp server)
- ScriptableObject-based framework configuration system
- Config Editor UI for managing AIDrivenFW settings
- Model metadata support (download URLs, filters, file paths)
- Unity Tools menu integration (`Tools → AIDrivenFW`)

### Changed
- Improved configuration workflow and Editor UI organization
- Refactored runtime execution layer with a unified Executor architecture

## [2.3.1] - 2026-03-09

### Added
- AI Setup Wizard for easier local AI configuration
- Model recommendation based on detected GPU memory
- Light / Balanced / Powerful model presets

### Changed
- Improved onboarding with a step-based AI setup interface

## [2.3.0] - 2026-03-08
### Added
- macOS support (Apple Silicon confirmed)
- Built-in file browser
- Glob-based file filtering

### Changed
- Updated AI Setup UI for improved file selection workflow

### Changed
- Removed StandaloneFileBrowser dependency

## [2.2.0] - 2026-03-03
### Added
- AISetup now supports English and Japanese.
- Automatic language detection based on system settings.
- Optional streaming mode for incremental output updates.
- Streaming `onUpdate` callback support in `Generate()`.

### Changed
- `IsPrepared()` now accepts an optional `GenAI` instance to prevent duplicate LLM initialization.

### Improved
- Added foundation for UI text localization system.
- Updated Example scene to align with the latest framework architecture.

## [2.1.3] - 2026-02-27
### Changed
- Documentation: Overall review and fixes to README / README_ja.md
  - Corrected several inaccuracies in installation, dependencies, and usage sections
  - Improved clarity and consistency across the document
### Notes
No code changes

## [2.1.2] - 2026-02-26
### Added
- Integration test suite (Initialization / Generate flow / End-to-End)
### Changed
- Stability and reliability improvements
- Minor internal refactoring
### Notes
Integration tests require a valid local LLM environment.

## [2.1.1] - 2026-02-25
### Changed
- Minor improvements and stability fixes

## [2.0.0] - 2026-02-19
### Added
- Swappable Executor architecture
- API and Core assembly separation

### Changed
- Improved configuration handling

## [1.0.0] - 2026-01-25
### Added
- Basic local LLM integration via llama.cpp

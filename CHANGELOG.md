# Changelog

All notable changes to this project will be documented in this file.

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
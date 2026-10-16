# Player setup

The optional `AIDrivenSetup` scene supports English and Japanese, with one purpose per slide: a brief introduction, selection review and consent, preparation, then completion. Model choice and custom files have their own optional slides. The review presents approximate download sizes, sources, separate model/runtime license links and an unchecked consent box. Opening the scene does not download software or models, launch a process or save consent. It only reads local settings and asks the loopback Ollama service for installed model metadata.

The progress bar represents the whole preparation: runtime 0–25%, model acquisition 25–85%, real generation and saving 85–100%. It accumulates distinct model-layer bytes, never moves backwards during an attempt and reaches 100% only after successful verification and commit. It is a work estimate, not a remaining-time prediction. Skipped steps advance immediately. Only a short fade/translation is animated; Reduce motion disables it.

The steady-state palette is checked using the [WCAG relative-luminance formula](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html): primary text/button 7.04:1, secondary text 8.31:1, disabled button 7.39:1, link hover 6.04:1 and progress fill 5.39:1. These are specific color checks, not an overall accessibility certification. Run `python Tools/check_setup_contrast.py`; the Windows smoke test also checks resolved primary/disabled/language colors.

After agreement, setup prepares the runtime, obtains the selected model, generates a short real greeting, then atomically saves verified settings in the game's writable `persistentDataPath`. An additive setup scene returns to the game automatically. `AIDrivenInitializer.Initialize()` returns `false` when the player declines; the game should offer its own retry, exit or non-AI experience. A standalone wizard displays completion without quitting the application.

## Models and licenses

The easy-start default is **Qwen3 0.6B**, approximately 523 MB. Players can choose 1.7B (about 1.4 GB), 4B (about 2.5 GB), another local Ollama text model or existing llama-server/GGUF files. Larger models use more memory and may be slower. The default favors a small download and leaving resources for the game; it is not a promise of conversation quality or compatibility with every PC.

The default models' Apache-2.0 terms were checked against [Qwen's 0.6B license](https://huggingface.co/Qwen/Qwen3-0.6B/blob/main/LICENSE), [1.7B license](https://huggingface.co/Qwen/Qwen3-1.7B/blob/main/LICENSE), [4B license](https://huggingface.co/Qwen/Qwen3-4B/blob/main/LICENSE) and the [Ollama model listing](https://ollama.com/library/qwen3:0.6b). This catalog does not replace license review for a game's distribution or a custom model. Model weights and runtimes are not included in this UnityPackage. Players obtain them from upstream. Preserve the notices included in acquired archives. The runtime, its dependencies and the model have separate terms. Custom models require an explicit HTTPS license link; setup does not certify arbitrary model licenses.

The UI uses simple code-defined shapes and fonts already installed on the operating system. No new third-party artwork or proprietary font file is redistributed. Japanese requires an installed Japanese-capable system font (for example Yu Gothic UI, Meiryo, Hiragino Sans or Noto Sans CJK JP).

## Runtime acquisition and network use

An existing local Ollama service is reused without taking ownership of it. If needed, an installed executable is started. On **Windows x64**, a missing runtime is acquired from the pinned official GitHub release below. Its size and SHA-256 must match before it is extracted to an attempt-specific staging directory and atomically moved into place. No installer, administrator access or global PATH change is required. OS security or firewall confirmation may still be necessary.

| Item | Value |
| --- | --- |
| Runtime | Ollama v0.40.5, Windows amd64 ZIP |
| Source | [official release](https://github.com/ollama/ollama/releases/tag/v0.40.5) |
| Archive bytes | 1,471,226,485 |
| SHA-256 | `5134ce7e3bc8e94d0494721c799ec293b429450d0c52461b0ee3647dd3bb4bb7` |
| Runtime terms | [upstream license](https://github.com/ollama/ollama/blob/v0.40.5/LICENSE) |
| Metadata verification | 2026-10-16, GitHub Releases API |

Allow archive size plus 5 GiB for runtime preparation and at least model size plus 1 GiB for models. Checks are conservative, not guarantees against a disk filling during setup. A user-managed Ollama service may use a model directory that differs from the launching game's environment; configure `OLLAMA_MODELS` consistently. Portable Ollama models use the game's `persistentDataPath/AIDrivenFramework/OllamaModels` directory. macOS, Linux and Windows ARM can use an already installed local Ollama service; when absent, the UI directs the player to [Ollama's download page](https://ollama.com/download) and explains possible OS confirmation. Automatic acquisition on those platforms remains outside this implementation.

Model downloads are requested through the local Ollama API and use its registry/CDN. AI prompts are sent to `127.0.0.1`; cloud model tags and remote models are rejected. This describes the local AI path, not other networking features of the game. License links open the player's browser explicitly.

## Cancellation, recovery and migration

- **Not now:** does not save consent or report success. The additive scene closes and initialization returns false.
- **Pause:** cancels the current request. Ollama keeps completed model parts for a later pull. An incomplete runtime archive/staging directory is removed, so runtime downloads restart.
- **Restart:** the attempt journal restores the selection. The player reviews and agrees again before work resumes.
- **Failure:** space, runtime, network, model and generation failures have recoverable guidance. No ready settings are committed before generation succeeds; an older working setup survives.
- **Prepared:** verified settings are restored and a real readiness generation runs before skipping setup. Old settings without a consent receipt open setup once for review. Legacy settings in the game's Data folder remain readable; new settings are written to `persistentDataPath`.
- **Files:** existing GGUF/runtime paths are retained; do not move those files after setup. No destructive copying or deletion of player-provided files occurs.

## Source and package

Canonical optional scene assets are under `src/AIDrivenFramework/Samples~/AISetup`. The scene uses `PlayerSetupScreen` and a resource stylesheet/theme, replacing the old wizard. Existing helper GUIDs are retained for import compatibility; the legacy manager is a passive facade and its old acquisition flow is removed. Rebuild the distributed artifact with:

```powershell
python Tools/package_aisetup.py
```

The exporter uses stable Unity GUIDs and fixed archive timestamps. Repeating it without source changes must yield identical bytes. Import the resulting `AIDriven_AISetup.unitypackage` into a clean Unity 6000 project with this framework, UniTask, LitMotion, UI Toolkit and uGUI enabled. Add the scene to Build Settings. Optional installation through Tools > AIDrivenFW > Optional Packages performs scene registration.

## Validation procedure

1. Run all `AIDrivenFW.Tests.Unit` EditMode tests. `PlayerSetupFlowTests` covers the consent gate, ordered acquisition/verification/save, selection snapshots, pause/retry/restart, failure preservation, model/terms validation, disk preflight, atomic writes and hostile archive paths/hash mismatch.
2. Import the rebuilt UnityPackage into a fresh validation project. Confirm the scene has a `PlayerSetupScreen`, all scripts compile and the stylesheet/theme load without missing references.
3. Build a Windows x64 Player with a small game scene calling `AIDrivenInitializer.Initialize()` and the setup scene in Build Settings. Start with no verified settings. Before agreement, check that no runtime/model acquisition occurs and the primary button is disabled.
4. Switch English/Japanese without reloading. Select each model; check name/size, terms links and consent reset. Check keyboard navigation, the reduce-motion toggle, 1440×900 and 900×700 layouts and scrolling to all controls.
5. Decline setup. Assert initialization returns false and the game scene remains. Start again, agree with a local Ollama model installed, and confirm real generation, saved consent, automatic scene unload and a true result. Initialize again; confirm readiness generation and scene skip.
6. Pause a pull and resume it, restart the game after a pause, disconnect the network, enter a nonexistent model, exhaust free space, deny execution permissions and select an unsupported model. Each must present recovery guidance without replacing older ready settings.
7. In an isolated Windows x64 environment without Ollama, agree and check the pinned archive download, hash verification, traversal-safe staging, runtime launch, model pull, generation and next-launch restoration. Separately check explicit installation guidance on other operating systems. These platform/install checks are distinct from the installed-service path.

For a repeatable isolated Windows validation host, with a running Ollama service and an installed `qwen3:0.6b` model:

```powershell
python Tools/package_aisetup.py
python Tools/check_setup_contrast.py
./Tools/UnityValidation/run.ps1 -Unity 'C:/Unity/6000.3.10f1/Editor/Unity.exe'
```

The script refuses to reset an existing directory, imports the distribution artifact, runs EditMode tests, copies the reviewable `.cs.txt` QA sources into the host, builds a Windows Player and exercises slide visibility, language, actual resolved contrast, model changes, narrow layout, refusal, real generation, overall progress, return and ready skip. It restores validation settings and language/motion preferences after the run. Logs, results and rendered proof frames stay in the isolated host. The renderer uses a 24-bit depth/stencil RenderTexture so rounded clipping is tested correctly without showing a test window.

Executed results and remaining platform limitations are recorded in the pull request. Do not infer a successful clean-machine runtime installation from fixture tests or an installed-service run.

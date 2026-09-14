# Totem native carousel — durable acceptance evidence

This is the versioned evidence package for
[`2026-09-13-totem-native-carousel-acceptance.md`](../../2026-09-13-totem-native-carousel-acceptance.md).
It preserves the audit artifacts that were originally generated in a disposable
SDD workspace, without copying that workspace or its unrelated reports.

## Identity and execution context

| Item | Value |
| --- | --- |
| Candidate code SHA | `c76c2be20edf5d3d6b122b76a9ba9c38de92c28a` |
| Candidate subject | `fix(totem): enlarge responsive coverflow stage` |
| Local branch at evidence collection | `codex/reception-backend` |
| Local runtime | Vite at `http://127.0.0.1:5174/totem/profissionais` for the handoff smoke; local browser fixtures at `http://127.0.0.1:5197/totem/profissionais` for layout/fixture captures |
| Evidence type | Local browser automation, deterministic session-local network fixtures, and local command output |

## Limits

These artifacts are not physical-device acceptance. No device, real touch,
published URL, staging instance, deployment, or production backend was used.
The effective-200% evidence is a DevTools 960×540/DPR-2 effective-viewport
equivalent; it is not literal Chrome UI zoom. The original Task 2 RED stdout
was not retained. The acceptance matrix remains **ACEITE FÍSICO PENDENTE**.

The original raw keyboard-layout log is deliberately excluded because it
contained one transient, unsettled Home observation. The preserved
[`scripts/check-layout.ps1`](scripts/check-layout.ps1) requires the expected
identity and settled center before recording output; the reliable observations
and the exclusion rationale are in
[`logs/keyboard-layout-summary.md`](logs/keyboard-layout-summary.md).

## Inventory

| Purpose | Retained files |
| --- | --- |
| Required visual comparisons | Four before/after pairs in [`screenshots/`](screenshots/): 1920×1080, 1366×768, 768×1024, and 390×844; plus `after-1366x768-full.png` showing the lower controls below the initial fold |
| Layout metrics | [`logs/final-metrics.log`](logs/final-metrics.log) and [`logs/keyboard-layout-summary.md`](logs/keyboard-layout-summary.md) |
| 0/1/2/many professionals | [`fixtures/professionals-fixture.json`](fixtures/professionals-fixture.json), [`scripts/check-fixtures.ps1`](scripts/check-fixtures.ps1), [`logs/fixture-checks.log`](logs/fixture-checks.log), and four `after-390x844-count-*.png` captures |
| Short mobile and reduced motion | [`screenshots/after-390x600-scrolled.png`](screenshots/after-390x600-scrolled.png) and [`screenshots/after-390x844-reduced-motion.png`](screenshots/after-390x844-reduced-motion.png) |
| Effective 200% viewport | [`scripts/check-effective-zoom.ps1`](scripts/check-effective-zoom.ps1), [`logs/effective-200-zoom-equivalent.log`](logs/effective-200-zoom-equivalent.log), and two `after-effective-200-*.png` captures |
| Local selection-to-handoff | [`logs/task-3-local-handoff-smoke.log`](logs/task-3-local-handoff-smoke.log) and [`screenshots/task-3-local-handoff-smoke.png`](screenshots/task-3-local-handoff-smoke.png) |
| Shared measurement logic | [`scripts/metrics.js`](scripts/metrics.js) |

The logs retain the commands and observed output from the candidate run. The
scripts use the package paths and are retained to reproduce the fixture,
keyboard/layout, and effective-viewport checks without relying on SDD files.

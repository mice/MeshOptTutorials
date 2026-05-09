# Skinned Mesh Simplification Execution Plan V1

## Purpose

This document is the execution-facing plan for the first version of skinned-mesh simplification work in this repository.

It is meant for handoff between agents. Keep this file current as tasks complete.

## Source Of Truth

Use these documents together:

- [SkinnedMeshSimplification_Workflow_V1.md](SkinnedMeshSimplification_Workflow_V1.md)
- [SkinnedMeshSimplification_Metrics_V1.md](SkinnedMeshSimplification_Metrics_V1.md)

Workflow defines the contract.
Metrics define how results are judged.
This file defines what is being executed now.

## Current Status

Completed or already established:

- V1 parameter scope is fixed to `reductionPercent`
- config path is fixed to `Assets/EditorConfig/SkinnedMeshSimplification.json`
- temp staging path is fixed to `Assets/raw_temp/models`
- trimmed FBX output path and naming rules are defined
- FBX rewrite path exists in `Assets/Editor/SkinnedMeshFbxExport.cs`
- config load and save now exist in `Assets/Editor/SkinnedMeshFbxExport.cs`
- external-source staging and cleanup now exist in the shared FBX export flow
- staged external FBX imports are forced readable before validation runs
- batch entry now exists at `MeshEditorUtils.Batch_SimplifySkinMeshToFbx`
- a minimal visual authoring window now exists behind `Assets/skin/SimplifyToFBX`
- the visual authoring window now shows side-by-side core metric comparisons for original vs simplified counts
- the visual authoring window now reports import-status and validation flags for source, preview, and current output assets
- the visual authoring window now also surfaces cached rest-pose, neutral-pose, and high-deformation validation signals for the current output asset
- the visual authoring window now also surfaces cached output comparison summaries for `jointRegionP95Error`, `globalP95Error`, `maxError`, and `screenSpaceDiff`, including worst-pose labels and representative-pose coverage
- Unity-side interactive validation of the current visual authoring flow has been completed and matched expected behavior
- trimmed FBX import behavior exists in `Assets/Editor/TrimFbxModelPostprocessor.cs`
- Unity batch execution has been validated end-to-end with the canonical `Anim_1325` sample entry
- a minimal batch regression wrapper now exists at `Tools/Validate-SkinnedMeshBatchExport.ps1`
- a config-wide batch regression suite now exists at `Tools/Validate-SkinnedMeshBatchExportSuite.ps1`
- a seed benchmark manifest now exists at `Assets/EditorConfig/SkinnedMeshBenchmarkSet_V1.json`
- a comparison result schema now exists at `Assets/EditorConfig/SkinnedMeshComparisonResultSchema_V1.json`
- a minimal comparison-result producer now exists at `Tools/New-SkinnedMeshComparisonResult.ps1`
- a partial comparison-metric updater now exists at `Tools/Update-SkinnedMeshComparisonMetrics.ps1`
- the visual preview path now guards against zero-sized editor layout rects before entering `PreviewRenderUtility`
- the batch export path now validates the imported trimmed FBX output for skinned renderer presence, bindposes, bone data, and final non-readable importer state
- the deeper imported-output batch validation now passes on the canonical `Anim_1325` sample entry, including the explicit import-validation log signal
- the canonical `Anim_1325` batch run now also passes an automated rest-pose bake sanity check between source and imported trimmed output
- the canonical `Anim_1325` batch run now also passes automated representative-pose bake sanity checks for both a neutral clip and a higher-deformation clip sampled onto source and trimmed output
- the config-wide batch regression suite now passes for both configured entries, `Anim_1325` and `Anim_1326`, and writes an aggregate machine-readable report
- the suite report now includes per-entry reduction and provenance metadata plus aggregate coverage counters for the current validation signals
- the current benchmark seed set now explicitly fixes the first two benchmark meshes, representative clip priorities, and current reduction percentages in a machine-readable manifest
- the comparison result contract is now fixed in a machine-readable schema that bridges current gate checks with later deformation metrics and artifact paths
- the current schema-shaped comparison result now exists at `Temp/skinned-mesh-comparison-result-v1.json`, populated from the current benchmark seed set and suite summary with gate data plus `jointRegionP95Error`, `globalP95Error`, `maxError`, and a fixed-view `screenSpaceDiff` for the current representative poses

Still open and blocking the V1 main flow:

- none on the current canonical V1 mainline path

Still open but not blocking V1 contract definition:

- comparison UI details
- benchmark set expansion beyond the current seed manifest
- deeper unit-consistency validation
- broader normal/tangent regression policy
- visual artifact producers plus later refinement of the current screen-space and joint-region heuristics against the comparison-result schema

## V1 Execution Gate

V1 should be treated as executable only when all of the following are true:

- section 1 is current and no longer conflicts with workflow or metrics
- section 2 is implemented end-to-end for load, save, stage, and cleanup behavior
- section 3 exposes only `reductionPercent` plus output-target editing
- section 4 can run the same config in batch without interactive editing
- section 5 can write or update a stable `_trim.fbx` output while preserving the FBX-backed import path
- section 6 provides enough validation to reject obviously broken output before manual acceptance

Section 7 remains deferred and is not part of the V1 execution gate.

Acceptance standard:

- prefer unit tests wherever the behavior can be tested without manual inspection
- save test result data where practical so the pipeline can compare runs over time
- CI test mode should produce machine-readable output for process checks
- manual visual inspection should be a fallback or supplementary check, not the primary acceptance path
- the current config-wide suite report is the first machine-readable stepping stone toward broader benchmark coverage, not the final benchmark harness

## Execution Plan

### 1. Freeze the V1 contract

Status:

- current for the canonical V1 mainline

Goal:

- prevent scope drift
- keep workflow, metrics, and execution aligned

Deliverables:

- workflow doc kept current
- execution plan doc kept current

Done when:

- the three docs agree on what is blocking V1 execution
- deferred work is marked consistently across the three docs

### 2. Implement config and staging flow

Status:

- partially implemented in code
- blocking

Goal:

- load and save the JSON config
- stage temporary model files under `Assets/raw_temp/models`
- clear temp files at startup or before a new run

Clarification:

- external source files live under the current default root `D:\incubationSpace\MeshOptTutorials\ART_SVN`
- staging copies those files into `Assets/raw_temp/models` so Unity can import them
- staged copies are temporary project assets, not canonical assets
- `Assets/raw_temp` is not source-controlled deliverable content
- cleanup removes staged copies and their Unity import artifacts only
- cleanup must not delete files from `ART_SVN`
- cleanup must not delete the final `_trim.fbx` output

Deliverables:

- config loader/saver
- staging cleanup logic
- provenance handling for external-source entries

Current implementation:

- `Assets/skin/SimplifyToFBX` now loads config, exports, and writes back the resolved config entry
- canonical sample config now exists at `Assets/EditorConfig/SkinnedMeshSimplification.json`
- external-source entries now stage into `Assets/raw_temp/models` with deterministic file names
- staged external FBX imports now enable read/write before skinned-mesh validation
- cleanup now runs before and after the shared export flow
- Unity batch validation has confirmed the staged import path can complete export and cleanup for the canonical sample entry

Done when:

- the config can be loaded from `Assets/EditorConfig/SkinnedMeshSimplification.json`, whether authored by hand or by the visual tool
- external-source entries can stage into `Assets/raw_temp/models` using a deterministic file name
- staging cleanup runs before a new run and after success or cancellation/failure where possible

Phase-1 allowance:

- for early test cases, the config may be hand-authored instead of written through the visual tool
- config write UI is therefore not a hard gate for the first visualization pass
- config field path semantics should still follow the canonical workflow contract even for hand-authored test entries

### 3. Implement visual parameter authoring

Status:

- implemented for the current V1 scope
- Unity-side interactive validation completed

Goal:

- expose only `reductionPercent` in V1 visual tooling
- show the original model and simplified model side by side
- show the core evaluation data that changes as the simplification changes
- allow the user to change `reductionPercent` and refresh the simplified result

Clarification:

- phase-1 visual preview does not need to compute the full benchmark comparison suite on every parameter change
- until the comparison harness exists, the live panel may show currently available non-benchmark indicators such as reduction ratio, vertex or triangle counts, import validity, and any available validation flags
- the current window may also show cached summaries from the existing partial comparison-metric producer for the current output asset, but it does not try to render the full benchmark evidence set on every parameter change

Deliverables:

- visual preview for two models
- live refresh of simplified output after `reductionPercent` changes
- core metric display for the current comparison
- no extra parameter scope beyond V1

Current implementation:

- `Assets/skin/SimplifyToFBX` now opens a dedicated editor window instead of exporting immediately
- the window edits only `reductionPercent` plus output-target routing via `outputPath`
- the window renders original and simplified meshes side by side using editor previews
- the window refreshes the simplified mesh when `reductionPercent` changes
- the window shows current core metrics as an original / simplified / delta comparison for vertices and triangles
- the window now surfaces validation flags for source-mesh validity, preview-mesh validity, output import presence, and current output-mesh validity
- the window now also surfaces cached rest-pose, neutral-pose, and high-deformation validation signals so the interactive path mirrors the current batch checks
- the window now also surfaces cached output comparison summaries for `jointRegionP95Error`, `globalP95Error`, `maxError`, and `screenSpaceDiff` so the interactive path can inspect the current partial benchmark result without rerunning the full suite
- the window can save the active config entry and invoke the existing trimmed-FBX export path
- the window now skips preview rendering when the editor layout reports an invalid preview rect size
- Unity-side interactive validation has confirmed the current window behavior matches the intended V1 flow for the current sample path

Done when:

- a user can change `reductionPercent` and see the simplified model update
- the original and simplified models remain visible together
- the displayed core metrics change when the simplified result changes
- the UI does not expose any extra simplification knobs beyond `reductionPercent`

### 4. Implement CLI execution

Status:

- implemented for the current V1 scope
- validated on the canonical sample entry

Goal:

- consume the same JSON config as the visual tool
- run simplification in batch mode
- produce the same output asset naming behavior

Clarification:

- the CLI entry is a non-interactive batch path
- it should load one `entryId` from the canonical config
- it should stage external-source inputs when needed
- it should not depend on the visual editor being open
- it should not introduce a separate config format or separate parameter model

Test entry split:

- editor test entry: interactive visual panel
- CI test entry: `bat`-driven test mode for automated runs

Authority rule:

- the authoritative execution surface is one Unity batch-mode entry
- any `bat` file is a wrapper for CI convenience, not a second execution contract

Deliverables:

- command-line entry path
- batch staging and cleanup
- config-driven reduction execution

Current implementation:

- Unity batch entry now exists at `MeshEditorUtils.Batch_SimplifySkinMeshToFbx`
- required argument: `-entryId <value>`
- optional argument: `-configPath <path>`
- canonical sample entry now targets `Assets/res/raw/chars/Anim_1325/Export/Anim_1325.FBX`
- the batch path uses the same config type, staging path, and `_trim.fbx` naming rules as the editor export flow
- direct Unity batch-mode execution has been validated end-to-end for the canonical sample entry

Done when:

- batch mode can load the same JSON config written by the visual path
- batch mode can process an entry without any editor interaction
- batch mode follows the same staging and output naming rules as the visual flow
- the minimum CLI argument contract is documented and stable enough for repeatable use

### 5. Keep FBX rewrite export working

Status:

- implemented for the current V1 scope
- validated by the current canonical export and reimport checks

Goal:

- write simplified output back to FBX
- preserve skeleton, weights, and importer-relevant data
- use the source FBX unit, not a fixed conversion target

Deliverables:

- stable `_trim.fbx` rewrite path
- unit handling derived from source FBX
- rig and skin data preserved through export

Current implementation:

- the export path writes back to a stable derived `_trim.fbx` asset path instead of inventing a new name on each run
- FBX unit handling is read from the source FBX before export instead of using a fixed conversion target
- the canonical batch regression now verifies that the imported trimmed FBX still contains the expected skinned renderer, mesh, bindposes, and bone-weight data after export

Done when:

- exporting to the same derived `_trim.fbx` path updates that file instead of switching names per run
- source FBX unit handling remains derived from the source asset rather than a fixed export unit
- the imported trimmed FBX still contains the expected rig, bindpose, and skinning data needed by Unity import

### 6. Add validation and regression checks

Status:

- implemented for the current V1 gate on the canonical sample path
- broader benchmark and comparison follow-up remains deferred

Goal:

- verify output imports correctly
- inspect unit mismatch and normal changes
- catch visible deformation regressions early

Deliverables:

- `Tools/Validate-SkinnedMeshBatchExport.ps1` as the minimum automated batch validation wrapper
- targeted import checks
- unit consistency check to be added later
- normal/tangent regression notes for known problem cases
- unit tests for deterministic behavior where practical
- saved metric/test artifacts for comparison between runs

Current implementation:

- `Tools/Validate-SkinnedMeshBatchExport.ps1` runs the authoritative Unity batch entry with explicit process wait semantics
- the harness loads the canonical JSON config, selects one `entryId`, and validates the existing batch export flow rather than defining a second execution contract
- the harness writes a machine-readable JSON result to `Temp/batch-skinned-export-result.json`
- the current pass/fail signals are: Unity exit code, success log line, refreshed `_trim.fbx` timestamp, `isReadable: 0` on the imported trimmed FBX, and empty `Assets/raw_temp/models`
- the batch export code now also validates that the imported trimmed FBX still exposes a skinned renderer, mesh, bindposes, and bone-weight data before reporting success
- the canonical `Anim_1325` batch run now passes both the export success log and the imported-output validation log in the same run
- the canonical `Anim_1325` batch run now also passes a rest-pose validation log based on baked source/output skinned meshes
- the canonical `Anim_1325` batch run now also passes representative-pose validation coverage for a neutral clip and a higher-deformation clip when those neighboring animation assets exist

Done when:

- the tool can fail fast on an output that does not reimport as a valid skinned FBX
- the tool records at least the minimal import and deformation checks defined in the metrics doc
- automated tests cover the non-visual parts of the flow as much as practical
- test data is saved in a format suitable for CI or later diffing
- full benchmark metrics remain optional until the comparison harness is scheduled

### 7. Defer comparison UI specifics

Goal:

- keep the first version focused
- avoid hard-coding a comparison UI before the core pipeline is stable

Deferred items:

- exact comparison layout
- frame stepping policy
- worst-frame review UI
- storage/export format for comparison results

## Handoff Rules

When another agent picks up this work:

- read the workflow doc first
- read the metrics doc next
- treat this file as the live task board
- do not re-open settled V1 scope unless a blocking bug requires it
- update status here when a task changes state

## Notes

The current focus is the FBX-backed skinned-mesh path.
Do not broaden this plan into general mesh tooling unless the user explicitly asks for that expansion.

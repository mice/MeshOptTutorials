# Skinned Mesh Simplification Workflow V1

## Purpose

This document defines the first-pass tool workflow for skinned-mesh simplification in this repository.

It separates parameter authoring from batch execution:

- the visual tool is used to set simplification parameters
- the CLI tool consumes the saved parameters and runs the process

This document only records the parts that are already decided.

## Workflow Modes

### Visual mode

Responsibilities:

- let the user inspect the target asset
- let the user configure simplification parameters
- let the user compare the original and simplified result
- let the user review the core evaluation data for the current result
- for the first visual pass, config save may remain manual
- full V1 execution readiness still expects the selected parameter set to be writable for CLI execution

V1 parameter scope:

- **reduction percentage only**

Output-target note:

- output target selection is routing and asset-ownership configuration, not an additional simplification parameter

Phase-1 visual scope:

- show the original model and simplified model at the same time
- allow editing only `reductionPercent`
- update the simplified model after `reductionPercent` changes
- update the displayed core evaluation data after the simplified model changes
- for the first visual pass, the displayed core evaluation data may be limited to currently available non-benchmark indicators such as reduction ratio, vertex or triangle counts, output import status, and any available validation flags
- use manually prepared config entries for early test cases

Not in scope for V1:

- multi-parameter tuning
- advanced comparison interaction design
- any final specification for the comparison UI

### CLI mode

Responsibilities:

- load the saved simplification parameters
- apply the same parameter set in batch
- run without interactive editing

The CLI must use the same parameter definition as the visual tool so that results are reproducible.

### Test entry points

V1 has two test entry points:

- editor test entry in the visual panel
- CI test entry through a `bat`-driven command path

The editor entry is for interactive checking.
The CI entry is for automated test mode.
The authoritative batch contract is still the underlying Unity batch-mode entry, not the wrapper script itself.

### CLI batch entry meaning

For V1, the CLI batch entry means:

- a non-interactive execution path
- reads the canonical JSON config
- resolves one `entryId`
- stages the external source if the entry points outside the project
- applies `reductionPercent`
- writes or updates the derived `_trim.fbx` output
- cleans up temporary staging files on exit

It does not mean:

- an editor menu action
- a manual UI workflow
- a separate parameter system
- a second source of truth for config data

### Minimum CLI contract

For V1, the CLI contract should be kept minimal and stable:

- config path defaults to `Assets/EditorConfig/SkinnedMeshSimplification.json`
- the target entry is selected by `entryId`
- `outputPath` comes from config unless an explicit override mode is added later
- the same entry must resolve to the same staging and derived output behavior as the visual path

## Shared Parameter Contract

V1 will use a single simplification parameter:

- `reductionPercent`

This value is the only first-version knob exposed by the visual workflow and the only parameter consumed by the CLI workflow.

## Configuration And Temporary Storage V1

### Configuration location

Tool configuration is stored as JSON under:

- `Assets/EditorConfig`

Recommended canonical file:

- `Assets/EditorConfig/SkinnedMeshSimplification.json`

### Temporary staging location

Temporary imported source files are staged under:

- `Assets/raw_temp/models`

### External-source staging meaning

For V1, "staging" means:

- read the external source from the configured external-source root
- copy it into `Assets/raw_temp/models` for Unity preview and simplification
- keep the staged file name identical to the source file name
- treat the staged copy as temporary only

For the current default setup:

- external-source root: `D:\incubationSpace\MeshOptTutorials\ART_SVN`
- staging root: `Assets/raw_temp/models`

Rules:

- the tool clears this directory at startup before a new run
- staged files are safe to overwrite directly
- stale files in this directory are not treated as user content
- the directory is temporary only and should not become a source of truth
- cleanup should delete the staged source asset and its generated Unity import artifacts, not the external-source root and not the final `_trim.fbx` output
- `Assets/raw_temp` is not meant to be checked into source control
- files created there are disposable working files, not deliverables

### Minimal V1 config contract

The JSON config should be enough to connect a project-side model entry to its external source and output target.

Suggested fields:

- `entryId`
- `isExternalSource`
- `externalSourcePath`
- `outputPath`
- `reductionPercent`

Path contract:

- `entryId` is the Unity asset path for the project-side selected model entry, normally rooted under `Assets/`
- `externalSourcePath` is canonicalized relative to the configured external-source root in normal workflow entries
- machine-local absolute filesystem paths may be tolerated for temporary hand-authored test entries, but they are not the canonical contract
- `outputPath` is the Unity asset path for the derived output and should normally stay under `Assets/`
- canonical config entries should use forward slashes in stored paths

For the first visualization pass, this config may be hand-authored for test cases.
Later visual tooling should edit this config directly.
The CLI tool reads the same config and executes from it.

For full V1 execution readiness, this round-trip should work without manual JSON edits during a normal tool run.

## Asset Ownership And Naming V1

### Source asset policy

The original asset is treated as read-only in the normal workflow.

- the visual tool does not overwrite the source asset by default
- the CLI tool does not overwrite the source asset by default
- generated meshes are saved to a stable output path
- if the generated output already exists, update the derived asset at the same path instead of creating a new derived path

### Source asset inside the project

This is **not the recommended production workflow**.

Use it only when the original model is intentionally managed as a Unity project asset.

If the original model is already in the Unity project:

- keep the imported source asset unchanged
- save the simplified result as a fixed-name derived asset
- prefer a stable output name such as `Warrior_Bindpose_trim.fbx`
- avoid percentage-suffixed output names as the default, because they make prefab and reference management harder

Example:

- `Warrior_Bindpose.fbx` -> `Warrior_Bindpose_trim.fbx`

If `Warrior_Bindpose_trim.fbx` already exists, the tool should update that asset instead of deleting and recreating it. This keeps the Unity asset GUID stable, which is important for prefab and scene references.

### Output file type

For production skinned-character assets, V1 should prefer reduced FBX output when the original source is FBX and the downstream import pipeline depends on avatar / rig information.

- `hero_red_trim.fbx` is the preferred production output for FBX-backed character assets
- `hero_red_trim.mesh` may still be useful as a debug or intermediate asset
- `hero_red.fbx` should not be overwritten by default

Reason:

- Unity `Mesh` assets can preserve vertices, indices, bindposes, and skin weights, but they do not preserve the full FBX model import contract
- avatar generation, rig settings, model hierarchy, material slots, and importer configuration are part of the FBX import workflow
- replacing a character pipeline with only a `.mesh` output can break prefab/avatar/resource import expectations

Implementation implication:

- producing `*_trim.fbx` is an FBX rewrite/export feature, not just an output naming change
- this uses the imported Autodesk FBX SDK package at `Packages/com.autodesk.fbx@4.1.2`
- the simplification step still uses the existing mesh simplify method, and the export step writes a new FBX from the simplified result
- the exported FBX must inherit the source FBX system unit instead of forcing a fixed centimeter unit
- the tool should later add a validation step that checks whether the input FBX unit settings are consistent across the pipeline
- the tool must preserve or reconstruct the skeleton hierarchy, skin clusters, bindposes, mesh attributes, material assignments, and importer-relevant structure
- the reduced FBX should use a fixed derived name such as `hero_red_trim.fbx`
- if the reduced FBX already exists, update it through the defined export path instead of changing the source FBX in place
- FBX export should be tracked as a separate native/export pipeline requirement with its own build, packaging, and validation plan

### Source asset outside the project

This is the **recommended production workflow** for FBX-backed character assets.

If the original model is outside the Unity project:

- the goal is to produce the simplified result and keep only the reduced file
- do not require the original source file to remain as a managed project asset
- the user still selects the project-side model entry in the tool
- configuration data determines whether that project-side model represents an external source
- provisional external source root: workspace-relative `ART_SVN/`
- treat this root as the current default for external-source lookup when no per-entry override is present
- keep it configurable so a later revision can move the source root without changing the workflow contract
- import or copy the external source into `Assets/raw_temp/models` only while the tool is running
- keep the staged file name identical to the external source file name
- treat the staged asset as a preview/input artifact, not as a long-term project asset
- delete the staged asset after the user accepts, cancels, or the CLI command finishes
- record provenance separately if the original path must be tracked later

Reason:

- the external workflow is meant to reduce asset count, not preserve the full source library
- Unity import and preview workflows still need a project asset while editing
- the tool still needs a stable output path for the generated reduced file

Recommended external-source flow:

1. User selects the project-side model entry.
2. The tool checks configuration data to determine whether the entry maps to an external source.
3. If an external source is configured, the tool copies it into `Assets/raw_temp/models`.
4. The staged file keeps the same file name as the external source, for example `hero_red.fbx` -> `hero_red.fbx`.
5. Unity imports the staged file for preview and simplification.
6. The visual tool lets the user set `reductionPercent`.
7. On accept, the tool writes or updates the fixed reduced output, for example `hero_red_trim.fbx` for FBX-backed character assets.
8. The tool deletes the staged source asset and its generated import artifacts.

CLI external-source flow:

1. CLI receives an `entryId` and loads the saved parameter set from the canonical config path.
2. CLI checks configuration data to determine whether the entry maps to an external source.
3. If an external source is configured, CLI stages the source file inside `Assets/raw_temp/models` using the same file name as the external source.
4. CLI runs simplification in batch mode without opening the visual editor.
5. CLI writes or updates the fixed reduced output from config.
6. CLI deletes the staged source asset and its generated import artifacts before exiting.

Staging rules:

- staged assets must not be referenced by prefabs or scenes
- staged assets must not be committed as final content
- `Assets/raw_temp/models` should be cleaned on tool startup or before a new run
- cleanup should run both on success and on cancellation/failure where possible
- cleanup should remove both the staged source asset and its Unity import artifacts

### Replace mode

Destructive in-place replacement is not the default workflow.

- it may remain available only as an explicit advanced action
- it should be limited to asset types that can safely be replaced in place
- imported `.fbx` character sources should prefer a fixed-name derived FBX output such as `_trim.fbx`

## Deferred Topics

The visual comparison workflow is intentionally left open for later discussion.

Still undecided:

- how the comparison view should be laid out
- whether comparison is frame-based, clip-based, or asset-based
- how many views are shown at once
- how error visualization is toggled
- how the user steps through worst frames
- whether comparison results are stored, exported, or only previewed

## Relation to Metrics

This workflow document is intended to be used together with:

- [SkinnedMeshSimplification_Metrics_V1.md](SkinnedMeshSimplification_Metrics_V1.md)
- [SkinnedMeshSimplification_ExecutionPlan_V1.md](SkinnedMeshSimplification_ExecutionPlan_V1.md)

The metrics document defines what should be measured.
This workflow document defines how the parameter is authored and how the CLI consumes it.
The execution plan document tracks current progress and handoff status.

The execution gate for V1 does not require the later comparison UI or full benchmark presentation layer.

Acceptance guidance:

- prefer unit tests for deterministic behavior
- save test outputs or metrics whenever practical
- use manual visual checks only when the behavior cannot be asserted automatically
- keep CI-friendly checks separate from editor-only visual checks

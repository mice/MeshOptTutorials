commit 82172fd3156409918c62f7992e4491b0e33e80ed
Author: lu.shenglin <mice_003@163.com>
Date:   Fri May 8 12:43:30 2026 +0800

    测试导出fbx

diff --git a/Docs/SkinnedMeshSimplification_ExecutionPlan_V1.md b/Docs/SkinnedMeshSimplification_ExecutionPlan_V1.md
new file mode 100644
index 0000000..76a9155
--- /dev/null
+++ b/Docs/SkinnedMeshSimplification_ExecutionPlan_V1.md
@@ -0,0 +1,287 @@
+# Skinned Mesh Simplification Execution Plan V1
+
+## Purpose
+
+This document is the execution-facing plan for the first version of skinned-mesh simplification work in this repository.
+
+It is meant for handoff between agents. Keep this file current as tasks complete.
+
+## Source Of Truth
+
+Use these documents together:
+
+- [SkinnedMeshSimplification_Workflow_V1.md](SkinnedMeshSimplification_Workflow_V1.md)
+- [SkinnedMeshSimplification_Metrics_V1.md](SkinnedMeshSimplification_Metrics_V1.md)
+
+Workflow defines the contract.
+Metrics define how results are judged.
+This file defines what is being executed now.
+
+## Current Status
+
+Completed or already established:
+
+- V1 parameter scope is fixed to `reductionPercent`
+- config path is fixed to `Assets/EditorConfig/SkinnedMeshSimplification.json`
+- temp staging path is fixed to `Assets/raw_temp/models`
+- trimmed FBX output path and naming rules are defined
+- FBX rewrite path exists in `Assets/Editor/SkinnedMeshFbxExport.cs`
+- trimmed FBX import behavior exists in `Assets/Editor/TrimFbxModelPostprocessor.cs`
+
+Still open and blocking the V1 main flow:
+
+- visual parameter authoring and interactive preview flow
+- external-source staging and cleanup flow
+- CLI batch entry and config-driven execution
+- targeted validation / regression harness
+
+Still open but not blocking V1 contract definition:
+
+- comparison UI details
+- full benchmark set definition
+- deeper unit-consistency validation
+- broader normal/tangent regression policy
+
+## V1 Execution Gate
+
+V1 should be treated as executable only when all of the following are true:
+
+- section 1 is current and no longer conflicts with workflow or metrics
+- section 2 is implemented end-to-end for load, save, stage, and cleanup behavior
+- section 3 exposes only `reductionPercent` plus output-target editing
+- section 4 can run the same config in batch without interactive editing
+- section 5 can write or update a stable `_trim.fbx` output while preserving the FBX-backed import path
+- section 6 provides enough validation to reject obviously broken output before manual acceptance
+
+Section 7 remains deferred and is not part of the V1 execution gate.
+
+Acceptance standard:
+
+- prefer unit tests wherever the behavior can be tested without manual inspection
+- save test result data where practical so the pipeline can compare runs over time
+- CI test mode should produce machine-readable output for process checks
+- manual visual inspection should be a fallback or supplementary check, not the primary acceptance path
+
+## Execution Plan
+
+### 1. Freeze the V1 contract
+
+Status:
+
+- in progress until the other documents stop moving
+
+Goal:
+
+- prevent scope drift
+- keep workflow, metrics, and execution aligned
+
+Deliverables:
+
+- workflow doc kept current
+- execution plan doc kept current
+
+Done when:
+
+- the three docs agree on what is blocking V1 execution
+- deferred work is marked consistently across the three docs
+
+### 2. Implement config and staging flow
+
+Status:
+
+- not implemented yet
+- blocking
+
+Goal:
+
+- load and save the JSON config
+- stage temporary model files under `Assets/raw_temp/models`
+- clear temp files at startup or before a new run
+
+Clarification:
+
+- external source files live under the current default root `D:\incubationSpace\MeshOptTutorials\ART_SVN`
+- staging copies those files into `Assets/raw_temp/models` so Unity can import them
+- staged copies are temporary project assets, not canonical assets
+- `Assets/raw_temp` is not source-controlled deliverable content
+- cleanup removes staged copies and their Unity import artifacts only
+- cleanup must not delete files from `ART_SVN`
+- cleanup must not delete the final `_trim.fbx` output
+
+Deliverables:
+
+- config loader/saver
+- staging cleanup logic
+- provenance handling for external-source entries
+
+Done when:
+
+- the config can be loaded from `Assets/EditorConfig/SkinnedMeshSimplification.json`, whether authored by hand or by the visual tool
+- external-source entries can stage into `Assets/raw_temp/models` using a deterministic file name
+- staging cleanup runs before a new run and after success or cancellation/failure where possible
+
+Phase-1 allowance:
+
+- for early test cases, the config may be hand-authored instead of written through the visual tool
+- config write UI is therefore not a hard gate for the first visualization pass
+- config field path semantics should still follow the canonical workflow contract even for hand-authored test entries
+
+### 3. Implement visual parameter authoring
+
+Status:
+
+- not implemented yet
+- blocking
+
+Goal:
+
+- expose only `reductionPercent` in V1 visual tooling
+- show the original model and simplified model side by side
+- show the core evaluation data that changes as the simplification changes
+- allow the user to change `reductionPercent` and refresh the simplified result
+
+Clarification:
+
+- phase-1 visual preview does not need to compute the full benchmark comparison suite on every parameter change
+- until the comparison harness exists, the live panel may show currently available non-benchmark indicators such as reduction ratio, vertex or triangle counts, import validity, and any available validation flags
+
+Deliverables:
+
+- visual preview for two models
+- live refresh of simplified output after `reductionPercent` changes
+- core metric display for the current comparison
+- no extra parameter scope beyond V1
+
+Done when:
+
+- a user can change `reductionPercent` and see the simplified model update
+- the original and simplified models remain visible together
+- the displayed core metrics change when the simplified result changes
+- the UI does not expose any extra simplification knobs beyond `reductionPercent`
+
+### 4. Implement CLI execution
+
+Status:
+
+- not implemented yet
+- blocking
+
+Goal:
+
+- consume the same JSON config as the visual tool
+- run simplification in batch mode
+- produce the same output asset naming behavior
+
+Clarification:
+
+- the CLI entry is a non-interactive batch path
+- it should load one `entryId` from the canonical config
+- it should stage external-source inputs when needed
+- it should not depend on the visual editor being open
+- it should not introduce a separate config format or separate parameter model
+
+Test entry split:
+
+- editor test entry: interactive visual panel
+- CI test entry: `bat`-driven test mode for automated runs
+
+Authority rule:
+
+- the authoritative execution surface is one Unity batch-mode entry
+- any `bat` file is a wrapper for CI convenience, not a second execution contract
+
+Deliverables:
+
+- command-line entry path
+- batch staging and cleanup
+- config-driven reduction execution
+
+Done when:
+
+- batch mode can load the same JSON config written by the visual path
+- batch mode can process an entry without any editor interaction
+- batch mode follows the same staging and output naming rules as the visual flow
+- the minimum CLI argument contract is documented and stable enough for repeatable use
+
+### 5. Keep FBX rewrite export working
+
+Status:
+
+- partially implemented
+- still blocked by missing surrounding execution flow and validation
+
+Goal:
+
+- write simplified output back to FBX
+- preserve skeleton, weights, and importer-relevant data
+- use the source FBX unit, not a fixed conversion target
+
+Deliverables:
+
+- stable `_trim.fbx` rewrite path
+- unit handling derived from source FBX
+- rig and skin data preserved through export
+
+Done when:
+
+- exporting to the same derived `_trim.fbx` path updates that file instead of switching names per run
+- source FBX unit handling remains derived from the source asset rather than a fixed export unit
+- the imported trimmed FBX still contains the expected rig, bindpose, and skinning data needed by Unity import
+
+### 6. Add validation and regression checks
+
+Status:
+
+- not implemented yet
+- blocking for reliable V1 acceptance
+
+Goal:
+
+- verify output imports correctly
+- inspect unit mismatch and normal changes
+- catch visible deformation regressions early
+
+Deliverables:
+
+- targeted import checks
+- unit consistency check to be added later
+- normal/tangent regression notes for known problem cases
+- unit tests for deterministic behavior where practical
+- saved metric/test artifacts for comparison between runs
+
+Done when:
+
+- the tool can fail fast on an output that does not reimport as a valid skinned FBX
+- the tool records at least the minimal import and deformation checks defined in the metrics doc
+- automated tests cover the non-visual parts of the flow as much as practical
+- test data is saved in a format suitable for CI or later diffing
+- full benchmark metrics remain optional until the comparison harness is scheduled
+
+### 7. Defer comparison UI specifics
+
+Goal:
+
+- keep the first version focused
+- avoid hard-coding a comparison UI before the core pipeline is stable
+
+Deferred items:
+
+- exact comparison layout
+- frame stepping policy
+- worst-frame review UI
+- storage/export format for comparison results
+
+## Handoff Rules
+
+When another agent picks up this work:
+
+- read the workflow doc first
+- read the metrics doc next
+- treat this file as the live task board
+- do not re-open settled V1 scope unless a blocking bug requires it
+- update status here when a task changes state
+
+## Notes
+
+The current focus is the FBX-backed skinned-mesh path.
+Do not broaden this plan into general mesh tooling unless the user explicitly asks for that expansion.

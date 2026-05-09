# Skinned Mesh Simplification Metrics V1

## Purpose

This document defines the first-pass validation metrics for skinned-mesh simplification in this repository.

The goal is to compare algorithms on the same assets, the same animation clips, and the same reduction ratios, with both numeric scores and visual evidence.

## Execution Gate Vs Benchmark Gate

This document serves two different needs:

- a minimum execution gate for deciding whether the V1 pipeline is safe enough to run and review
- a later benchmark gate for comparing algorithms and presenting richer evidence

The minimum execution gate is blocking for V1 pipeline acceptance.
The richer benchmark presentation is not blocking until comparison work is explicitly scheduled.

## Minimum V1 Acceptance Checks

Before a run is treated as acceptable for manual review, verify at least the following:

- batch execution reaches a successful export signal and writes the expected `_trim.fbx` output
- temporary staged source assets are cleaned back out of `Assets/raw_temp/models` after the run
- the imported trimmed FBX finishes with `read/write` disabled on the final output importer
- the trimmed FBX reimports successfully as a skinned model
- the imported result still exposes the expected mesh, bindposes, and skinning data
- the output does not show an obvious unit mismatch relative to the source FBX
- normals and tangents are imported with the expected policy for known problem cases
- a reviewer can inspect at least three representative poses or frames for visible deformation breakage:
   - bind or rest pose
   - one neutral or idle pose if available
   - one high-deformation pose from an available clip if available

Current automated coverage on the canonical sample includes:

- batch success log present
- imported-output validation log present
- rest-pose validation log present
- neutral-pose validation log present
- high-deformation-pose validation log present
- refreshed `_trim.fbx` timestamp
- `Assets/raw_temp/models` cleaned after the run
- final trimmed FBX importer ending with `isReadable: 0`

Current broader sample coverage now includes:

- `Tools/Validate-SkinnedMeshBatchExportSuite.ps1` runs the existing batch regression across every configured entry in `Assets/EditorConfig/SkinnedMeshSimplification.json`
- the current suite passes for both configured entries, `Anim_1325` and `Anim_1326`
- the suite writes a machine-readable aggregate report to `Temp/batch-skinned-export-suite-result.json` plus per-entry reports under `Temp/batch-skinned-export-suite-reports`
- the aggregate report now carries per-entry metadata such as `reductionPercent`, external-source provenance, output size, and key validation flags, plus suite-level coverage counters for the current gate checks

Current benchmark seed set now includes:

- `Assets/EditorConfig/SkinnedMeshBenchmarkSet_V1.json` as the machine-readable source of truth for the first benchmark seed set
- `Anim_1325` and `Anim_1326` as the first fixed benchmark meshes
- rest pose, neutral pose, and high-deformation pose as the required representative categories
- explicit clip-priority lists for neutral and high-deformation sampling on both current meshes
- the current validated reduction percentages, `50` and `90`, as the seed comparison ratios for these two meshes

Current gap relative to the minimum acceptance checklist:

- the current automated deformation coverage is now proven on the small configured suite rather than only one canonical asset path
- richer artifact generation, broader benchmark expansion, and refinement of the current screen-space/joint heuristics remain follow-up work, not a current V1 execution blocker

These checks are the minimum gate for the core pipeline.
They do not replace the fuller benchmark metrics below.

## V1 Metric Set

### Primary metric

**Joint-region p95 surface error**

Definition:

1. Skin the original mesh for a given animation frame.
2. Skin the simplified mesh for the same frame.
3. Sample points on the original animated surface.
4. Measure the shortest distance from each sample point to the simplified animated surface.
5. Compute the 95th percentile of the distances inside joint-risk regions.

Joint-risk regions include:

- elbow
- knee
- shoulder
- hip
- twist chains
- armpit
- crotch
- tail root
- wing root
- hard bone-weight boundaries

Why this is the primary metric:

- it measures deformation in the places where simplification usually fails first
- it is more sensitive than global averages
- it is less fragile than max error

### Secondary metrics

**Global p95 surface error**

- same measurement as above, but across the full mesh
- used as a broad quality baseline

**Max surface error**

- useful for spotting isolated severe failures
- not used as the main ranking metric because it is too sensitive to single outliers

**Screen-space visual diff**

- compare rendered original and simplified frames from fixed camera views
- use as visual proof of whether the numeric error is actually visible

## Reporting Format

Machine-readable comparison contract:

- `Assets/EditorConfig/SkinnedMeshComparisonResultSchema_V1.json` now defines the current result schema for future comparison outputs
- the schema carries both current gate-oriented validation fields and future deformation-metric/artifact fields so later producers can evolve without redefining the contract
- `Tools/New-SkinnedMeshComparisonResult.ps1` now produces the schema-shaped comparison result scaffold at `Temp/skinned-mesh-comparison-result-v1.json`
- `Tools/Update-SkinnedMeshComparisonMetrics.ps1` now enriches that result by running `MeshEditorUtils.Batch_ComputeSkinnedMeshComparisonMetrics` for each current entry and filling representative-pose `jointRegionP95Error`, `globalP95Error`, `maxError`, and a fixed-view `screenSpaceDiff`
- the current seed-set result now reports `comparison.status = partial` for both `Anim_1325` and `Anim_1326`, with `aggregate.metricCoverage.jointRegionP95Error = 2`, `aggregate.metricCoverage.globalP95Error = 2`, `aggregate.metricCoverage.maxError = 2`, and `aggregate.metricCoverage.screenSpaceDiff = 2`
- the current joint-region metric is a minimal heuristic producer driven by multi-influence weights plus hard bone-weight boundary triangles, and the current `screenSpaceDiff` is a fixed-view offscreen render diff over the representative poses; worst-frame artifacts and render artifact paths remain intentionally uncomputed in the current partial producer

For each algorithm, reduction ratio, mesh, and animation clip, report:

- `joint_region_p95_error`
- `global_p95_error`
- `max_error`
- reference frame index for the worst joint-region frame
- reference frame index for the worst global-error frame

Recommended normalization:

- divide surface errors by character height, or
- divide surface errors by mesh bounds diagonal

## Benchmark Visualization Output

Once the comparison harness is in scope, it should produce:

1. side-by-side render
   - original
   - simplified
   - error heatmap
2. per-frame error curve
   - `joint_region_p95_error`
   - `global_p95_error`
   - `max_error`
3. worst-frame list
   - top 10 frames by `joint_region_p95_error`
   - top 10 frames by `global_p95_error`

## Comparison Rule

For benchmark comparison work, algorithm quality should be judged in this order:

1. `joint_region_p95_error`
2. `global_p95_error`
3. screen-space visual diff

## Notes for Later Requirement Work

This document only fixes the first validation target.

Later additions should define:

- which camera views are mandatory
- which output formats the tool should generate
- how comparison results should be reviewed once producers start writing the current schema


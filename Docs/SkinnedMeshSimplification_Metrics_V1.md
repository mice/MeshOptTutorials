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

- the trimmed FBX reimports successfully as a skinned model
- the imported result still exposes the expected mesh, bindposes, and skinning data
- the output does not show an obvious unit mismatch relative to the source FBX
- normals and tangents are imported with the expected policy for known problem cases
- a reviewer can inspect at least three representative poses or frames for visible deformation breakage:
   - bind or rest pose
   - one neutral or idle pose if available
   - one high-deformation pose from an available clip if available

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

- which meshes are in the benchmark set
- which animation clips are required
- which camera views are mandatory
- which reduction ratios must be tested
- which output formats the tool should generate
- how the comparison results should be stored or reviewed


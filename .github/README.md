## Usage

Select the target mesh asset in the Project window, then use the context menu:

- `Assets/mesh/convert`
- `Assets/mesh/SimplifyMesh`
- `Assets/skin/convert`
- `Assets/skin/SimplifyMesh`

## Supported Scope And Hard Constraints

The current skinned-mesh tool is intended only for:

- character meshes
- monster meshes
- LOD / distance-reduction use cases

It is **not** a general-purpose mesh simplifier. Use it only when all constraints below are satisfied.

### Hard constraints

#### C1 - No BlendShape support

- BlendShape / morph targets are not supported
- Meshes with BlendShapes must stay out of the simplification pipeline
- Reason: the current rebuild path does not preserve BlendShape frames or deltas

#### C2 - Maximum 4 bone influences per vertex

- Maximum supported value is **4 bones per vertex**
- This is enforced by `ValidateSkinnedMeshForProcessing(...)`
- Reason: the current implementation depends on the legacy `BoneWeight` path and assumes `bonesPerVertex <= 4`

#### C3 - Single-submesh meshes only

- Only **single-submesh** meshes are supported
- Reason: the current tool processes one triangle index buffer and does not preserve multi-submesh authoring structure

#### C4 - Valid skinning data is required

- Bindposes must exist
- `GetBonesPerVertex()` must match `vertexCount`
- Legacy `boneWeights.Length` must match `vertexCount`
- Reason: the rebuilt mesh copies skinning data back from the source mesh and assumes the source data is internally consistent

#### C5 - UV channel support is limited

- The current skinned simplifier preserves **UV0 only**
- UV1 / UV2 / UV3+ are not preserved by the skinned simplification path
- In many character / monster cases this is acceptable because UV1 lightmap data is often irrelevant for animated meshes
- But any shader or runtime feature that depends on extra UV channels is **not supported**

#### C6 - Intended output quality

- Recommended usage is **mid/far LOD**
- Prefer conservative reduction ratios first
- The farther the output is from camera-critical deformation zones, the safer the result

### Practical interpretation

If a mesh is a normal character / monster mesh, has no BlendShape, uses one submesh, uses at most 4 bone influences per vertex, and only relies on UV0, then the tool is within its intended operating range.

As an editor tool, mesh readability itself is not treated here as a product-level limitation, because it can be adjusted on the asset import side when needed.

If any of those assumptions are false, the result should be treated as unsupported.

## Skinned Mesh Simplification Notes

This section records only the cases that still matter **inside the current supported scope**:

- character / monster meshes
- no BlendShape
- single submesh
- at most 4 bone influences per vertex
- valid skinning data
- UV0-only requirement accepted

The goal is to focus follow-up work on the real remaining deformation risks, and avoid spending time on out-of-scope cases.

Important: the cases below are a risk analysis inferred from the current implementation and common skinned-mesh failure modes. They are not a substitute for per-asset animation QA or measured deformation benchmarks. Priority labels are therefore triage guidance, not a formal severity ranking.

Current pipeline:

1. `SkinMeshOpt.Simplify(...)` snapshots bind-pose vertex data into `SimpleSkinData`
2. `MeshEditorUtils.OptMeshData(...)` reindexes exact duplicate vertices
3. `MeshOperations.Simplify(...)` calls `meshopt_simplify(...)`
4. The simplified index buffer is written back and bone weights are copied onto the rebuilt mesh

Important: this document intentionally ignores UV1/lightmap concerns. The focus here is only on cases that can still cause animation deformation problems for supported character / monster assets.

### Group A - Cases that still matter within the current constraints

#### Case A1 - Large joint bending areas

- Examples: elbow, knee, shoulder, hip
- Symptom: the mesh looks acceptable in bind pose, but caves in, pinches, or collapses when the joint bends
- Why it happens: `meshopt_simplify(...)` evaluates simplification from bind-pose vertex positions; it does not evaluate the same area under animated poses
- Why this matters: these are the exact areas where geometric error in bind pose is a poor predictor of runtime deformation quality
- Improvement direction: evaluate simplification error on sampled poses/clips, or add a skinning-aware metric that penalizes collapses across highly deforming joints
- Priority: **high** (inferred from deformation behavior)

#### Case A2 - Twist / roll bone chains

- Examples: forearm twist, upper-arm twist, spine twist, neck twist
- Symptom: volume loss, candy-wrapper artifacts, or unnatural twisting after animation plays
- Why it happens: the simplifier does not understand rotational deformation driven by twist chains; it only sees static positions
- Why this matters: twist chains often require extra topology density even when the bind pose appears visually simple
- Improvement direction: add pose sampling for twist-heavy clips and preserve density in regions with large rotational deformation
- Priority: **high** (inferred from deformation behavior)

#### Case A3 - Stretch-heavy deformation regions

- Examples: armpit, crotch, torso side panels, monster mouth corners, tail roots, wing roots
- Symptom: triangles disappear in places that later stretch open, causing holes, sharp silhouette loss, or visible tension artifacts
- Why it happens: simplification is decided before those areas are observed in stretched poses
- Why this matters: an area that is compact in bind pose may become visually important only during motion
- Improvement direction: score the mesh across a set of representative extreme poses instead of bind pose only
- Priority: **high** (inferred from deformation behavior)

#### Case A4 - Hard bone-weight boundaries

- Examples: armor plates attached to different bones, rigid-looking straps, hand/finger transitions
- Symptom: a boundary that should look stable starts wobbling, collapsing, or losing shape when adjacent bones move differently
- Why it happens: the algorithm keeps bone weights after simplification, but the triangle removal decision itself is still position-driven and not weight-gradient-aware
- Why this matters: even if weights are copied back correctly, the reduced topology may no longer support the intended deformation boundary
- Improvement direction: add a penalty for simplifying across strong bone-weight gradients, or preserve protected regions near weight discontinuities
- Priority: **medium** (inferred from deformation behavior)

#### Case A5 - Over-aggressive reduction ratios

- Examples: 25% target on limbs, face-adjacent body parts, fingers, claws, tails
- Symptom: the algorithm technically completes, but the animation quality drops faster than expected in motion
- Why it happens: a bind-pose-driven simplifier becomes much less predictable as reduction becomes more aggressive
- Why this matters: even within supported assets, the largest visible failures often come from using an overly strong reduction level instead of from a coding bug
- Improvement direction: keep conservative presets first, and evaluate 75% / 50% before enabling 25% broadly
- Priority: **medium** (policy / QA guidance)

### Group B - Out-of-scope cases under the current product boundary

#### Case B1 - More than 4 bone influences per vertex

- Current status: blocked in `ValidateSkinnedMeshForProcessing(...)`
- Action now: **no extra work**
- Note: only revisit this if the tool scope expands beyond the current character / monster constraint

#### Case B2 - BlendShape / morph driven characters

- Current status: blocked by generic mesh validation
- Action now: **no extra work**
- Note: BlendShape support is explicitly out of scope for the current tool

#### Case B3 - Multi-submesh skinned characters

- Current status: blocked by generic mesh validation
- Action now: **no extra work**
- Note: single-submesh is part of the current supported scope

#### Case B4 - Invalid or incomplete skinning data

- Current status: blocked when bindposes / boneWeights / bonesPerVertex are inconsistent
- Action now: keep validation, but do not treat this as an algorithm-improvement track
- Note: this is an input-quality guard, not a core simplification problem

#### Case B5 - Extra UV channels

- Current status: unsupported by the current skinned simplifier
- Action now: **no extra work**
- Note: for the current character / monster scope, UV0 is the only required channel; revisit this only if future assets need UV2+ at runtime

## Practical Conclusion

The current `Assets/skin/SimplifyMesh` path should be treated as a constrained **character / monster LOD tool**, not as a general skinned-mesh simplifier.

Code-review conclusion:

- the hard input boundary is already enforced in code, so Group B should stay out of the implementation backlog
- the real technical risk is pose-dependent deformation quality under reduction, especially around joints, twist chains, stretch regions, and strong weight boundaries
- the right next step is not broader format support, but better validation of supported assets and more conservative reduction policy for high-risk regions

The main remaining work should stay focused on **Group A**:

1. joint bending areas
2. twist chains
3. stretch-heavy regions
4. hard weight boundaries
5. reduction ratio policy

Everything in **Group B** is currently out of scope or already blocked by validation, so it should not drive extra implementation work right now.

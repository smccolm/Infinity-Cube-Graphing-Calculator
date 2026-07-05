# Revision 12 Requirements

R12 is a Surface rendering stability pass. It preserves the R8 BoundaryWeightedTanSampler, Mesh mode, LOD profiles, parser, cache shape, and TAN cube coordinate behavior.

## Goals

- Keep Mesh mode unchanged.
- Keep Surface mode cinematic, but reduce extreme-edge tearing and dirty transparency.
- Treat TAN boundary walls as a special rendering case.
- Make single visible surfaces mostly opaque so they do not blend against themselves.
- Reject pathological sliver triangles only when they create dishonest edge artifacts.
- Keep Surface/Mesh switching non-recalculating when sample results are already present.

## Surface renderer changes

- Single visible Surface uses full opacity.
- Multiple visible Surfaces retain controlled transparency.
- Surface rasterizer uses a small z-buffer tie margin to prevent nearly coplanar edge triangles from overwriting each other.
- Boundary and near-corner cells are identified during triangle construction.
- Extremely skinny, tiny, high-depth-spread triangles are skipped.
- Back-facing boundary slivers are skipped when they would contribute noise.
- Back faces that remain visible are slightly darkened.

## Out of scope

- No sampler rewrite.
- No Direct3D migration.
- No expression language expansion.
- No changes to the 15-function layout.

# GraphCalc TAN Cube - R12

R12 is a Surface edge-stability pass on top of the accepted R8 sampler baseline and the R11 cinematic Surface baseline. It keeps the `BoundaryWeightedTanSampler`, keeps Mesh as the startup default, and refines Surface mode so extreme TAN boundary walls look solid instead of torn or self-transparent.

## What changed in R12

* Preserved the R8 `BoundaryWeightedTanSampler` and LOD profiles.
* Preserved `View > Mesh` and `View > Surface` as a two-option selector.
* Added `View > Surface Quality` with `Fast`, `Smooth`, and `Cinematic`.
* Preserved the R11 rasterized Surface renderer and cinematic material lighting.
* Made single visible Surface renders fully opaque to prevent self-blending artifacts.
* Added z-buffer tie protection for nearly coplanar edge-wall triangles.
* Added boundary/corner sliver rejection for pathological extreme-edge triangles.
* Darkened remaining back-facing boundary triangles instead of letting them look like noisy transparency.
* Projection panels now use rasterized filled projection surfaces in Surface mode.
* `File > Test Mode...` keeps the optional `Show surface triangle edges` debug overlay.
* Cache remains compatible with existing sampled results because R12 is a rendering/UI refinement, not a sampler change.

## Folder setup

Use a new folder:

```bat
E:\\Graphing Calculator R12
```

Extract this ZIP directly into that folder.

After extraction you should see:

```text
E:\\Graphing Calculator R12\\scripts
E:\\Graphing Calculator R12\\src
E:\\Graphing Calculator R12\\README.md
E:\\Graphing Calculator R12\\GraphCalc.sln
```

## Build

Open Command Prompt and run:

```bat
cd /d "E:\\Graphing Calculator R12"
scripts\\01\_check\_prereqs.cmd
scripts\\02\_restore.cmd
scripts\\03\_build.cmd
```

The prerequisite script may say the  is ready if an 8.x SDK is present. A newer 9.x SDK can also build `net8.0-windows` projects when the 8.x targeting packs are available.

## Run

Use two Command Prompt windows.

Window 1, API:

```bat
cd /d "E:\\Graphing Calculator R12"
scripts\\04\_run\_api.cmd
```

Leave it open.

Window 2, UI:

```bat
cd /d "E:\\Graphing Calculator R12"
scripts\\05\_run\_ui.cmd
```

## R12 smoke path

1. Start API.
2. Start UI.
3. Confirm Mesh is selected by default.
4. Let the default functions calculate.
5. Choose `View > Surface`.
6. Confirm the graph renders as filled surfaces.
7. Choose `View > Surface Quality > Cinematic`.
8. Confirm surfaces look smoother and less vector/triangle outlined.
9. Choose `View > Mesh`.
10. Confirm the R8/R10-style mesh returns without recalculating.
11. Open `File > Test Mode...`.
12. Toggle `Show surface triangle edges` only for diagnostics, then turn it off again.

## Expected R12 behavior

* Mesh remains the technical wire view.
* Surface mode is the production visual view.
* Surface mode should not show normal gray triangle outlines or dirty self-transparent edge tearing.
* Cinematic quality should look smoother than Fast/Smooth, though it may be slower on WPF.
* Projection panels switch with the main render mode.
* Changing Surface Quality does not recalculate function samples.
* Changing Mesh/Surface does not recalculate function samples.
* LOD changes still recalculate checked non-empty functions.

## Logs and data

Logs:

```text
%LOCALAPPDATA%\\GraphCalc\\logs
```

Database:

```text
%LOCALAPPDATA%\\GraphCalc\\data\\graphcalc.db
```

Use `scripts\\06\_clean\_logs.cmd` to clear logs for a clean test run.



## R12 notes

R12 refines Surface mode boundary rendering. It keeps the R8 sampler and R11 cinematic surface path, but makes single surfaces opaque, adds z-buffer tie protection, rejects pathological edge slivers, and darkens remaining back-facing boundary surfaces.


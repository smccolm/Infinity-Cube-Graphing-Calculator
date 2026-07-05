# Revision 12 Smoke Test

## Build and run

```bat
cd /d "E:\Graphing Calculator R12"
scripts\01_check_prereqs.cmd
scripts\02_restore.cmd
scripts\03_build.cmd
```

API window:

```bat
cd /d "E:\Graphing Calculator R12"
scripts\04_run_api.cmd
```

UI window:

```bat
cd /d "E:\Graphing Calculator R12"
scripts\05_run_ui.cmd
```

## Surface edge artifact check

1. Select `View > Surface`.
2. Select `View > Surface Quality > Cinematic`.
3. Calculate and show only F2: `0.12*(x*x-y*y)`.
4. Rotate the cube to inspect the two extreme side walls.

Expected:

- Mesh mode remains unchanged.
- Surface mode is mostly solid for one visible function.
- The two extreme edges should not show the dirty transparent tearing seen in R11.
- Boundary walls may still look mathematically steep, but they should not look like accidental alpha noise.

## Toggle check

1. Switch `View > Mesh`.
2. Switch `View > Surface`.
3. Switch back to `View > Mesh`.

Expected: render mode switches without recalculating samples.

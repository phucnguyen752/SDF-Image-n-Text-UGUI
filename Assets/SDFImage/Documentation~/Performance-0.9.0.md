# SDF Image performance in 0.9.0

SDF Image now shares render materials between compatible images and prepares their properties only when dirty. It reuses material storage as animated groups change style, including native stencil variants. The sprite shader and one-quad geometry are unchanged.

## Measurement

100 non-overlapping images, one sprite and Canvas, 90 warmup frames and 240 measured frames per case. Unity **6000.0.83f1**, URP **17.0.4**, Windows Development Player / Mono / Direct3D 11, i5-13600KF / RTX 3060, 1280 × 720 offscreen render. Before: release 0.8.0 (`e1670a9` in the development project). After: the image runtime sources shipped in 0.9.0; source hashes were checked again during release preparation.

CPU below is `firstCanvasUpdateMeanMs`: effect Width changes plus the first Canvas callback cycle. It is **not whole-frame CPU or GPU time**. The offscreen benchmark invokes three Canvas cycles per frame. Dynamic cases change Width on every image each frame unless stated otherwise.

| Configuration | Draw calls before → after | Dynamic CPU before → after |
| --- | ---: | ---: |
| Same sprite, size and effects | **100 → 1** | **2.7129 → 1.3416 ms** |
| Different Graphic tints, matching effects | **100 → 1** | 2.7219 → 1.3463 ms |
| Four effect styles | **100 → 4** | 2.7338 → 1.3391 ms |
| Five image sizes | **100 → 5** | 2.7232 → 1.3418 ms |
| One shared stencil Mask | **102 → 3** | 2.8389 → 1.3937 ms |

With only one of the 100 images changing, the optimized result typically uses two draws, returning to one when its style matches the static group (measured mean: 1.9917). Its update + first Canvas cycle changed from 0.0478 to 0.0357 ms.

Static callback CPU was already negligible: 0.0009 → 0.0008 ms for the matching group. Treat that difference as noise. The separate whole-frame main-thread counter changed from 0.5692 to 0.3005 ms; native Image measured 0.2773 ms in the final run. This counter includes benchmark overhead and must not be added to the CPU column above.

## Allocation, memory and visual checks

- Final dynamic cases added no GC over their static baseline. The whole-frame counter still includes about 282 bytes/frame from the benchmark. Isolated regression cases confirm **zero managed bytes after warmup** for one animated group, five groups, and one group sharing a stencil mask. This does not claim allocation-free initialization or every custom mask configuration.
- 100 images retain 100 meshes, 200 triangles and 144,400 mesh bytes. The cache adds state buffers/spare materials; these mesh counters do not measure its total RAM cost.
- All **8 before/after PNG captures have identical SHA-256 hashes**, covering tint, style, size and stencil variants.
- The Windows Development Player built and ran every benchmark case without runtime exceptions.
- The complete **161-case** regression suite passed on Built-in/Gamma and URP/Linear. It covers shared/detached/rejoined materials, last-owner cleanup, animation, masks, real bordered nine-slice rendering and existing text behavior.

## Batching contract and limits

Images share a render state only when baked color/distance textures, local drawing rectangle, slice mapping and all effect properties match. Position/rotation and Graphic tint/alpha can differ. A changed image detaches before modifying its style, so siblings remain unchanged; matching styles can rejoin.

The cache keeps at most one spare per live render state and up to four native stencil variants per entry. Spares clear texture references, storage shrinks with usage, and the final owner releases it. Static images add no per-frame synchronization callback.

Different sprites/textures, sizes/pivots, styles, Canvases, clipping and overlapping order can split batches. There is **no cross-sprite atlas**. More effect layers still add texture samples; large soft/offset effects cover more pixels. No Android/iOS device, GPU timing, thermal or battery measurements were performed.

The CSV's `SDF_sliced` case uses a star with zero border; actual nine-slice coverage comes from the bordered regression render tests. `Image_shared` animates tint while SDF animates Width, so those dynamic rows are not equivalent workloads.

## Evidence

- [Before measurements](Image-0.9.0-before.csv)
- [Final measurements](Image-0.9.0-after.csv)
- [Built-in tests](Tests-0.9.0-Builtin.xml)
- [URP Linear tests](Tests-0.9.0-URP-Linear.xml)
- [SDF Text comparison with TMP](Performance-0.8.0.md)

Benchmark sources, player outputs and original captures remain in the development project's ignored `Build/ImagePerformanceAudit` and `Build/TmpValidation` directories. The published CSVs and test reports are copied from those results.

# SDF Text performance in 0.8.0

SDF Text shares face/effect materials between compatible labels and stores effect styles in vertices. With one font material, all layers on each side of the face share a mesh/renderer. Unchanged text skips full effect synchronization; source materials are watched once per shared material per Canvas cycle.

## Measurement

100 non-overlapping labels reading `Level 12345`, one font atlas/preset and Canvas, 90 warmup frames and 240 sampled frames per case. Unity 6000.0.83f1 / URP 17.0.4, Windows Development Player, Mono, Direct3D 11, i5-13600KF / RTX 3060, 1280 × 720 offscreen render.

CPU below is `firstCanvasUpdateMeanMs`: the first Canvas callback cycle plus `SetText` time. It is not whole-frame or GPU time. Offscreen rendering invokes three Canvas cycles per frame. Dynamic cases update every label each frame, including all four TMP copies.

| Configuration | Static CPU | Dynamic CPU | Draw calls |
| --- | ---: | ---: | ---: |
| One TMP with native outline/underlay | 0.0075 ms | 1.8864 ms | 1 |
| Four stacked TMP labels, four shared presets | 0.0194 ms | 7.7448 ms | 4 |
| Four stacked TMP labels, unique materials | 0.0244 ms | 7.8014 ms | 400 |
| SDF Text, effects disabled | 0.0138 ms | 2.1995 ms | 1 |
| SDF Text, one Normal layer | 0.0451 ms | 4.0202 ms | 2 |
| SDF Text, three Normal layers | 0.0452 ms | 4.7236 ms | 2 |
| SDF Text, three Inner underlays with offset | 0.0447 ms | 5.0075 ms | 2 |
| SDF Text, three layers with different styles across labels | 0.0437 ms | 4.6148 ms | 2 |
| SDF Text, three layers above/below the face | 0.0443 ms | 5.1282 ms | 3 |

The idle optimization reduced three-layer static CPU from 0.4124 to 0.0452 ms for 100 labels (89%). Compared with four shared TMP presets in the final run, the remaining static overhead is 0.0258 ms per first Canvas cycle, with half the draw calls. One native TMP remains lighter if its built-in effects are sufficient.

Across all three Canvas cycles, static callback CPU was 0.1371 ms for SDF Text with three Normal layers, 0.0574 ms for four shared TMP labels and 0.0192 ms for one native TMP. Whole-frame main-thread counters were 0.4677 / 0.4075 / 0.3013 ms respectively. These counters cover different scopes and must not be added together.

## Tradeoffs and limits

- Ten benchmark captures were byte-identical to the version before idle optimization, including offset Inner underlays, different styles and layers on both sides.
- Draw merging does not reduce layer triangles or overdraw. Shader work and visual capabilities differ from stacked TMP; this is not a GPU-equivalence claim.
- Mesh memory from renderer counters is 1,766,400 bytes for 100 three-layer SDF labels, versus 1,649,600 for four TMP copies and 412,400 for one TMP. Additional vertex channels also increase Canvas vertex bandwidth.
- The measured whole-frame dynamic/static managed-allocation difference for 100 SDF labels is 2,400 bytes/frame. The counters include the benchmark harness and are not a library-only allocation trace.
- Atlas, preset, Canvas, stencil and overlapping order can split batches. The two/three-draw examples require compatible labels and the single-font-material merge path.
- No Android/iOS device performance, GPU timing, thermals or battery measurements were taken. Windows timings do not establish mobile FPS.

Raw data: [CSV](Performance-0.8.0.csv), [environment](Performance-0.8.0-environment.txt). See [validation](../VALIDATION.md) for release tests and build coverage.

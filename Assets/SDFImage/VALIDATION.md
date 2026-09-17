# Validation

## 0.9.0 — SDF Outline and image material batching (2026-09-17)

Validated through a local UPM installation in an isolated Unity **6000.0.83f1** project. Runtime image sources were hash-checked against the completed Windows benchmark; release metadata, demo menu and documentation were then updated. Unrelated development scenes, project settings and user assets are excluded.

| Check | Result |
| --- | --- |
| Built-in pipeline, Gamma | **161 / 161 EditMode tests passed**, zero skipped, 34.303 s |
| URP 17.0.4, Linear | **161 / 161 EditMode tests passed**, zero skipped, 35.429 s |
| Android Image/Text shader bundle, Vulkan and OpenGL ES 3 | **Passed**, fresh strict shader bundle build |
| Windows Player script compilation | **18 runtime assemblies**, including SDFUI and TMP; no package Editor/test assemblies |
| Windows Development Player image benchmark | **Built and completed**, no runtime exceptions; unchanged runtime sources verified by hash |
| Before/after image rendering | **8 / 8 PNG captures have identical SHA-256 hashes** |
| README illustrations | **5 Unity URP/Linear renders visually reviewed**, including the new text mode guide |

Seven additional image test cases cover compatible material sharing, independent style changes and rejoining, texture/drawing/slicing separation, masked siblings, last-owner release, and warmed animated groups with zero managed allocations. Existing text, rendering, import, sample and GUID/icon checks remain active.

With 100 matching non-overlapping images, draws fell from **100 to 1**; four styles use **4** draws and one shared stencil Mask uses **3**. Animating Width on all images reduced property updates plus the first Canvas cycle from **2.7129 to 1.3416 ms**. This is not GPU time or whole-frame CPU. See the [image performance report and raw measurements](Documentation~/Performance-0.9.0.md).

Reports: [Built-in Gamma](Documentation~/Tests-0.9.0-Builtin.xml), [URP Linear](Documentation~/Tests-0.9.0-URP-Linear.xml). Build, capture and benchmark logs remain in the development project's ignored `Build` directory. Earlier release reports below are historical evidence.

No complete APK/iOS player build, mobile device run, GPU timing, thermal or battery profile was performed. Fewer draws do not remove per-layer shader/geometry costs. Automated Inspector and integration checks do not establish mouse-driven interaction coverage.

## 0.8.0 — text layer modes, batching and idle CPU (2026-09-17)

Validated the release sources through a local UPM installation in the isolated Unity **6000.0.83f1** fixture. The development scene, project settings and unrelated assets are excluded from this package release.

| Check | Result |
| --- | --- |
| Built-in pipeline, Gamma | **154 / 154 EditMode tests passed**, zero skipped, 35.703 s |
| URP 17.0.4, Linear | **154 / 154 EditMode tests passed**, zero skipped, 35.390 s |
| Android Image/Text shader bundle, Vulkan and OpenGL ES 3 | **Passed**, strict shader bundle build |
| Windows Development Player, Mono / D3D11 | **Built and ran the complete performance benchmark**, no runtime exceptions |
| Render comparison before/after idle optimization | **All 10 benchmark captures have identical SHA-256 hashes** |

The full suite includes 72 SDF Text cases covering Outer/Inner/Center, Normal/Inner underlays, original-glyph masking and holes, offsets, alpha, whole-layer ordering across fallback fonts, shared materials, merged effect meshes, style changes, native TMP geometry APIs, clipping and cache recovery. Idle regressions exercise transforms/fades, added/removed CanvasGroups and masks, sibling ordering and fallback-material animation. Existing image/import/bake/sample and serialized identity tests remain enabled.

In the Windows benchmark with 100 labels and three Normal layers, static CPU decreased from **0.4124 to 0.0452 ms** per first Canvas cycle. Four stacked TMP labels with shared presets measured **0.0194 ms / 4 draws**, versus **0.0452 ms / 2 draws** for SDF Text. One TMP with built-in effects measured **0.0075 ms / 1 draw**. These are Canvas callback timings, not whole-frame/GPU measurements. Extra effect vertices still cost memory and bandwidth; layer triangle count and overdraw remain. See the [performance report and raw data](Documentation~/Performance-0.8.0.md).

Reports: [Built-in Gamma](Documentation~/Tests-0.8.0-Builtin.xml), [URP Linear](Documentation~/Tests-0.8.0-URP-Linear.xml). Android shader and Windows build/player logs remain under the development project's ignored `Build` directory. The player benchmark exercised the same runtime/shader sources before the metadata-only version bump to 0.8.0; both full test suites ran after that bump. Git may normalize XML line endings without changing report content.

No complete APK/iOS player build, Android/iOS device run, mobile GPU timing, thermal or battery profile was performed. Automated tests do not establish mouse-driven Inspector interaction coverage.

## 0.7.0 — image layers, compressed bakes and import controls (2026-09-16)

Validated the package in an isolated local UPM fixture using Unity **6000.0.83f1**. The original development scene and unrelated project changes were excluded from the release.

| Check | Result |
| --- | --- |
| Built-in pipeline, Gamma | **110 / 110 EditMode tests passed**, zero skipped, 29.751 s |
| URP, Linear | **110 / 110 EditMode tests passed**, zero skipped, 29.903 s |
| StandaloneWindows64 player script compilation | **Passed**, 18 runtime assemblies; no SDFUI Editor/test assemblies |
| Android shader bundle, Vulkan and OpenGL ES 3 | **Passed**, strict shader bundle build |
| Image Inspector | **Passed**, 15 automated draws covering single/multiple selection, narrow layouts, texture color and adding layers |

The suite covers image layer ordering, offsets, independent texture color, 16 layers, one-time opacity fading, legacy field migration, reordered compatibility properties and cleared lists. Existing mask, nine-slice, native-size, source-swap and TextMeshPro checks remain enabled. Material-preset editing, Clear SDF, Undo/Redo, source identity preservation, compression formats and platform settings are also exercised.

The Ready-status regression was reproduced before the fix: editing layers did not rebake or reimport, but the queue cycled through Queued and Ready. The corrected test keeps Ready stable through layer edits, Undo/Redo, enable and refresh. A second test confirms missing/stale bakes still regenerate. The component's Ready badge and SDF Settings foldout were subsequently removed; the Inspector draw and player compilation checks passed afterward.

Distance compression preserves source geometry and effect padding, adding unused space to reach power-of-two dimensions. For the tested 400×400 star with 32-pixel padding, the distance payload decreased from **430,592 bytes (420.5 KiB)** in 464×464 RHalf to **131,072 bytes (128 KiB)** in 512×512 BC4/EAC R. These are texture payload sizes, not total Editor memory estimates. The uncompressed option restores RHalf storage. Separate Android-target validation passed 108 cases before the two queue regression cases were added.

Reports: [Built-in Gamma](Documentation~/Tests-0.7.0-Builtin.xml), [URP Linear](Documentation~/Tests-0.7.0-URP-Linear.xml), [player assemblies](Documentation~/0.7.0-player-assemblies.txt). Android shader, image-layer renders and Inspector probe logs remain in the development project's ignored `Build/Validation` directory. Git can normalize report line endings without changing their content.

No complete APK/iOS player build, mobile device run or mobile performance profile was performed. Automated Inspector draws and serialized edits do not establish mouse-driven interaction coverage.

## 0.6.0 — unified SDF Text layers (2026-09-16)

Validated the unified Layers list, Linear color correction and antialiasing update in the isolated local UPM fixture with Unity **6000.0.83f1**.

| Check | Result |
| --- | --- |
| Built-in pipeline, Gamma | **87 / 87 EditMode tests passed**, zero skipped, 14.120 s |
| URP, Linear | **87 / 87 EditMode tests passed**, zero skipped, 14.257 s |
| StandaloneWindows64 player script compilation | **Passed**, 17 runtime assemblies |

The suite includes 27 SdfText cases covering layer order across fallback materials, signed spread, offsets and masks, source geometry updates, cleared lists, legacy settings and prefab variants, compatibility aliases after reordering, and effect color/opacity. The Linear color regression failed before the correction and passes afterward. A separate Editor serialization check passed add/remove, signed spread, mixed values and single-label reorder with Undo/Redo; multi-label reordering is disabled to prevent Unity from copying one label's values over another's.

Five controlled antialiasing cases compared small, large and rotated labels, including stock and regenerated font atlases, against an 8× render reference. Using both atlas footprint axes reduced edge RMSE by approximately **4–18%** while retaining smoothstep, Softness behavior and the existing texture sample count. This does not restore detail missing from low-resolution glyphs.

The [1600×900 Layers demo](Documentation~/sdf-text-layers-demo.png) was rendered in URP Linear and inspected at native resolution. Final test, player-script, Inspector and demo logs contain no C# errors, shader errors, exceptions or native file-move failures.

Reports: [Built-in Gamma](Documentation~/Tests-TextLayers-Builtin.xml), [URP Linear](Documentation~/Tests-TextLayers-URP-Linear.xml), [player assemblies](Documentation~/TextLayers-player-assemblies.txt). Git may normalize report line endings; XML content and the assembly list are preserved. Inspector and antialiasing probe logs remain in the development project's ignored `Build/Validation` directory as `tmp-layers-inspector.log` and `tmp-layers-aa-stock-probe.log`.

No complete player build, mobile device run or mouse-driven Inspector interaction was performed. Player script compilation and serialized Editor actions are separate checks from those workflows.

## 0.5.0 — texture-colored outlines (2026-09-08)

Validated the optional SdfImage texture-color outline with Unity **6000.0.83f1** through the isolated local UPM fixture: **71/71 EditMode tests passed** in Built-in Gamma (14.756 s) and URP 17.0.4 Linear (13.784 s), with zero skips. StandaloneWindows64 player scripts compiled successfully with **17 runtime assemblies**.

Two new GPU cases verify a freshly baked multicolor source with transparent padding, Outer/Inner/Center positions, intensity 0/0.5/1/2, unchanged face color, exact restoration of solid-color rendering, sliced images, independent opacity, Image tint, CanvasGroup fading and RectMask2D clipping. Existing component tests also cover default values, invalid intensity, retained settings and stencil-material refresh. Captures from both pipelines were visually inspected. No C# or shader compiler errors were found.

Reports: [Built-in](Documentation~/Tests-TextureOutline-Builtin.xml), [URP Linear](Documentation~/Tests-TextureOutline-URP-Linear.xml), [player assemblies](Documentation~/TextureOutline-player-assemblies.txt). Report line endings are normalized for Git; XML content is preserved. The validated runtime, shader and test sources are unchanged for publication.

The [Use Texture Color showcase](Documentation~/sdf-outline-texture-color-demo.png) is an actual 1600×900 Unity URP Linear render exported to sRGB. It uses copies of the shipped Star, Ring and RoundedPanel sprites, the normal asynchronous SDF baker, intensity 0.5 and opacity 1. The panel uses nine-slice rendering. The image was visually inspected at native resolution.

No complete player build, mobile device run or mouse-driven Inspector interaction was performed for this change.

## 0.4.0 — TextMeshPro support (2026-09-08)

Validated **0.4.0** through a local UPM installation in an isolated Unity **6000.0.83f1** project, with uGUI **2.0.0**, TMP Essential Resources, Windows and Direct3D 11. These reports cover the final runtime, shader and test sources for this release.

| Check | Result |
| --- | --- |
| Built-in pipeline, Gamma | **69 / 69 EditMode tests passed**, zero skipped, 12.223 s |
| URP 17.0.4, Linear, HDR off, MSAA 1 | **69 / 69 EditMode tests passed**, zero skipped, 12.262 s |
| StandaloneWindows64 player script compilation | **Passed**, 17 runtime assemblies including `SDFUI.dll` and `Unity.TextMeshPro.dll`; no SDFUI Editor/test assemblies |

The suite includes the 58 existing sprite/package tests and 11 new TMP cases. The TMP checks cover tightly spaced glyphs and fallback materials with every opaque face remaining in front of the effects; dynamic text, empty text, `ClearMesh` and disabling; ancestor stencil/rectangular masks and animated outline width; same-frame unculling after text layout changes; a custom mesh supplied through `UpdateGeometry`; CanvasGroup fading; negative shadow spread; unchanged preferred size, rectangle and shared font material. An independent geometric-dilation check verifies that a seven-unit outline has no holes within five pixels of opaque concave glyph faces. All cases also passed in Linear rendering.

Raw reports: [Built-in](Documentation~/Tests-TMP-Builtin.xml), [URP Linear](Documentation~/Tests-TMP-URP-Linear.xml). [Player assembly list](Documentation~/TMP-player-assemblies.txt). All three final logs contain no C# errors, shader compiler errors or native file-write errors. The close-letter and fallback-font GPU captures were visually inspected in both pipelines.

![Tightly spaced SdfText glyphs rendered in URP Linear](Documentation~/tmp-preview.png)

The [SDF Text showcase](Documentation~/sdf-text-demo.png) is an actual 1600×900 URP Linear render. Its three labels use Liberation Sans SDFAA generated at sampling size 256, padding 48 and a 2048×2048 atlas; the colored outline uses width 7 and softness 0.8. The atlas was generated only for the capture, without changing the consuming project's font assets. The final image was inspected at native resolution.

No complete player build, Android/iOS device run or mobile performance profile was performed. This support is for SDF-font `TextMeshProUGUI` on Canvas; atlas padding limits expansion/blur, and Canvas/Mask/RectMask2D components must be on a parent. Live domain reload and mouse-driven Inspector interaction were not exercised by the automated suite. See the [TMP usage instructions](README.md#textmeshpro).

## 0.3.1 metadata patch

Version **0.3.1** adds the package author name `Phuc Nguyen` and GitHub profile URL, and updates release documentation. The manifest parses as valid JSON and the release diff passes whitespace checks. Runtime code, Editor code, shaders, tests, samples, and all `.meta` files are unchanged from **0.3.0**.

Unity test suites, clean-project installation, and Package Manager visual verification were not rerun for this metadata-only patch. The results below belong to **0.3.0**.

## 0.3.0 environment and results

SDF Image **0.3.0**, Unity **6000.0.83f1**, Windows, Direct3D 11 / NVIDIA RTX 3060. Validation ran in isolated projects under the source project's ignored `Build` directory. The consuming project's source metadata and one generated descriptor reference in `DialogWin.prefab` were migrated for the rename; no scene was changed by this audit.

| Check | Result |
| --- | --- |
| Built-in pipeline, uGUI 2.0.0, library in Assets | **58 / 58 EditMode tests passed**, zero skipped |
| Built-in pipeline, local UPM installation `com.sdfimage.ugui@0.3.0` | **58 / 58 EditMode tests passed**, zero skipped, 8.382 s |
| URP 17.0.4, uGUI 2.0.0, Gamma, HDR off, MSAA 1 | **58 / 58 EditMode tests passed**, zero skipped, 10.442 s |
| Destination project, URP 17.0.4, Linear color, existing HDR configuration | **58 / 58 EditMode tests passed**, zero skipped, 12.357 s |
| URP demo through automatic source baking | Rendered and visually inspected; no SDF shader compiler errors |
| StandaloneWindows64 player script compilation | **Passed**, 17 assemblies including `SDFUI.dll`; no `SDFUI.Editor.dll` in player output |

Unmodified final raw reports: [Built-in with local UPM](Documentation~/Tests-Builtin-UPM.xml), [URP Gamma](Documentation~/Tests-URP.xml), [destination URP Linear](Documentation~/Tests-URP-Linear.xml). Preview: [Unity URP render](Documentation~/preview.png). Final logs contain no C# errors, shader compiler errors, or inconsistent importer-result warnings. Earlier reports remain archived outside the distribution.

## What the tests establish

- Five distance-transform cases: independent brute-force comparison, threshold behavior, full/empty masks, invalid input.
- Eight asynchronous source-import cases: default off; enable; disable; active cancellation without automatic restart; source edits; rapid settings changes; maximum size; multiple sprites in one sheet.
- Generated descriptors and both textures share the source image's asset path; each descriptor is actually attached to its original Sprite. Original texture pixels, imported dimensions, and main-asset identity are preserved. Stable object IDs survive rebake/reimport. Tests detect separate `.asset` output and import loops.
- Sixteen component/layout cases: independent material lifecycle, stencil style refresh, expanded drawing with unchanged raycast bounds, nine-slice borders, collapsed slice centers, a thin `1024×3 → 64×1` bake at two Canvas PPU values, standard Image source/override swaps, normal Image fallback, effect toggles preserving style values, legacy Sliced/Preserve Aspect migration, layout parity with Unity Image before/after baking and effect toggles, and allocation-free warmed-up style animation with direct animated Sprite swaps.
- Fourteen GPU render cases: inner/outer outline, shadow direction, composite opacity, translated RectMask2D, single/nested stencil masks, SDF silhouette used as a Mask, several transformed images batched with a normal background Graphic, antialiased-edge seam repair, inactive-outline transparency guards, and authored transparency on unevenly stretched Simple/Sliced images.
- Two Inspector integration cases: the real custom ImageEditor with serialized Source assignment and Undo/Redo, Generate binding back to the same component without a helper, and legacy helper migration respecting a deliberately cleared source. These exercise Editor actions and serialization; they do not automate visual mouse interaction with the Inspector.
- Eleven Editor pipeline cases: preserve other tools' importer metadata before/after the SDF block, handle escaped/malformed/oversized metadata, restore cache publication without unnecessary pointer writes, retain disabled warm-cache data across reimport, and include Resources/preloaded dependencies while excluding Editor-only Resources from build checks. Custom dependency tracking connects published cache content to imported artifacts.
- Two package integration cases: public assembly names, shader/resource lookup, package identity, 64×64 component icon binding, and all six shipped sample components resolve with the renamed library. These also passed with the library installed through UPM.

The final demo uses 256px mathematical source artwork, the same asynchronous importer, and URP rendering. Its six SDF graphics use a single Image-derived component each, with no Auto Bake helper. It demonstrates outer/inner/center outlines, drop shadow, glow, nine-slice, and RectMask2D.

The release migration also ran all 58 cases in the destination project's original Linear/HDR configuration. A material color readback comparison was changed from exact float equality to a per-channel tolerance of 0.000001; component style values remain checked exactly. Runtime code and project settings were unchanged. All 60 pre-existing destination Assets, Packages and ProjectSettings files retained their hashes.

## Rename and serialized data

All existing library `.meta` GUIDs were preserved. In a separate Unity import check, four source sprites retained their GUIDs and source object IDs, all generated descriptors/color/distance textures remained embedded at the original source path, and six sample components resolved correctly. See the [recorded object identities](Documentation~/Rename-verification.json). Generated descriptor IDs changed with the import identifier rename; the shipped demo and consuming prefab were updated to the new IDs. Original source PNG bytes were not changed.

## Dark-edge regression

The reported keychain sprite was reproduced in an isolated URP fixture using a copy of the source PNG and importer settings. Its edge RGB was bright; the defect came from subtracting binary SDF coverage from a different, antialiased source alpha. Background/shadow showed through the resulting gap.

The earlier shader failed both white-on-white edge tests at brightness **0.451**; the corrected shader passed the **>0.98** brightness and alpha checks. It covers the join over the source filtering footprint, retains authored transparency farther inside, and leaves zero-width/transparent-outline behavior unchanged. Version 0.3.0 also keeps that footprint in source-pixel units when the image is stretched unevenly. Before/after URP renders of the actual sprite and the final demo were inspected. The change requires no new texture samples and no texture rebake.

## Bake responsiveness measurement

The integration test starts an active 512×512 bake, cancels it, checks that it does not restart/publish, then explicitly requests it again. Defaults produce 576×576 textures after padding. Timings include queue completion and observing the published SDF, measured once per final suite on this machine:

| Pipeline | Total bake | Largest observed Editor update gap | Cancel API call |
| --- | ---: | ---: | ---: |
| Built-in, local UPM | 104.1 ms | 4.1 ms | 0.6 ms |
| URP | 128.2 ms | 11.2 ms | 0.5 ms |

The Editor continued updating throughout. These are observations, not worst-case guarantees: texture import/publication and GPU resource creation still require the main thread. Larger sheets, slow storage, import workers and other Editor activity can change the numbers. No production mobile FPS claim follows from these timings.

## Old-library comparison

The previous `Assets/com.nickeltin.sdf` implementation was inspected from project Git history as requested. Its GPU generator ran jump-flood passes on full-resolution data, performed synchronous readback, and reduced resolution afterward; yielding between whole imports did not make each texture bake asynchronous.

This library reduces the source region first, processes alpha only for distance generation, uses asynchronous readback and one cancellable CPU worker, and publishes cached results during import. It keeps the useful source-owned asset workflow while avoiding separate baked Assets files. It does not reuse the old implementation's source code.

## Not established by these checks

- No complete player build or Android/iOS device run; player script compilation is a separate check from a packaged player.
- No performance profile in the existing game's actual UI or a low-end phone.
- Sprite Atlas packing in a player, third-party material modifiers, and all supported mobile graphics APIs have not been exercised here. Linear rendering passed the same 14 GPU regression cases in the destination project; this does not cover every HDR or post-processing configuration.
- This release implements Simple/Sliced uGUI graphics. It does not claim every feature of the reference commercial asset.

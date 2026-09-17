# SDF Outline

Outlines and shadows for **Unity 6 / uGUI (Canvas)** sprites and TextMeshPro labels. This standalone library uses uGUI 2.0 and its bundled TextMeshPro.

## Contents

- [Install and update](#install-and-update)
- [SDF Image quick start](#sdf-image-quick-start)
- [SDF Text quick start](#sdf-text-quick-start)
- [Sprite import and bake settings](#textures-embedded-in-the-source-sprite)
- [Image rendering and limitations](#image-rendering)
- [Performance](#performance)
- [Image API](#api)
- [Troubleshooting](#troubleshooting)
- [Demo and testing](#demo-and-testing)

## Install and update

In Package Manager, choose **Install package from Git URL** and enter:

```text
https://github.com/phucnguyen752/sdf-image.git#upm
```

This URL follows the `upm` branch. After each release, select **SDF Outline** (or **SDF Image** before updating an older version) in Package Manager and click **Update**; keep the same URL and let Package Manager update the version. If you installed a tag such as `#0.3.1`, use **Install package from Git URL** once with the `#upm` URL above to switch to this update flow. See [Unity's Git package update instructions](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-ui-update.html).

SDF Outline includes the **SDF Image** and **SDF Text** components. The package ID (`com.sdfimage.ugui`) and installation URL remain unchanged after the library rename.

To keep this version, use `https://github.com/phucnguyen752/sdf-image.git#0.9.0`. Clicking **Update** while using this tag will not switch to a newer release tag.

The `upm` branch and version tags contain the `com.sdfimage.ugui` package at the repository root; no `?path=` is needed. The `main` branch contains the full Unity project, with the library in `Assets/SDFImage`. Keep `#upm` in the URL because the default `main` branch does not have a package at its root.

You can also copy `Assets/SDFImage` with its `.meta` files into a Unity 6 project with uGUI 2.0, or keep a copy outside Assets and use Package Manager → Add package from disk with `package.json`. Keep only one installation. Source textures must be in Assets to save settings and import attached data. Shaders in Resources are included in builds. See [Publishing.md](Documentation~/Publishing.md) for the release workflow.

Namespaces and assemblies use `SDFUI`, `SDFUI.Editor` and `SDFUI.Tests.Editor`. When updating from version 0.2, update namespaces in your code and the package ID in the manifest; preserve script `.meta` files so existing components retain their identities. Move source settings and baked-data references together with the library. The component icon is a 64×64 PNG exported from the [source SVG](Documentation~/SdfImage.svg).

## SDF Image quick start

1. Create **GameObject → UI → SDF Image**. It uses a single `SdfImage` component derived from `UnityEngine.UI.Image`.
2. Assign the original sprite to **Source Image** on this component.
3. If the sprite has no SDF yet, click **Generate SDF**. The image continues to display normally while baking.
4. Standard Unity Image properties appear directly in the Inspector: **Source Image**, **Color**, **Material**, Raycast, Maskable, Image Type and its related options. These controls are always available, without a separate group or waiting for generation.
5. After generation, edit **SDF Effects → Layers** in the component Inspector. Drag layers to reorder them; the top layer is in front. Bake controls are available in the source texture's **SDF Import Settings**.

Settings belong to the **source texture** and apply to every sprite in that texture. Assigning a regular sprite does not start a bake. Generation enables **Auto Update**, so later changes to the image, import settings or SDF settings trigger an update. You can change Auto Update in SDF Import Settings. The source texture's Inspector shows **Generate** until an SDF is available, then **Open SDF Import Settings**.

Clicking **Cancel** or disabling Auto Update cancels queued and running work and prevents new jobs. Completed results remain available. Disable individual layers to hide them, or turn off **Effects Enabled** to use the standard Image rendering path.

For each layer, enable **Use Texture Color** to use the texture's RGB for the effect. **Intensity** `0` produces black, `1` keeps the original color, and values above `1` make it brighter. **Opacity** controls the effect's alpha separately. When disabled, the layer uses **Color** and retains your settings. Existing SDF sprites do not need rebaking.

![Use Texture Color: gradient star, hollow ring and nine-sliced panel rendered in Unity URP](Documentation~/sdf-outline-texture-color-demo.png)

All three examples use **Intensity 0.5** and **Opacity 1**; the outline follows the texture colors along each sprite's edges.

The legacy `SdfAutoBake` component is retained so older prefabs still load. Image adopts its saved source; **Remove Legacy Auto Bake** in the Inspector removes the redundant helper with Undo support. New objects do not need this helper.

## SDF Text quick start

1. Create **GameObject → UI → SDF Text**. The `SdfText` component derives from `TextMeshProUGUI` and keeps the standard TMP Inspector for content, font, font size, alignment, spacing, auto size and rich text.
2. Assign a TMP font with an SDF atlas. There is no need to Generate SDF or bake text into sprites.
3. Enable **Effects Enabled** in **SDF Effects** below the Inspector. Spread, softness and offset use Canvas local units.

Use **+** and **−** in **Layers** to add or remove effects, and drag the handles to reorder them. Each layer has its own enable toggle, Position, Color, Width/Spread, Softness and Offset. **Position** offers **Outer**, **Inner**, **Center** and **Underlay**. Inner draws a border inside the glyph edge while leaving the middle of the stroke visible; Center puts half the width on each side. Inner and Center borders and Inner underlays render over the text, while Outer and Normal underlays render below it. Within each group, the top list entry (lowest index) draws in front. **Effects Enabled** controls the whole list and retains its settings when disabled.

![SDF Text Layers: stacked outlines, an offset shadow and reordered colors rendered in Unity URP](Documentation~/sdf-text-layers-demo.png)

### Text layer positions

| Position / Underlay Type | Shape | Draw group |
| --- | --- | --- |
| Outer | Exterior border; glyph interior stays empty | Below text |
| Inner | Inset border clipped to the original glyph | Above text |
| Center | Border on both sides of the edge | Above text |
| Underlay / Normal | Filled silhouette for shadows and glow | Below text |
| Underlay / Inner | Inner shadow clipped to the original glyph and holes | Above text |

![Text border positions and Normal/Inner underlays, rendered directly in Unity](Documentation~/sdf-text-modes-guide.png)

Outline **Width** must be positive; zero or negative widths hide the border. **Outer** draws only the exterior border, leaving the glyph interior empty. **Underlay** shows an **Underlay Type** dropdown with **Normal** and **Inner**. Normal fills the glyph shape behind the text: positive **Spread** expands it, zero keeps its size, and negative values contract it. Outer and Normal underlays can look alike when opaque text covers their centers. Normal is useful for shadows and glow, especially with an offset.

**Underlay Type → Inner** casts a shadow inside the original glyph. Offset shifts the silhouette that cuts out the shadow, so shading appears on the opposite side; Softness blurs it. Positive Spread expands that silhouette and reduces the inner shadow; negative Spread grows the shadow. This differs from **Position → Inner**, which draws an inset border. Existing and new layers default to Normal. Use the layer checkbox to disable either type.

**Softness** blurs the effect edge. Both Inner borders and Inner underlays are masked by the original glyph, including its holes: Offset moves the pattern inside that fixed mask, and Softness cannot extend it outside the text. Other effects move their entire shape with Offset.

**Softness 0** still uses antialiasing. Enlarged contours from low-resolution glyphs can remain rough; regenerate the font at a higher sampling size, with enough atlas space and padding for large labels and thick effects. Softness adds blur but cannot restore missing glyph detail.

Reordering requires a single selected label. You can edit shared layer settings across multiple labels, and add or remove layers together when their layer counts match.

Existing layers load as **Underlay / Normal** to preserve their filled outline, shadow and glow rendering, including signed spread. New layers also default to Underlay / Normal; choose Position → Inner to add an inset border. Outer and Normal underlays stay behind all glyph faces, including fallback fonts and multiple materials. The component uses TMP's current mesh and font atlas and updates automatically when content, layout or fonts change at runtime. TMP's built-in Outline/Underlay/Glow effects are disabled on separate render materials; source fonts and materials remain unchanged.

The material Inspector below **SDF Effects** edits the assigned TMP material preset, including Face Color, Softness and Dilate. Changes persist in that material and affect other labels sharing it. Choose a separate material preset for an independent style. Temporary render materials are hidden from the Inspector; use **SDF Effects** for outlines, shadows and glow.

![SDF Text: tight spacing, colored outline and soft glow rendered in Unity URP](Documentation~/sdf-text-demo.png)

These normal-underlay examples use the same `SdfText` component: outline and shadow behind tightly spaced text, a colored outline on a multiline label, and glow from a shadow with no offset. The image was rendered directly in URP Linear. See the [tight-spacing checks and render report](VALIDATION.md).

For an existing TMP label, create an **SDF Text** label, assign its font, content and layout settings, then update references to the new component. Automatic conversion of existing TMP components is not provided; do not replace the TMP script directly in a scene or prefab.

`SdfText` can still be assigned to a `TMP_Text` or `TextMeshProUGUI` field. Continue using `text`, `SetText`, `font`, `fontSize` and the usual TMP APIs:

```csharp
using SDFUI;
using UnityEngine;

public sealed class ScoreLabel : MonoBehaviour
{
    [SerializeField] private SdfText label;

    private void Awake()
    {
        label.EffectsEnabled = true;
        label.Layers.Clear();
        label.Layers.Add(new SdfTextEffect { Color = Color.black, Width = 2 });
        label.Layers.Add(new SdfTextEffect
        {
            Color = new Color(0, 0, 0, 0.3f),
            Width = 0,
            Softness = 2,
            Offset = new Vector2(0, -3)
        });
        label.RefreshEffects();
    }

    public void SetScore(int score) => label.SetText("Score: {0}", score);
}
```

`Layers` exposes a mutable `List<SdfTextEffect>`. Each entry has `Enabled`, `Position` (a `SdfOutlinePosition`, default `Underlay`), `UnderlayType` (a `SdfTextUnderlayType`, default `Normal`), `Width` (shown as Spread for Underlay), `Softness`, `Color` and `Offset` properties. `Spread` is an alias for `Width`. For example, `label.Layers[0].Position = SdfOutlinePosition.Inner` selects an inset border. Set Position to Underlay and UnderlayType to `SdfTextUnderlayType.Inner` for an inner shadow. After adding, removing, reordering or editing entries from code, call `RefreshEffects()`.

Existing outline settings migrate in their current order, followed by the old shadow as the back layer, with its settings and enabled state preserved. New labels start with an enabled outline layer and a disabled shadow layer. The earlier `Outline*` and `Shadow*` properties remain compatibility aliases for their migrated layers, including after reordering; use `Layers` and `EffectsEnabled` for new code.

Normal underlay spread zero renders an unexpanded effect. This also applies to migrated width-zero outlines; disable the layer to hide it. Inner underlay spread zero can still cast a shadow through Offset and Softness. Outer, Inner and Center borders are hidden at width zero.

- Supports `TextMeshProUGUI` on a Canvas only. 3D `TextMeshPro` and custom font shaders that do not use SDF are not supported.
- Spread and softness are limited by the font atlas's existing padding and distance range. If an effect stops expanding, regenerate the font atlas with more padding; sprite bake settings do not affect fonts.
- Supports ancestor Canvases, `Mask`, `RectMask2D` and `CanvasGroup` in the hierarchy. Place `Canvas`, `Mask` and `RectMask2D` on a parent object. Attaching them directly to the text object disables SDF effects.
- Labels share face/effect materials by font preset and stencil state. Effect color, width, softness, position and offset are stored in mesh vertices, so different layer styles can batch together without affecting one another. Render materials are shared and read-only; edit the font preset or `Layers` instead.
- With one font atlas, all effects on each side of the text merge into one mesh/renderer while preserving whole-layer order. Compatible, non-overlapping labels can typically use two draws (effects on one side plus face), or three with effects both above and below. Different presets, atlases, Canvases, masks, clipping or overlapping draw order can split batches. Multiple font materials retain separate ordered effect graphics across fonts.
- Effect meshes upload when geometry, TMP's SDF scale or a layer style changes. Native TMP padding and mesh indices are cached until their inputs change. Inner borders and Inner underlays with a nonzero Offset use an extra atlas sample; glyph atlas bounds prevent large offsets from sampling neighboring characters.
- Unchanged text skips layer enumeration, transform writes and material setup. Lightweight checks follow transform/order, renderer alpha and runtime component changes; TMP/UI dirty callbacks request full synchronization when needed. Source material changes are checked once per shared render material per Canvas cycle, including fallback fonts. Call `RefreshEffects()` after editing `Layers` from a script.
- See the [0.8.0 performance comparison with TMP](Documentation~/Performance-0.8.0.md) for measured CPU, draw calls and memory tradeoffs.
- Combining layers reduces draw calls and renderer overhead, but each effect still draws its glyph geometry, so triangle count and overdraw remain. Effect meshes carry extra vertex data, and TexCoord2/TexCoord3 are enabled on the containing Canvas; this increases vertex memory/bandwidth. Profile frequently changing text and target phones rather than treating fewer draws as a guaranteed FPS improvement.
- Shared material copies refresh when the source preset changes and are released with their last owner. Font material animation, fallback fonts, masks and layer order remain supported; the native TMP face keeps its own render pass.

## Textures embedded in the source sprite

The padded color texture, distance texture and `SdfSprite` descriptor are subassets of the **source image file itself**. The descriptor is also attached directly to the Sprite through Unity 6's `Sprite.AddScriptableObject` API; `SdfSprite.FromSprite(source)` retrieves it in the player.

Expand the source image in the Project window and select **`<sprite name> SDF`** to open its import settings. This is the actual single-channel distance texture, with Unity's texture preview. The color texture and descriptor remain internal, and the original image stays the main asset. Expand **Imported Textures** to inspect both generated textures' dimensions and formats. **Open SDF Import Settings** on the source Inspector or SDF Image component opens the same entry.

The source Inspector has an **SDF** foldout below **Open Sprite Editor**, beside the native **Advanced** section. It shows status and one action: **Generate** before a bake exists, or **Open SDF Import Settings** once it is ready. All bake controls live in **SDF Import Settings**: Auto Update, Padding, Distance Range, Alpha Threshold and Compress Distance, followed by Unity's native **Default** and platform icon tabs. The entire platform compression panel is Unity's native Sprite Inspector: **Max Size**, **Resize Algorithm**, the full platform **Format** list, **Compression**, **Use Crunch Compression**, and format-specific quality and platform controls. Platform tabs follow installed build modules. Each platform can override the defaults independently. Use **Apply** to save changes or **Revert** to discard pending edits. Inactive platform overrides do not invalidate the current target's bake. These settings belong to SDF generation and do not change the original texture's platform settings.

**Clear SDF** in the import settings removes the generated textures and descriptor for every sprite in that source image, cancels any ongoing bake, and turns off Auto Update. The original image, Sprite references, and saved bake settings stay intact. The source Inspector returns to **Generate**. Clear supports Undo/Redo; local cache files may be reused when restoring or generating again.

No separate `.asset`, SDF PNG or baked-image folder is created in Assets. Temporary cache data lives in `Library/SDFImage` and does not need to be committed. Commit the source image, its `.meta` file and the library. On a new machine, Unity rebakes sources with SDF enabled during import. The original image file and its image import settings remain unchanged; SDF settings are added to `TextureImporter.userData` while preserving its existing contents.

`Image.sprite` keeps its reference to the original Sprite in the player to find the attached data. Baking does not run at runtime. Wait for Ready before building sources with Auto Update enabled. Build validation checks images in enabled scenes, Resources, preloaded assets and dependent prefabs. Sprites without generated SDF data use the standard Image renderer.

## Bounded, cancellable baking

- Downscales **before** calculating distances. Max Size defaults to 512 and uses Unity's native size choices. The total bake budget below still applies. Choose Bilinear or Mitchell to control downsampling. The source texture's dimensions are unchanged.
- Reads the GPU with `AsyncGPUReadback` and calculates distances on one worker in time linear to the pixel count. It does not perform synchronous GPU readback or wait for the worker on the main thread.
- Runs one job at a time. Changing settings cancels outdated results; only the current generation can be published.
- Caches by source, Sprite ID and settings. Reimporting valid data does not repeat the bake.
- Limits each texture to 128 sprites or 4 million texels after padding. Exceeding either limit reports an error asking you to reduce Max Size or padding before allocating large bake buffers.
- The main thread still creates GPU resources and publishes subassets through Unity's import process, which may briefly stall depending on the machine. There is no guarantee of zero stalls or any unmeasured frame rate.

The Editor needs a graphics device that supports AsyncGPUReadback. Running with `-nographics` cannot generate new SDF data. The GPU is used only to read the imported image, including its alpha and import settings; the source does not need Read/Write enabled.

## Image rendering

- **SDF Effects → Layers** is a reorderable list, matching SDF Text: the top entry is in front. Each Image supports up to **16 layers**, with independent enable, color, width/spread, softness and offset. **Effects Enabled** toggles the entire list without losing its settings.
- Each layer supports **Outer**, **Inner** or **Center** outlines. **Underlay** fills the silhouette behind the sprite for shadows and glow; positive Spread expands it and negative Spread contracts it. Exterior effects stay behind the sprite; inner outlines tint its inner edge.
- Each layer has its own **Use Texture Color** and **Intensity**. This replaces layer RGB with texture RGB × Intensity, without multiplying by layer Color or Image Color RGB. Alpha still uses the layer Color/Opacity and overall Image alpha. Intensity does not change alpha.
- Existing outline and shadow settings migrate into two layers. Released scalar APIs and legacy animation/prefab overrides follow their original layers after reordering. Clearing the list remains intentional.
- Retains the source artwork and transparency for the fill, subject to the selected color compression. `Graphic.color` tints the fill, while its alpha fades the entire image and effects once.
- Supports Simple, preserve aspect, nine-slice, layout and native size. The quad expands to avoid clipping outlines and shadows.
- Supports `Mask`, `RectMask2D` including softness, and `CanvasGroup`. Raycasts still use the original RectTransform.
- SDF Image supports Simple and Sliced with Fill Center enabled. Filled/radial fill, Tiled and Sliced with Fill Center disabled use the standard Unity Image renderer without SDF effects. Text is supported through `SdfText` as described above. SpriteRenderer, UI Toolkit and Coffee SoftMask/UIEffect are not integrated.

Width, softness, offset, blur and spread use **Canvas local units**. Padding and Distance Range use **pixels of the downscaled SDF image**. The shader limits effects to the available padding and distance range; increase both if an outline stops expanding. Shadow offset is independent of the distance limit. Canvas and object transforms scale the effects too.

The field stores signed distances, positive inside the shape. The algorithm calculates the Euclidean distance to the opposite alpha class with a half-pixel correction. Alpha Threshold defines the boundary. Compressed storage normalizes these distances into a linear single-channel texture; the shader decodes them back to source pixels. This is a raster SDF, not vector reconstruction or MSDF; increasing Max Size helps preserve fine details.

Each image uses one quad, compositing its layers before applying Graphic/CanvasGroup alpha once. Images with matching baked textures, local drawing rectangle, slice mapping and effect settings share a cached material and can batch together. Position, rotation and Graphic tint/alpha do not require separate materials. Different textures, dimensions/pivots, styles, Canvases, clipping or overlapping order can split batches. This does not pack different sprites into an atlas.

Render materials are shared and read-only; edit the component or its `Layers` and call `RefreshEffects()` after changing list entries. A style change detaches from a shared material without changing other images. Material properties are prepared only when dirty. Animated groups reuse material storage, including native stencil variants. The cache keeps at most one spare per live render state and four stencil variants per entry; spares retain no textures and are released as usage shrinks or the final owner is removed. Static images add no per-frame synchronization callback.

The shader samples the fill once, distance once per visible layer, and color once more for each texture-colored layer. More layers increase fragment work; large offsets and soft effects increase the covered area. Multiple images using the same Sprite share its baked textures, and editing layers does not rebake them. The first 16 list entries are supported; entries beyond this limit are not rendered.

### Baked texture memory and compression

**SDF Import Settings** uses Unity's native Sprite compression controls and texture encoder. **Automatic** chooses a format from the platform and compression quality; a platform override exposes the same complete Format list as a normal Sprite, including applicable Crunch formats. The native controls show compressor quality and platform-specific options when relevant. Existing bake settings are retained when opening the Inspector.

The native platform controls encode the padded **color** texture. **Compress Distance**, enabled by default, separately stores the distance map as **BC4** on desktop or **EAC R** on Android/iOS/tvOS (4 bits per texel), with **R8** on other targets. Each distance dimension is rounded up to a power of two by adding outside-of-shape texels at the top and right. This does not resize the artwork, move the sprite or change its usable effect padding. For example, a 400×400 sprite with padding 32 keeps its 464×464 field inside a 512×512 compressed texture. Color and distance use independent texture coordinates, so color formats and sprite slicing remain aligned.

Disable **Compress Distance** to keep the original, uncompressed **RHalf (2 bytes per texel)** field and its exact padded dimensions. Compression is lossy; large Distance Range values or extreme magnification can expose contour errors. Both generated textures have no mipmaps and release their CPU-readable pixel copies. This does not change the original texture's format or Read/Write setting. Use GPU readback if tooling needs to inspect generated pixels. Older RHalf descriptors remain supported without changing existing object references.

For a 400×400 sprite with padding 32 and BC7 color, the compressed distance map uses **128 KiB**, compared with **420.5 KiB** for RHalf. Together with the 464×464 color texture, the two baked textures use approximately **338 KiB of GPU pixel data**, compared with **631 KiB** with RHalf distance storage. These figures exclude the original texture, object overhead and any platform fallback. Editor memory reports can include extra texture data; measure a player build for runtime memory. This change does not optimize TMP font atlases or draw-call batching.

Compression is lossy for the color artwork, including alpha and texture-colored outlines. Select **Compression: None** or **RGBA 32 bit** for exact baked colors; it still releases the CPU copies. RGB-only formats discard alpha, as they do for ordinary sprites. Crunch reduces stored data rather than GPU memory. Color textures receive unused right/top padding as required by the selected block format (or square power-of-two padding for PVRTC), without stretching the image or changing its pivot, borders or native size. Unsupported target GPUs may decompress textures and use more memory; validate your target devices. Lower Maximum Size and keep Padding only as large as your effects require to reduce memory further.

## Performance

In the 0.9.0 desktop benchmark, 100 compatible, non-overlapping images dropped from **100 draws to 1**; four styles used **4**, and one shared stencil Mask used **3** including mask setup/teardown. Animating Width on all 100 images reduced property updates plus the first Canvas cycle from **2.7129 to 1.3416 ms**. Static images already had negligible callback cost.

See [image benchmark, raw data and limits](Documentation~/Performance-0.9.0.md) and [SDF Text versus TMP](Documentation~/Performance-0.8.0.md). Text typically uses two draws for effects on one side, or three on both sides, with one font atlas and compatible ordering. Draw-call reduction does not remove overdraw, texture sampling or effect geometry. Android/iOS device performance has not been measured.

## API

```csharp
using SDFUI;
using UnityEngine;

public sealed class ButtonStyle : MonoBehaviour
{
    [SerializeField] private SdfImage image;
    [SerializeField] private Sprite icon; // SDF generated in the Editor.

    private void Awake()
    {
        image.sprite = icon; // Standard Image API; overrideSprite is also supported.
        image.OutlineEnabled = true;
        image.OutlineWidth = 4;
        image.OutlineColor = Color.white;
        image.OutlinePosition = SdfOutlinePosition.Outer;
        image.ShadowEnabled = true;
        image.ShadowColor = new Color(0, 0, 0, 0.35f);
        image.ShadowOffset = new Vector2(0, -6);
        image.ShadowBlur = 10;
    }
}
```

`SdfImage` derives from `Image` and can be assigned to a `UnityEngine.UI.Image` field or a Button's Target Graphic. `image.sprite` and `image.overrideSprite` automatically find the matching SDF data, including when switching sprites within one sheet. The legacy `image.Sprite` API accepting `SdfSprite` remains for compatibility; new code should use `image.sprite` and `image.SdfData`. For Buttons, enable Raycast Target; the Canvas needs a GraphicRaycaster and EventSystem as usual for uGUI.

To color the outline from the texture, set `image.OutlineUseTextureColor = true` and `image.OutlineTextureColorIntensity = 1f`. This mode is disabled by default; intensity defaults to `1` and accepts values of `0` or greater. `image.OutlineColor.a` still controls opacity; `Image.color` RGB only tints the fill.

For multiple effects, edit the list and call `RefreshEffects()` after changing entries or their order:

```csharp
image.Layers.Clear();
image.Layers.Add(new SdfImageEffect { Width = 3, Color = Color.blue });
image.Layers.Add(new SdfImageEffect { Width = 8, Color = Color.white });
image.Layers.Add(new SdfImageEffect {
    Position = SdfOutlinePosition.Underlay,
    Spread = 8, Softness = 4, Offset = new Vector2(3, -5),
    Color = new Color(0, 0, 0, 0.5f)
});
image.RefreshEffects();
```

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Image has no effects | Generate SDF for the source; enable Effects Enabled and a visible layer; use Simple or Sliced with Fill Center. |
| Text has no effects | Assign a TMP SDF font; enable Effects Enabled; keep Canvas/Mask/RectMask2D on ancestors. |
| Effect stops growing or clips | Increase sprite Padding and Distance Range, or regenerate the TMP atlas with sufficient padding. These are separate workflows. |
| Baked sprite colors differ | Use uncompressed color storage, check Image tint/alpha and layer Intensity. Lossy color compression can shift RGB/alpha. |
| Inner underlay seems reversed | Its Offset shifts the silhouette that cuts out the shadow; shading appears on the opposite side. |
| Script-edited layers do not refresh | Call `RefreshEffects()` after list/entry edits. Do not mutate a shared render material. |
| Matching images do not batch | Check exact textures, size/pivot, slice mapping, effects, masks, Canvas and overlapping order. Different sprites are not atlased together. |
| New name is missing after installing | Update the package. Versions before 0.9.0 still display SDF Image; the package ID and URL stay the same. |

## Demo and testing

**Tools → SDF Outline → Create Demo Prefab** creates a separate sample in `Assets/SDFImageDemo`, with three source images that have SDF enabled and a prefab demonstrating outlines, shadows, glow, Sliced and RectMask2D. Wait for Ready, then drag the prefab into an empty scene. The command does not modify the open scene.

![SDF Outline overview: outer, inner and center outlines, shadow, glow, nine-slice and RectMask2D](Documentation~/sdf-outline-demo.png)

For UPM installations, import the **Outline and Shadow Demo** sample through Package Manager to try outlines, shadows, glow, nine-slice and RectMask2D. The `Samples~` folder is not imported automatically when copying the library into Assets.

Run `SDFUI.Tests` in Window → General → Test Runner → EditMode. For UPM installations, add the package to `testables` in the manifest and install Unity Test Framework. `SdfTextTests` requires **Window → TextMeshPro → Import TMP Essential Resources**; text rendering checks are skipped when the sample font or a GPU is unavailable. See [VALIDATION.md](VALIDATION.md) for recorded test results and limitations.

Reference: [SDF Image – Quality UI Outlines and Shadow](https://marketplace.unity.com/packages/tools/gui/sdf-image-quality-ui-outlines-and-shadow-244942). This is an independent implementation with the scope described above.

# SDF Image

Unity 6 / uGUI outlines and soft shadows for sprites and TextMeshPro labels. Sprite SDF baking runs asynchronously and stays embedded in the source sprites.

## Install

In Unity Package Manager, choose **Install package from Git URL**:

```text
https://github.com/phucnguyen752/sdf-image.git#upm
```

This URL follows the `upm` branch. After a new release, select **SDF Image** in Package Manager and click **Update**; keep the same URL. If you installed a version tag such as `#0.3.1`, use **Install package from Git URL** once with the `#upm` URL above to switch to this update flow. See [Unity's Git package update instructions](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-ui-update.html).

To keep this version, use `https://github.com/phucnguyen752/sdf-image.git#0.8.0` instead. Updating a pinned tag does not switch to a newer release tag.

Requires Unity 6000.0 and uGUI 2.0.0. The version tag and `upm` branch contain the package at the repository root. The `main` branch contains the Unity development project, with the library in `Assets/SDFImage`.

## Use

Create **GameObject → UI → SDF Image**, assign a source sprite, then select **Generate SDF** if needed. Edit **SDF Effects → Layers** for up to 16 outlines, shadows and glows with independent color, width, softness and offset. Drag layers to reorder them; the top layer is in front. Use **Underlay** for an offset shadow or glow. New images use one component derived from Unity Image, with all layers composed in one quad/material draw.

![SDF Outline overview: outer, inner and center outlines, shadow, glow, nine-slice and RectMask2D](Assets/SDFImage/Documentation~/sdf-outline-demo.png)

Enable **Use Texture Color** on a layer to color its effect from the sprite texture. **Intensity** controls brightness (`0` black, `1` original, above `1` brighter), while **Opacity** controls transparency. Editing effect layers does not rebake the sprite.

![Use Texture Color: gradient star, hollow ring and nine-sliced panel rendered in Unity URP](Assets/SDFImage/Documentation~/sdf-outline-texture-color-demo.png)

Select the source texture and open **SDF → Open SDF Import Settings** to edit bake settings and Unity's native platform compression controls. Distance maps use a single compressed channel and are padded to power-of-two dimensions without shrinking the artwork. Disable **Compress Distance** to retain full-precision RHalf data. **Clear SDF** removes generated data while preserving the source sprite and saved settings.

For text, create **GameObject → UI → SDF Text**, assign a TMP SDF font, and enable **Effects Enabled** below the standard TMP Inspector. `SdfText` derives from `TextMeshProUGUI`. Each layer's **Position** supports **Outer**, **Inner**, **Center** and **Underlay**. Inner draws an inset border over the glyph edge while keeping the stroke center visible; Center straddles the edge. Text remains editable at runtime and uses the existing font atlas; no sprite bake is needed. Font atlas padding limits effect width and softness.

Use one **Layers** list for outlines, shadows and glow, with independent position, color, width/spread, softness and offset. Existing and new layers default to **Underlay / Normal**, preserving the filled effect with signed spread. Choose Inner, Outer or Center for a border with positive Width. Underlay has **Underlay Type: Normal / Inner**: Normal draws behind the text; Inner casts a shadow inside the original glyph mask. Inner and Center borders also render over the text; Outer renders below it. Drag to reorder: within each group, the top list entry (lowest index) draws in front. Each active layer adds rendering cost for each font material in use.

Outer draws an exterior ring; Normal underlay includes the filled silhouette behind the text. Both Inner borders and Inner underlays stay masked by the original glyph, including holes, even with Offset and Softness. For Inner underlay, positive Spread reduces the shadow and negative Spread grows it.

SDF Text shares cached face/effect materials even across different layer styles. For a single font atlas, effects merge into one mesh per side of the text, preserving whole-layer order; compatible labels typically use two draws with effects on one side, or three with effects on both sides. Meshes update when geometry, TMP scale or style changes. Different font presets, atlases, Canvases, clipping and overlapping order can still split batches. Extra vertex data trades memory/bandwidth for fewer draws; profile the target device. Configure styles through the font preset and Layers, not the shared render material.

Static text skips full effect synchronization until it becomes dirty. Lightweight checks preserve transform/order and fades, while source materials are watched once per shared material. Call `RefreshEffects()` after editing the `Layers` list from code.

See the [0.8.0 performance comparison with TMP](Assets/SDFImage/Documentation~/Performance-0.8.0.md) for measured CPU, draw calls and memory tradeoffs.

![SDF Text Layers: stacked outlines, an offset shadow and reordered colors rendered in Unity URP](Assets/SDFImage/Documentation~/sdf-text-layers-demo.png)

Zero spread still renders an unexpanded Normal underlay, including migrated outlines that previously used width zero to hide. Inner underlay spread zero can still cast a shadow through Offset and Softness. Turn off the layer to hide it. Outer, Inner and Center borders are hidden at width zero.

Softness `0` still uses antialiasing. Enlarging low-resolution glyphs can leave rough contours; regenerate the font at a higher sampling size, with enough atlas space and padding for large labels and thick effects.

For an existing TMP label, create an **SDF Text** label and assign its font, content and layout settings, then update references to the new component. Automatic component conversion is not provided.

![SDF Text: tight spacing, colored outline and soft glow rendered in Unity URP](Assets/SDFImage/Documentation~/sdf-text-demo.png)

- [Usage, API, and limitations](Assets/SDFImage/README.md)
- [Validation results](Assets/SDFImage/VALIDATION.md)
- [Changelog](Assets/SDFImage/CHANGELOG.md)
- [Release workflow](Assets/SDFImage/Documentation~/Publishing.md)

## Development

Open this project with Unity **6000.0.83f1**. Run the `SDFUI.Tests` EditMode suite in Test Runner. Keep all library `.meta` files when moving or updating the package so existing components and sprites retain their identities.

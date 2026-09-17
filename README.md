# SDF Outline

Layered outlines, shadows and glow for **Unity 6 / uGUI** sprites and **TextMeshPro** labels.

Use **SDF Image** for sprites and **SDF Text** for editable text. Both expose a reorderable **Layers** list. Sprite baking runs asynchronously in the Editor; text uses the existing TMP font atlas.

![SDF Image: outline positions, soft shadow, glow, nine-slice and clipping, rendered in Unity](Assets/SDFImage/Documentation~/sdf-outline-demo.png)

## Install or update

Requires **Unity 6000.0+** and **uGUI 2.0.0** (includes TextMeshPro). In **Window → Package Manager**, choose **Install package from Git URL**:

```text
https://github.com/phucnguyen752/sdf-image.git#upm
```

Select **SDF Outline → Update** for later releases. An older installation may still appear as **SDF Image** until updated. The library name changed in **0.9.0**; the package ID `com.sdfimage.ugui`, component names, script GUIDs and installation URL are unchanged.

For a pinned version, use `https://github.com/phucnguyen752/sdf-image.git#0.9.0`. A pinned tag does not advance to a new release when you click Update; reinstall once with `#upm` to follow releases. Keep the suffix: `main` is the development project, while `upm` and version tags have the package at their root.

## SDF Image: start with a sprite

1. Create **GameObject → UI → SDF Image** and assign **Source Image**.
2. Click **Generate SDF** if needed. The original sprite remains visible while baking.
3. Open **SDF Effects → Layers**. Add effects with **+**, drag to reorder, and use each layer's checkbox to toggle it. The top entry draws in front.
4. Choose **Outer**, **Inner**, **Center** or **Underlay**; adjust **Color**, **Width/Spread**, **Softness** and **Offset**.

Up to **16 layers** are composed in one quad. Use Underlay for an offset shadow or a soft glow. Standard Image tint, alpha, Simple/Sliced, preserve aspect, masks and CanvasGroup remain available. Editing effects does not rebake the sprite.

Enable **Use Texture Color** to follow the artwork's edge colors. **Intensity** changes brightness (`0` black, `1` original, above `1` brighter); **Opacity** changes transparency.

![Texture-colored effects on a gradient star, hollow ring and sliced panel](Assets/SDFImage/Documentation~/sdf-outline-texture-color-demo.png)

Bake settings belong to the source texture. Select it and choose **SDF → Open SDF Import Settings** for Auto Update, Padding, Distance Range and platform compression. Generated color/distance textures stay embedded in the source asset; baking does not run in a player. [Full image and bake guide →](Assets/SDFImage/README.md#sdf-image-quick-start)

## SDF Text: start with a TMP font

1. Create **GameObject → UI → SDF Text**.
2. Assign a **TMP SDF font** and edit content, size and alignment as usual. Import TMP Essential Resources if Unity prompts for them.
3. Enable **SDF Effects → Effects Enabled** and edit **Layers** below the standard TMP Inspector. No sprite bake is needed.

| Layer setting | Result | Relative to the text face |
| --- | --- | --- |
| Outer | Border outside the glyph | Below |
| Inner | Border inside the glyph, clipped to its original shape | Above |
| Center | Border straddling the glyph edge | Above |
| Underlay → Normal | Filled silhouette for shadows or glow | Below |
| Underlay → Inner | Shadow clipped inside the original glyph, including holes | Above |

![Text position guide: Outer, Inner, Center, Normal underlay and Inner underlay rendered in Unity](Assets/SDFImage/Documentation~/sdf-text-modes-guide.png)

Within each above/below group, the **top layer (lowest index) is in front**. Inner borders and Inner underlays stay inside the original glyph mask when offset or softened. New text layers default to **Underlay / Normal**. Borders use positive Width; Underlay uses signed Spread. Disable a layer to hide it: zero Spread still renders a Normal underlay.

![Layered text: stacked borders, an offset shadow and layer ordering](Assets/SDFImage/Documentation~/sdf-text-layers-demo.png)

Text remains editable through `text`, `SetText` and the usual TMP APIs. After changing the `Layers` list from code, call **`RefreshEffects()`** on either component. Render materials are shared; configure the components, layers and font presets rather than editing temporary render materials. [Full text guide and API →](Assets/SDFImage/README.md#sdf-text-quick-start)

## Performance

**SDF Image** shares materials between images with matching baked textures, local drawing rectangle, slice mapping and effects. Position and Graphic tint/alpha can differ. Static images add no per-frame synchronization callback; material properties update when dirty. Different sprites, dimensions/pivots, styles, masks and draw order can still split batches. This is not a cross-sprite atlas.

Measured with **100 non-overlapping images**, one Canvas, Windows Development Player / URP:

| Scenario | Before 0.9.0 | 0.9.0 |
| --- | ---: | ---: |
| Same sprite, size and effects | 100 draws | **1 draw** |
| Four effect styles | 100 draws | **4 draws** |
| One shared stencil Mask | 102 draws | **3 draws** |
| Width animated on all 100 images: update + first Canvas cycle | 2.7129 ms | **1.3416 ms** |

**SDF Text** shares face/effect materials and merges layers into one effect mesh per side of the text. Compatible labels with one font atlas typically use **2 draws** with effects on one side, or **3** on both sides. Static text skips full effect synchronization until dirty. Presets, atlases, masks, Canvases and overlapping order can split batches.

Fewer draws do not remove shader work or overdraw. Text effects still add glyph geometry and vertex data; image layers add texture samples. These measurements are from desktop, **not Android/iOS device results**. See the [image benchmark and method](Assets/SDFImage/Documentation~/Performance-0.9.0.md) and [text comparison with TMP](Assets/SDFImage/Documentation~/Performance-0.8.0.md).

## Practical limits

- **Image:** Simple and Sliced with Fill Center enabled. Filled, Tiled and Sliced without Fill Center fall back to standard Image rendering.
- **Text:** Canvas-based `TextMeshProUGUI` with an SDF atlas. World-space 3D `TextMeshPro` and non-SDF custom font shaders are not supported.
- **Effect range:** increase sprite bake Padding/Distance Range or regenerate the TMP font atlas with more padding if effects stop expanding. Softness cannot restore detail missing from a low-resolution source.
- **Masks:** `Mask`, `RectMask2D` and CanvasGroup are supported. For SDF Text, keep Canvas/Mask/RectMask2D on ancestors, not on the text object itself.
- **Color:** lossy texture compression can change baked sprite colors. Choose uncompressed color storage when exact matching matters. Text is limited by its font atlas and material preset.

## Samples and documentation

Import **Outline and Shadow Demo** from the package's **Samples** tab, or run **Tools → SDF Outline → Create Demo Prefab**. The latter creates assets in `Assets/SDFImageDemo` without changing the open scene.

- [Complete usage, API and troubleshooting](Assets/SDFImage/README.md)
- [Validation results](Assets/SDFImage/VALIDATION.md)
- [Changelog](Assets/SDFImage/CHANGELOG.md)
- [Release workflow](Assets/SDFImage/Documentation~/Publishing.md)

All examples shown above are rendered by Unity. Development uses **Unity 6000.0.83f1** and **URP 17.0.4**. Run the `SDFUI.Tests` EditMode suite in Test Runner; keep library `.meta` files when moving or updating the package.

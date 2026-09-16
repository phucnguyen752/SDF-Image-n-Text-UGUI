# SDF Image

Unity 6 / uGUI outlines and soft shadows for sprites and TextMeshPro labels. Sprite SDF baking runs asynchronously and stays embedded in the source sprites.

## Install

In Unity Package Manager, choose **Install package from Git URL**:

```text
https://github.com/phucnguyen752/sdf-image.git#upm
```

This URL follows the `upm` branch. After a new release, select **SDF Image** in Package Manager and click **Update**; keep the same URL. If you installed a version tag such as `#0.3.1`, use **Install package from Git URL** once with the `#upm` URL above to switch to this update flow. See [Unity's Git package update instructions](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-ui-update.html).

To keep this version, use `https://github.com/phucnguyen752/sdf-image.git#0.6.0` instead. Updating a pinned tag does not switch to a newer release tag.

Requires Unity 6000.0 and uGUI 2.0.0. The version tag and `upm` branch contain the package at the repository root. The `main` branch contains the Unity development project, with the library in `Assets/SDFImage`.

## Use

Create **GameObject → UI → SDF Image**, assign a source sprite, then select **Generate SDF** if needed. Enable **Outline** or **Shadow** to edit that effect. New images use one component derived from Unity Image.

![SDF Outline overview: outer, inner and center outlines, shadow, glow, nine-slice and RectMask2D](Assets/SDFImage/Documentation~/sdf-outline-demo.png)

Enable **Outline → Use Texture Color** to color the outline from the sprite texture. **Intensity** controls brightness (`0` black, `1` original, above `1` brighter), while **Opacity** controls transparency. Existing SDF sprites do not need rebaking.

![Use Texture Color: gradient star, hollow ring and nine-sliced panel rendered in Unity URP](Assets/SDFImage/Documentation~/sdf-outline-texture-color-demo.png)

For text, create **GameObject → UI → SDF Text**, assign a TMP SDF font, and enable **Effects Enabled** below the standard TMP Inspector. `SdfText` derives from `TextMeshProUGUI` and draws all glyph effects behind the label's faces, so the outline of one character cannot cover its neighbour's face. Text remains editable at runtime and uses the existing font atlas; no sprite bake is needed. Font atlas padding limits effect spread and softness.

Use one **Layers** list for outlines, shadows and glow, with independent color, spread, softness and offset. Positive spread expands the shape, zero keeps its size, and negative spread contracts it. Use a dark offset layer for a shadow or a bright soft layer for glow. Drag to reorder: the top layer is in front, and every layer stays behind the text. Each active layer adds rendering cost for each font material in use.

![SDF Text Layers: stacked outlines, an offset shadow and reordered colors rendered in Unity URP](Assets/SDFImage/Documentation~/sdf-text-layers-demo.png)

Zero spread still renders an unexpanded layer, including migrated outlines that previously used width zero to hide. Turn off the layer to hide it.

Softness `0` still uses antialiasing. Enlarging low-resolution glyphs can leave rough contours; regenerate the font at a higher sampling size, with enough atlas space and padding for large labels and thick effects.

For an existing TMP label, create an **SDF Text** label and assign its font, content and layout settings, then update references to the new component. Automatic component conversion is not provided.

![SDF Text: tight spacing, colored outline and soft glow rendered in Unity URP](Assets/SDFImage/Documentation~/sdf-text-demo.png)

- [Usage, API, and limitations](Assets/SDFImage/README.md)
- [Validation results](Assets/SDFImage/VALIDATION.md)
- [Changelog](Assets/SDFImage/CHANGELOG.md)
- [Release workflow](Assets/SDFImage/Documentation~/Publishing.md)

## Development

Open this project with Unity **6000.0.83f1**. Run the `SDFUI.Tests` EditMode suite in Test Runner. Keep all library `.meta` files when moving or updating the package so existing components and sprites retain their identities.

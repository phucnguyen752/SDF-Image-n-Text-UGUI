# SDF Image

Outlines and shadows for **Unity 6 / uGUI (Canvas)** sprites and TextMeshPro labels. This standalone library uses uGUI 2.0 and its bundled TextMeshPro.

## Quick start

1. Create **GameObject → UI → SDF Image**. It uses a single `SdfImage` component derived from `UnityEngine.UI.Image`.
2. Assign the original sprite to **Source Image** on this component.
3. If the sprite has no SDF yet, click **Generate SDF**. The image continues to display normally while baking.
4. Standard Unity Image properties appear directly in the Inspector: **Source Image**, **Color**, **Material**, Raycast, Maskable, Image Type and its related options. These controls are always available, without a separate group or waiting for generation.
5. When **SDF ready**, the Inspector also shows **Outline** and **Shadow** below. Enable an effect to reveal its settings; disabling it preserves the values for later use. **SDF Settings** is collapsed by default.

Settings belong to the **source texture** and apply to every sprite in that texture. Assigning a regular sprite does not start a bake. Generate SDF enables **Auto Update**, so later changes to the image, import settings or SDF settings trigger an update. You can change Auto Update in SDF Settings, or enable Generate SDF in the source texture's Inspector.

Clicking **Cancel** or disabling Auto Update cancels queued and running work and prevents new jobs. Completed results remain available. To disable an effect, turn off **Outline** or **Shadow**; disabling both uses the standard Image rendering path.

In **Outline**, enable **Use Texture Color** to use the texture's RGB for the outline. **Intensity** `0` produces black, `1` keeps the original color, and values above `1` make it brighter. **Opacity** controls the outline's alpha separately. When disabled, the outline uses **Color** as before and retains your settings. Existing SDF sprites do not need rebaking.

![Use Texture Color: gradient star, hollow ring and nine-sliced panel rendered in Unity URP](Documentation~/sdf-outline-texture-color-demo.png)

All three examples use **Intensity 0.5** and **Opacity 1**; the outline follows the texture colors along each sprite's edges.

The legacy `SdfAutoBake` component is retained so older prefabs still load. Image adopts its saved source; **Remove Legacy Auto Bake** in the Inspector removes the redundant helper with Undo support. New objects do not need this helper.

## TextMeshPro

1. Create **GameObject → UI → SDF Text**. The `SdfText` component derives from `TextMeshProUGUI` and keeps the standard TMP Inspector for content, font, font size, alignment, spacing, auto size and rich text.
2. Assign a TMP font with an SDF atlas. There is no need to Generate SDF or bake text into sprites.
3. Enable **Effects Enabled** in **SDF Effects** below the Inspector. Spread, softness and offset use Canvas local units.

Use **+** and **−** in **Layers** to add or remove effects, and drag the handles to reorder them. The top layer is in front, closest to the text; the last layer is at the back. Each layer has its own enable toggle, Color, Spread, Softness and Offset. **Effects Enabled** controls the whole list and retains its settings when disabled.

![SDF Text Layers: stacked outlines, an offset shadow and reordered colors rendered in Unity URP](Documentation~/sdf-text-layers-demo.png)

Positive **Spread** expands the glyph shape, zero keeps its size, and negative values contract it. **Softness** blurs the edge. Use a dark layer with an offset for a shadow, or a bright soft layer with zero offset for glow. Outlines, shadows and glow share the same list and follow its order.

**Softness 0** still uses antialiasing. Enlarged contours from low-resolution glyphs can remain rough; regenerate the font at a higher sampling size, with enough atlas space and padding for large labels and thick effects. Softness adds blur but cannot restore missing glyph detail.

Reordering requires a single selected label. You can edit shared layer settings across multiple labels, and add or remove layers together when their layer counts match.

All effect layers are drawn behind all glyph faces. A later character's outline cannot cover its neighbour's face, even with tight spacing, fallback fonts or multiple materials. The component uses TMP's current mesh and font atlas and updates automatically when content, layout or fonts change at runtime. TMP's built-in Outline/Underlay/Glow effects are disabled on separate render materials; source fonts and materials remain unchanged.

![SDF Text: tight spacing, colored outline and soft glow rendered in Unity URP](Documentation~/sdf-text-demo.png)

These three examples use the same `SdfText` component: outline and shadow behind tightly spaced text, a colored outline on a multiline label, and glow from a shadow with no offset. The image was rendered directly in URP Linear. See the [tight-spacing checks and render report](VALIDATION.md).

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

`Layers` exposes a mutable `List<SdfTextEffect>`. Each entry has `Enabled`, signed `Width` (shown as Spread in the Inspector), `Softness`, `Color` and `Offset` properties. `Spread` is an alias for `Width`. After adding, removing, reordering or editing entries from code, call `RefreshEffects()`.

Existing outline settings migrate in their current order, followed by the old shadow as the back layer, with its settings and enabled state preserved. New labels start with an enabled outline layer and a disabled shadow layer. The earlier `Outline*` and `Shadow*` properties remain compatibility aliases for their migrated layers, including after reordering; use `Layers` and `EffectsEnabled` for new code.

Unlike the old single-outline component, width zero now renders an unexpanded effect. This also applies to migrated width-zero outlines; disable the layer to hide it.

- Supports `TextMeshProUGUI` on a Canvas only. 3D `TextMeshPro` and custom font shaders that do not use SDF are not supported.
- Spread and softness are limited by the font atlas's existing padding and distance range. If an effect stops expanding, regenerate the font atlas with more padding; sprite bake settings do not affect fonts.
- Supports ancestor Canvases, `Mask`, `RectMask2D` and `CanvasGroup` in the hierarchy. Place `Canvas`, `Mask` and `RectMask2D` on a parent object. Attaching them directly to the text object disables SDF effects.
- Each active effect layer adds a render layer and material for each font material in use. More layers or fallback fonts increase draw calls, while large effects increase overdraw.

## Textures embedded in the source sprite

The padded color texture, distance texture and `SdfSprite` descriptor are subassets of the **source image file itself**. The descriptor is also attached directly to the Sprite through Unity 6's `Sprite.AddScriptableObject` API; `SdfSprite.FromSprite(source)` retrieves it in the player.

No separate `.asset`, SDF PNG or baked-image folder is created in Assets. Temporary cache data lives in `Library/SDFImage` and does not need to be committed. Commit the source image, its `.meta` file and the library. On a new machine, Unity rebakes sources with SDF enabled during import. The original image file and its image import settings remain unchanged; SDF settings are added to `TextureImporter.userData` while preserving its existing contents.

`Image.sprite` keeps its reference to the original Sprite in the player to find the attached data. Baking does not run at runtime. Wait for Ready before building sources with Auto Update enabled. Build validation checks images in enabled scenes, Resources, preloaded assets and dependent prefabs. Sprites without generated SDF data use the standard Image renderer.

## Bounded, cancellable baking

- Downscales **before** calculating distances. Max Size defaults to 512, with a range of 64–1024. The source texture's dimensions are unchanged.
- Reads the GPU with `AsyncGPUReadback` and calculates distances on one worker in time linear to the pixel count. It does not perform synchronous GPU readback or wait for the worker on the main thread.
- Runs one job at a time. Changing settings cancels outdated results; only the current generation can be published.
- Caches by source, Sprite ID and settings. Reimporting valid data does not repeat the bake.
- Limits each texture to 128 sprites or 4 million texels after padding. Exceeding either limit reports an error asking you to reduce Max Size or padding before allocating large bake buffers.
- The main thread still creates GPU resources and publishes subassets through Unity's import process, which may briefly stall depending on the machine. There is no guarantee of zero stalls or any unmeasured frame rate.

The Editor needs a graphics device that supports AsyncGPUReadback. Running with `-nographics` cannot generate new SDF data. The GPU is used only to read the imported image, including its alpha and import settings; the source does not need Read/Write enabled.

## Effects and limitations

- Outer, inner and centered outlines with width, color and softness; shadows with offset, blur and spread. A bright shadow with zero offset creates a glow.
- **Use Texture Color** replaces outline RGB with texture RGB × Intensity, without multiplying by Outline Color or Image Color RGB. Alpha still uses Outline Color/Opacity and the overall Image alpha. Intensity does not change alpha.
- Preserves source RGB and alpha for the fill. `Graphic.color` tints the fill, while its alpha fades the entire image and effects once.
- Supports Simple, preserve aspect, nine-slice, layout and native size. The quad expands to avoid clipping outlines and shadows.
- Supports `Mask`, `RectMask2D` including softness, and `CanvasGroup`. Raycasts still use the original RectTransform.
- SDF Image supports Simple and Sliced with Fill Center enabled. Filled/radial fill, Tiled and Sliced with Fill Center disabled use the standard Unity Image renderer without SDF effects. Text is supported through `SdfText` as described above. SpriteRenderer, UI Toolkit and Coffee SoftMask/UIEffect are not integrated.

Width, softness, offset, blur and spread use **Canvas local units**. Padding and Distance Range use **pixels of the downscaled SDF image**. The shader limits effects to the available padding and distance range; increase both if an outline stops expanding. Shadow offset is independent of the distance limit. Canvas and object transforms scale the effects too.

The field uses linear RHalf, with positive distances inside the shape. The algorithm calculates the Euclidean distance to the opposite alpha class with a half-pixel correction. Alpha Threshold defines the boundary. This is a raster SDF, not vector reconstruction or MSDF; increasing Max Size helps preserve fine details.

Each image has its own material and does not batch with images using other materials. The shader takes three texture samples per fragment. Large shadows increase overdraw. Textures have no mipmaps or compression: RGBA32 + RHalf uses about 6 bytes per texel on the GPU, plus about 6 bytes per texel for CPU-readable data, excluding the source texture and overhead. A 256×256 image with padding 32 uses about 600 KiB each on the GPU and CPU.

## API

```csharp
using SDFUI;
using UnityEngine;

public sealed class ButtonStyle : MonoBehaviour
{
    [SerializeField] private SdfImage image;
    [SerializeField] private Sprite icon; // Generate SDF enabled in the Editor.

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

## Demo, installation and testing

**Tools → SDF Image → Create Demo Prefab** creates a separate sample in `Assets/SDFImageDemo`, with three source images that have SDF enabled and a prefab demonstrating outlines, shadows, glow, Sliced and RectMask2D. Wait for Ready, then drag the prefab into an empty scene. The command does not modify the open scene.

![SDF Outline overview: outer, inner and center outlines, shadow, glow, nine-slice and RectMask2D](Documentation~/sdf-outline-demo.png)

For UPM installations, import the **Outline and Shadow Demo** sample through Package Manager to try outlines, shadows, glow, nine-slice and RectMask2D. The `Samples~` folder is not imported automatically when copying the library into Assets.

In Package Manager, choose **Install package from Git URL** and enter:

```text
https://github.com/phucnguyen752/sdf-image.git#upm
```

This URL follows the `upm` branch. After each release, select **SDF Image** in Package Manager and click **Update**; keep the same URL and let Package Manager update the version. If you installed a tag such as `#0.3.1`, use **Install package from Git URL** once with the `#upm` URL above to switch to this update flow. See [Unity's Git package update instructions](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-ui-update.html).

To keep this version, use `https://github.com/phucnguyen752/sdf-image.git#0.6.0`. Clicking **Update** while using this tag will not switch to a newer release tag.

The `upm` branch and version tags contain the `com.sdfimage.ugui` package at the repository root; no `?path=` is needed. The `main` branch contains the full Unity project, with the library in `Assets/SDFImage`. Keep `#upm` in the URL because the default `main` branch does not have a package at its root.

You can also copy `Assets/SDFImage` with its `.meta` files into a Unity 6 project with uGUI 2.0, or keep a copy outside Assets and use Package Manager → Add package from disk with `package.json`. Keep only one installation. Source textures must be in Assets to save settings and import attached data. Shaders in Resources are included in builds. See [Publishing.md](Documentation~/Publishing.md) for the release workflow.

Namespaces and assemblies use `SDFUI`, `SDFUI.Editor` and `SDFUI.Tests.Editor`. When updating from version 0.2, update namespaces in your code and the package ID in the manifest; preserve script `.meta` files so existing components retain their identities. Move source settings and baked-data references together with the library. The component icon is a 64×64 PNG exported from the [source SVG](Documentation~/SdfImage.svg).

Run `SDFUI.Tests` in Window → General → Test Runner → EditMode. For UPM installations, add the package to `testables` in the manifest and install Unity Test Framework. `SdfTextTests` requires **Window → TextMeshPro → Import TMP Essential Resources**; text rendering checks are skipped when the sample font or a GPU is unavailable. See [VALIDATION.md](VALIDATION.md) for recorded test results and limitations.

Reference: [SDF Image – Quality UI Outlines and Shadow](https://marketplace.unity.com/packages/tools/gui/sdf-image-quality-ui-outlines-and-shadow-244942). This is an independent implementation with the scope described above.

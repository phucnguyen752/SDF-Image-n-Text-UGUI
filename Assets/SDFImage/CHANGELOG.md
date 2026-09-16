# Changelog

## 0.6.0 — 2026-09-16

- Unify SDF Text outlines, shadows and glow in one reorderable Layers list, with independent color, signed spread, softness, offset and enable controls. The top layer is in front; all layers stay behind the glyph faces.
- Add one Effects Enabled toggle for the entire list. Zero spread preserves the glyph shape; negative spread contracts it.
- Migrate existing SDF Text outline and shadow settings into the layer list.
- Correct SDF Text effect colors in Linear color space so layers match the selected colors instead of rendering too bright.
- Improve SDF Text edge antialiasing using both axes of the atlas pixel footprint, without extra texture samples or changing Softness.
- Add a dedicated SDF Text component icon.

## 0.5.0 — 2026-09-08

- Add optional texture-colored SdfImage outlines with adjustable intensity and separate opacity, reusing existing baked color data without rebaking. Fixed-color outlines remain the default.
- Replace the README sprite preview with a rendered three-card Use Texture Color showcase matching the SDF Text demo layout.

## 0.4.0 — 2026-09-08

- Add `SdfText`, a `TextMeshProUGUI` component with outline and soft shadow drawn behind all character faces, including fallback font and material submeshes.
- Reuse TMP meshes and SDF font atlases for dynamic text; expose Canvas-unit effect controls below the standard TMP Inspector.
- Keep thick outlines smooth at concave glyph corners with stable font-atlas distance scaling.
- Add **GameObject → UI → SDF Text** for creating TMP labels with SDF effects.
- Add rendered SDF Text examples to the package documentation and release notes.

## 0.3.1 — 2026-09-07

- Add package author name `Phuc Nguyen` and GitHub profile URL so Unity Package Manager can display the author.

## 0.3.0 — 2026-09-07

- Rename the distribution to `Assets/SDFImage`, package `com.sdfimage.ugui`, and namespace/assemblies `SDFUI`; update resources, cache, import metadata, demo paths, and documentation.
- Preserve script GUIDs and migrate source opt-in metadata, cached data, and serialized generated-object references in the consuming project.
- Keep translucent interiors intact when SDF images are stretched unevenly.
- Keep standard Image layout sizes stable when effects are enabled or disabled, and reuse sprite attachment lookup during style animation.
- Repair cache publication state, include Resources/preloaded dependencies in build checks, and preserve other tools' appended importer metadata.
- Track published cache data through importer custom dependencies to keep restored and generated artifacts consistent.
- Repaint source inspectors only while generation is active.

## 0.2.0 — 2026-09-07

- Rename the package display name, component, and menus to SDF Image.
- Add a custom 64x64 component icon exported from editable SVG, stored with the script importer for portable Editor display.
- Inherit Unity Image and accept source sprites directly; no separate Auto Bake component for new images.
- Keep Unity Image controls visible in their standard Inspector layout, with Generate and effect controls below.
- Add Outline/Shadow toggles, preserving style settings when disabled; fold less-used settings away.
- Support standard sprite/overrideSprite changes and normal Image rendering when SDF is unavailable or the image mode does not support SDF.
- Preserve old baked references and offer Undo-enabled removal of the legacy helper.
- Fix dark seams between antialiased sprite edges and outlines without rebaking textures.

## 0.1.0 — 2026-09-07

- Unity 6 uGUI image with outer/inner/center outlines, shadow, glow, Simple and Sliced rendering.
- Source Sprite binding through SdfAutoBake and opt-in Generate SDF on the source texture.
- Bounded asynchronous GPU readback, cancelable CPU distance transform, and Library cache.
- Generated color/distance/descriptor subassets in the original source image; descriptor attached to its Sprite.
- Stable generated references, automatic refresh, and preservation of completed data when generation is disabled.
- Inspectors, demo generation, build readiness checks, and algorithm/import/GPU rendering tests.

# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Package skeleton.
- Tree generator: trunk with flare, up to three branch levels, and leaf cards (single or crossed), all driven by a seed.
- `TreeWizardTree` component that rebuilds the mesh live as settings change.
- Leaf atlases: `LeafSheetSlicer` finds each leaf or twig on transparent sheets, cleans background fringes and packs an atlas; `LeafAtlasBuilder` writes it as a texture plus `LeafAtlas` asset. Cards take each cell's proportions and stem position.
- Leaf **Outward** setting: turns cards from facing the sky to facing away from the trunk, for fuller crowns from the side.
- Leaf **Canopy Normals** setting: bends leaf lighting out from the crown's centre so canopies shade as one rounded mass. Carried through FBX export.
- Autumn setting: the share of leaves drawn from an atlas's autumn-coloured cells. Greens through blue-greens count as summer.
- Oak leaf atlas preset (36 summer and autumn twigs).
- Spruce preset: conical tiered shape, 18 spruce sprays, spruce bark.
- Bark textures for both presets (seamless tiles, stored as JPG), with normal maps.
- `NormalMapGenerator`: builds seamless tangent-space normal maps from colour textures (brightness as height); runs in builds too.
- FBX export includes bark normal maps (`FbxMaterial.NormalTexture`), picked up by Unity's and Blender's importers. Opaque textures are exported without an alpha channel.
- Tree presets (`TreePreset`): shape settings plus bark material, leaf material and leaf atlas. Ships with an Oak preset.
- **GameObject > 3D Object > TreeWizard Tree** creates a tree from the Oak preset.
- Tree inspector: preset picker, seed randomizer, foldable sections, add/remove branch levels, live triangle counts, Save as Preset and Bake to Prefab.
- `TreeWizard.Fbx`: a standalone, engine-free binary FBX 7.4 writer (static meshes, materials, textures, model hierarchy), checked against Unity's and Blender's importers.
- Export FBX: `TreeFbxExporter` (works in the Editor and in builds) and an **Export FBX…** inspector button. Textures are saved as PNGs beside the file.
- `TreeSettingsPanel`: runtime UI Toolkit editor for tree settings, generated from the same attributes as the inspector, for building tree editing into apps.
- Bake to Prefab writes a static prefab with the mesh embedded; re-baking updates the mesh in place so existing references keep working.

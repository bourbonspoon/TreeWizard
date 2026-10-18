# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Package skeleton.
- Tree generator: trunk with flare, up to three branch levels, and leaf cards (single or crossed), all driven by a seed.
- `TreeWizardTree` component that rebuilds the mesh live as settings change.
- Leaf atlases: `LeafSheetSlicer` finds each leaf or twig on transparent sheets, cleans background fringes and packs an atlas; `LeafAtlasBuilder` writes it as a texture plus `LeafAtlas` asset. Cards take each cell's proportions and stem position.
- Leaf **Outward** setting: turns cards from facing the sky to facing away from the trunk, for fuller crowns from the side.
- **Roots**: shallow surface roots spreading from the base, low broad ridges that snake outward and sink into the ground along their length, so trees sit naturally on detailed terrain. Off by default; the presets have them. Wind no longer moves anything near the ground.
- **Flatten** setting on branch levels and leaves: spreads them flat to either side of their parent instead of spiralling around it, for layered conifer sprays and fern-like fronds.
- Leaf **Canopy Normals** setting: bends leaf lighting out from the crown's centre so canopies shade as one rounded mass. Carried through FBX export.
- Autumn setting: the share of leaves drawn from an atlas's autumn-coloured cells. Greens through blue-greens count as summer.
- **Leaf Mix**: a thumbnail and weight slider per leaf atlas cell, in the inspector and the app (`LeafMixPanel`). Weights are stored on the tree (`LeafSettings.cellWeights`), so atlases stay shared and untouched; 0 leaves a cell out.
- `SnapshotHistory`: undo/redo over whole-document snapshots, merging quick runs of changes such as a slider drag into one step. `TreeSettingsPanel` now keeps sections open or closed across rebuilds.
- `TextureImport`: loads PNG or JPG bark, normal maps and leaf sheets at runtime, for apps where people bring their own textures. Leaf atlases get mipmaps that keep the same share of solid pixels at every level, so alpha-clipped leaves don't thin out with distance.
- Oak leaf atlas preset (36 summer and autumn twigs).
- Spruce preset: conical tiered shape with flat, drooping twigs covered in small spruce tips (fishbone style), 18 spruce sprays, spruce bark.
- Beech preset: smooth grey trunk under a broad, dense dome, with layered, slightly drooping sprays. 24 beech sprigs including yellow, copper and dry autumn ones.
- Bark textures for both presets (seamless tiles, stored as JPG), with normal maps.
- `NormalMapGenerator`: builds seamless tangent-space normal maps from colour textures (brightness as height); runs in builds too.
- FBX export includes bark normal maps (`FbxMaterial.NormalTexture`), picked up by Unity's and Blender's importers. A normal map loaded at runtime is exported as it is; imported ones are rebuilt from the bark. Opaque textures are exported without an alpha channel.
- `TreeWizard.FileBrowser`: a standalone UI Toolkit open/save/folder dialog for built players. It has places, drives and recent folders, breadcrumbs, sortable columns, search, filters and keyboard navigation, and works the same on Windows, macOS and Linux.
- Tree presets (`TreePreset`): shape settings plus bark material, leaf material and leaf atlas. Ships with an Oak preset.
- **GameObject > 3D Object > TreeWizard Tree** creates a tree from the Oak preset.
- Tree inspector: preset picker, seed randomizer, foldable sections, add/remove branch levels, live triangle counts, Save as Preset and Bake to Prefab.
- `TreeWizard.Fbx`: a standalone, engine-free binary FBX 7.4 writer (static meshes, materials, textures, model hierarchy), checked against Unity's and Blender's importers.
- Export FBX: `TreeFbxExporter` (works in the Editor and in builds) and an **Export FBX…** inspector button. Textures are saved as PNGs beside the file.
- `TreeSettingsPanel`: runtime UI Toolkit editor for tree settings, generated from the same attributes as the inspector, for building tree editing into apps.
- `TreeWizard/Tree` shader (URP Shader Graph): Lit surface plus wind (trunk sway that grows with height, branch bob, leaf flutter). Wind works from object-space position, so baked prefabs and imported FBX trees sway too. Presets use it.
- `TreeWizardWind` component (GameObject > TreeWizard > Wind): sets wind direction, strength and speed for all trees, optionally following a WindZone.
- Bake to Prefab writes a static prefab with the mesh embedded; re-baking updates the mesh in place so existing references keep working.

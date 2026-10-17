# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Package skeleton.
- Tree generator: trunk with flare, up to three branch levels, and leaf cards (single or crossed), all driven by a seed.
- `TreeWizardTree` component that rebuilds the mesh live as settings change.
- Leaf atlases: `LeafSheetSlicer` finds each leaf or twig on transparent sheets, cleans background fringes and packs an atlas; `LeafAtlasBuilder` writes it as a texture plus `LeafAtlas` asset. Cards take each cell's proportions and stem position.
- Autumn setting: the share of leaves drawn from an atlas's autumn-coloured cells.
- Oak leaf atlas preset (36 summer and autumn twigs).

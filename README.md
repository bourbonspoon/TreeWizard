# TreeWizard

A free, open-source procedural tree generator, in the spirit of SpeedTree.

- **Unity package**: build trees inside the Unity Editor from a stack of trunk, branch and leaf generators, then bake meshes, materials and LODs.
- **Standalone app**: the same generator as a desktop app that exports trees to FBX for any engine or DCC tool.

> Early development: usable, but expect changes.

## Features

- Trunk, up to three branch levels and leaf cards, all driven by a seed. Shallow surface roots. Oak, Spruce and Beech presets.
- Leaf atlases sliced automatically from leaf or twig sheets, with summer/autumn mixing.
- Bark textures with generated normal maps.
- `TreeWizard/Tree` URP shader with wind (sway, branch bob, leaf flutter). Add **GameObject > TreeWizard > Wind** to a scene.
- Unity: **GameObject > 3D Object > TreeWizard Tree**, live inspector, Bake to Prefab, Export FBX.
- Standalone app: design trees, use your own bark and leaf sheet images and choose the leaf mix, save/open designs with undo/redo and unsaved-change prompts, export FBX with textures. The UI follows the display scale (Retina, Windows scaling); Cmd/Ctrl + and - resize it, 0 resets.
- Reusable pieces with their own assemblies and no dependency on the rest of TreeWizard: a binary FBX writer (`Fbx/`) and a UI Toolkit file browser for built players (`FileBrowser/`).

## Install (Unity package)

Package Manager → **+** → *Install package from git URL…*

```
https://github.com/bourbonspoon/TreeWizard.git?path=/TreeWizard/Packages/com.treewizard.core
```

Requires Unity 6000.7 or newer.

## Repository layout

| Path | What |
|---|---|
| `TreeWizard/` | Unity host project (URP), which also builds the standalone app |
| `TreeWizard/Packages/com.treewizard.core/` | The UPM package: runtime generator, editor tooling, tests |

## Building the standalone app

Open `TreeWizard/` in Unity 6000.7 or newer, then use **TreeWizard > Dev > Build macOS App** or **Build Windows App**. Builds go to `TreeWizard/Builds/`.

To smoke-test a build, run it with `-smokeTest <folder>`. It screenshots each preset, saves a design and exports FBX into the folder, then quits with exit code 0 (pass) or 1 (fail):

```
# macOS
TreeWizard.app/Contents/MacOS/TreeWizard -smokeTest ~/Desktop/tw-smoke
# Windows
TreeWizard.exe -smokeTest %USERPROFILE%\Desktop\tw-smoke
```

## License

[MIT](LICENSE)

# TreeWizard

A free, open-source procedural tree generator, in the spirit of SpeedTree.

- **Unity package**: build trees inside the Unity Editor from a stack of trunk, branch and leaf generators, then bake meshes, materials and LODs.
- **Standalone app**: the same generator as a desktop app that exports trees to FBX for any engine or DCC tool.

> Early development. Nothing works yet.

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

## License

[MIT](LICENSE)

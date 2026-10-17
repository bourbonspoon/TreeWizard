# TreeWizard.Fbx

A small binary FBX 7.4 writer with no Unity dependency. The assembly sets `noEngineReferences` and uses `System.Numerics` types, so it works in any Unity project, in built players, or in plain .NET.

It writes static meshes: positions, normals, one UV set, per-polygon materials, diffuse colour and diffuse textures, inside a hierarchy of models. Animation, skinning, cameras and lights are not supported.

The output is checked by importing it through Unity's FBX importer and through Blender's.

```csharp
using System.Numerics;
using TreeWizard.Fbx;

var mesh = FbxMesh.FromSubmeshes("Quad",
    positions: new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) },
    normals: null,
    uvs: null,
    submeshTriangles: new[] { new[] { 0, 1, 2, 0, 2, 3 } });

var model = new FbxModel("Quad") { Mesh = mesh };
model.Materials.Add(new FbxMaterial("Paint") { DiffuseTexture = "paint.png" });

var scene = new FbxScene();
scene.Models.Add(model);

// From Unity data, set ConvertFromLeftHanded: X is mirrored and winding reversed,
// and importers convert back.
FbxSceneWriter.WriteFile("quad.fbx", scene, new FbxWriteOptions { ConvertFromLeftHanded = true });
```

Notes:
- Units: metres by default (`UnitScaleFactor = 100`, i.e. centimetres per unit). Axes: Y up.
- Colours are linear, which is how Unity and Blender read them.
- `FbxMaterial.AlphaFromDiffuseTexture` also links the texture to transparency. Blender uses it for cutouts. Unity imports the material as transparent, so switch it to alpha clipping for foliage.
- For other FBX content, `FbxBinaryWriter` writes any tree of `FbxNode` records.

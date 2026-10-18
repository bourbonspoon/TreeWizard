using System.Collections.Generic;
using System.Numerics;

namespace TreeWizard.Fbx
{
    /// <summary>A simple scene to write as FBX: a hierarchy of models, each with an optional mesh and materials.</summary>
    public sealed class FbxScene
    {
        public string Creator = "TreeWizard";
        public readonly List<FbxModel> Models = new List<FbxModel>();
    }

    public sealed class FbxModel
    {
        public string Name;
        public Vector3 Translation;
        /// <summary>Euler angles in degrees, applied X then Y then Z.</summary>
        public Vector3 Rotation;
        public Vector3 Scaling = Vector3.One;
        /// <summary>Optional. A model without a mesh is an empty transform.</summary>
        public FbxMesh Mesh;
        /// <summary>Indexed by <see cref="FbxMesh.PolygonMaterials"/>.</summary>
        public readonly List<FbxMaterial> Materials = new List<FbxMaterial>();
        public readonly List<FbxModel> Children = new List<FbxModel>();

        public FbxModel(string name) => Name = name;
    }

    public sealed class FbxMesh
    {
        public string Name;
        public Vector3[] Positions;
        /// <summary>Optional, one per position.</summary>
        public Vector3[] Normals;
        /// <summary>Optional, one per position.</summary>
        public Vector2[] Uvs;
        /// <summary>Polygon corners as indices into <see cref="Positions"/>, polygon after polygon.</summary>
        public int[] Indices;
        /// <summary>Corner count of each polygon. Null means every polygon is a triangle.</summary>
        public int[] PolygonSizes;
        /// <summary>Material index of each polygon. Null means every polygon uses material 0.</summary>
        public int[] PolygonMaterials;

        public int PolygonCount => PolygonSizes?.Length ?? Indices.Length / 3;

        /// <summary>Builds a triangle mesh from per-material triangle lists, as Unity stores submeshes.</summary>
        public static FbxMesh FromSubmeshes(string name, Vector3[] positions, Vector3[] normals, Vector2[] uvs,
            IReadOnlyList<IReadOnlyList<int>> submeshTriangles)
        {
            var indices = new List<int>();
            var materials = new List<int>();
            for (var s = 0; s < submeshTriangles.Count; s++)
            {
                var tris = submeshTriangles[s];
                indices.AddRange(tris);
                for (var t = 0; t < tris.Count / 3; t++)
                    materials.Add(s);
            }
            return new FbxMesh
            {
                Name = name,
                Positions = positions,
                Normals = normals,
                Uvs = uvs,
                Indices = indices.ToArray(),
                PolygonMaterials = materials.ToArray(),
            };
        }
    }

    public sealed class FbxMaterial
    {
        public string Name;
        /// <summary>Linear RGB, 0..1. Unity's and Blender's importers both read FBX colours as linear.</summary>
        public Vector3 DiffuseColor = new Vector3(0.8f, 0.8f, 0.8f);
        /// <summary>Optional image file, relative to the FBX file or absolute.</summary>
        public string DiffuseTexture;
        /// <summary>Also use the diffuse texture's alpha as transparency (leaves, cutouts).</summary>
        public bool AlphaFromDiffuseTexture;
        /// <summary>Optional tangent-space normal map (+Y up), relative to the FBX file or absolute.</summary>
        public string NormalTexture;

        public FbxMaterial(string name) => Name = name;
    }

    public sealed class FbxWriteOptions
    {
        /// <summary>
        /// Input is left-handed (Unity): mirror X and reverse polygon winding to convert to FBX's
        /// right-handed space. Importers into left-handed engines convert back.
        /// </summary>
        public bool ConvertFromLeftHanded;

        /// <summary>zlib-compress large arrays. Smaller files; every importer supports it.</summary>
        public bool CompressArrays = true;

        /// <summary>Centimetres per scene unit. 100 means the scene is in metres.</summary>
        public double UnitScaleFactor = 100;
    }
}

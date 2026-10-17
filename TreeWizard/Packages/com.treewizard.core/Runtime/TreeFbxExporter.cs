using System.IO;
using System.Linq;
using TreeWizard.Fbx;
using UnityEngine;
using N = System.Numerics;

namespace TreeWizard
{
    /// <summary>
    /// Exports trees as FBX, with each material's texture saved as a PNG beside the file.
    /// Works in the Editor and in built players.
    /// </summary>
    public static class TreeFbxExporter
    {
        /// <summary>Writes <paramref name="tree"/> to <paramref name="fbxPath"/>, plus NAME_Bark.png and NAME_Leaves.png when its materials have textures.</summary>
        public static void Export(TreeWizardTree tree, string fbxPath)
        {
            var data = TreeGenerator.Generate(tree.definition, tree.leafAtlas != null ? tree.leafAtlas.cells : null);
            Export(data, fbxPath, tree.barkMaterial, tree.leafMaterial);
        }

        public static void Export(MeshData data, string fbxPath, Material bark, Material leaves)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(fbxPath));
            var name = Path.GetFileNameWithoutExtension(fbxPath);

            var mesh = FbxMesh.FromSubmeshes(name,
                data.vertices.Select(v => new N.Vector3(v.x, v.y, v.z)).ToArray(),
                data.normals.Select(n => new N.Vector3(n.x, n.y, n.z)).ToArray(),
                data.uvs.Select(uv => new N.Vector2(uv.x, uv.y)).ToArray(),
                new[] { data.Triangles(MeshData.BarkSubmesh), data.Triangles(MeshData.LeafSubmesh) });
            var model = new FbxModel(name) { Mesh = mesh };
            model.Materials.Add(ToFbxMaterial(bark, $"{name}_Bark", directory, false));
            model.Materials.Add(ToFbxMaterial(leaves, $"{name}_Leaves", directory, true));

            var scene = new FbxScene();
            scene.Models.Add(model);
            FbxSceneWriter.WriteFile(fbxPath, scene, new FbxWriteOptions { ConvertFromLeftHanded = true });
        }

        static FbxMaterial ToFbxMaterial(Material source, string name, string directory, bool cutout)
        {
            var material = new FbxMaterial(name);
            if (source == null)
                return material;

            var color = MainColor(source).linear; // FBX colours are linear
            material.DiffuseColor = new N.Vector3(color.r, color.g, color.b);
            if (source.mainTexture != null)
            {
                var file = name + ".png";
                SaveTexturePng(source.mainTexture, Path.Combine(directory, file));
                material.DiffuseTexture = file;
                material.AlphaFromDiffuseTexture = cutout;
            }
            return material;
        }

        static Color MainColor(Material material)
        {
            foreach (var property in new[] { "_BaseColor", "_Color" })
            {
                if (material.HasProperty(property))
                    return material.GetColor(property);
            }
            return Color.white;
        }

        /// <summary>
        /// Saves any texture as PNG, including ones that aren't CPU-readable or are GPU-compressed,
        /// by drawing it into a render texture and reading that back.
        /// </summary>
        public static void SaveTexturePng(Texture texture, string path)
        {
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readable.Apply();
                File.WriteAllBytes(path, readable.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(readable);
            }
        }
    }
}

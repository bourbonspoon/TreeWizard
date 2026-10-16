using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TreeWizard
{
    /// <summary>
    /// Engine-neutral mesh: positions, normals, one UV set, and a triangle list per material.
    /// Exporters read this directly, and <see cref="ApplyTo"/> turns it into a Unity mesh.
    /// Triangles wind clockwise when seen from the front, as Unity expects.
    /// </summary>
    public class MeshData
    {
        public const int BarkSubmesh = 0;
        public const int LeafSubmesh = 1;
        public const int SubmeshCount = 2;

        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int>[] _triangles = { new List<int>(), new List<int>() };

        public List<int> Triangles(int submesh) => _triangles[submesh];

        public int TriangleCount
        {
            get
            {
                var count = 0;
                foreach (var t in _triangles)
                    count += t.Count;
                return count / 3;
            }
        }

        public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            vertices.Add(position);
            normals.Add(normal);
            uvs.Add(uv);
            return vertices.Count - 1;
        }

        public void ApplyTo(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = SubmeshCount;
            for (var i = 0; i < SubmeshCount; i++)
                mesh.SetTriangles(_triangles[i], i, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }
    }
}

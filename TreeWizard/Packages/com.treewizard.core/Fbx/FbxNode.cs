using System.Collections.Generic;

namespace TreeWizard.Fbx
{
    /// <summary>
    /// One record in an FBX document: a name, a list of typed values and nested records.
    /// Property values map to FBX types by their .NET type:
    /// short Y, bool C, int I, float F, double D, long L, string S, byte[] R,
    /// float[] f, double[] d, long[] l, int[] i, bool[] b.
    /// </summary>
    public sealed class FbxNode
    {
        public readonly string Name;
        public readonly List<object> Properties = new List<object>();
        public readonly List<FbxNode> Children = new List<FbxNode>();

        public FbxNode(string name, params object[] properties)
        {
            Name = name;
            Properties.AddRange(properties);
        }

        /// <summary>Adds a child node and returns it.</summary>
        public FbxNode Add(string name, params object[] properties)
        {
            var child = new FbxNode(name, properties);
            Children.Add(child);
            return child;
        }

        public FbxNode Add(FbxNode child)
        {
            Children.Add(child);
            return child;
        }

        public FbxNode Find(string name) => Children.Find(c => c.Name == name);

        public override string ToString() => $"{Name} ({Properties.Count} properties, {Children.Count} children)";
    }
}

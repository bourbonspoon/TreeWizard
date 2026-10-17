using System.Collections.Generic;

namespace TreeWizard
{
    /// <summary>Entry point: definition in, mesh out.</summary>
    public static class TreeGenerator
    {
        /// <param name="leafCells">Leaf atlas cells (see <see cref="LeafAtlas"/>), or null to map the whole leaf texture onto every card.</param>
        public static MeshData Generate(TreeDefinition definition, IReadOnlyList<LeafCell> leafCells = null) =>
            TreeMeshBuilder.Build(TreeSkeleton.Generate(definition, leafCells), definition, leafCells);
    }
}

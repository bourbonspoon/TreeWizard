namespace TreeWizard
{
    /// <summary>Entry point: definition in, mesh out.</summary>
    public static class TreeGenerator
    {
        public static MeshData Generate(TreeDefinition definition) =>
            TreeMeshBuilder.Build(TreeSkeleton.Generate(definition), definition);
    }
}

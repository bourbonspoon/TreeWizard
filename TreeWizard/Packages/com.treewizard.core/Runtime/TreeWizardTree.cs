using UnityEngine;

namespace TreeWizard
{
    /// <summary>
    /// Generates a tree mesh from <see cref="definition"/> and keeps it in sync as settings change,
    /// in edit mode and play mode. The mesh is never saved with the scene; it is rebuilt on load.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TreeWizardTree : MonoBehaviour
    {
        public TreeDefinition definition = new TreeDefinition();
        public Material barkMaterial;
        public Material leafMaterial;

        [Tooltip("Where each leaf sits in the leaf material's texture. Empty maps the whole texture onto every card.")]
        public LeafAtlas leafAtlas;

        Mesh _mesh;
        bool _dirty = true;

        /// <summary>The generated mesh, or null before the first build.</summary>
        public Mesh Mesh => _mesh;

        void OnEnable() => MarkDirty();

        void OnValidate() => MarkDirty();

        void Update()
        {
            if (_dirty)
                Rebuild();
        }

        void OnDisable()
        {
            if (_mesh == null)
                return;
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
            _mesh = null;
        }

        public void MarkDirty()
        {
            _dirty = true;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        public void Rebuild()
        {
            _dirty = false;
            if (_mesh == null)
                _mesh = new Mesh { name = "TreeWizard Tree", hideFlags = HideFlags.DontSave };

            TreeGenerator.Generate(definition, leafAtlas != null ? leafAtlas.cells : null).ApplyTo(_mesh);
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            GetComponent<MeshRenderer>().sharedMaterials = new[] { barkMaterial, leafMaterial };
        }
    }
}

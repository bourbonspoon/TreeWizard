using UnityEngine;

namespace TreeWizard
{
    /// <summary>A saved tree: shape settings plus the materials and leaf atlas that go with them.</summary>
    [CreateAssetMenu(menuName = "TreeWizard/Tree Preset", fileName = "TreePreset")]
    public class TreePreset : ScriptableObject
    {
        public TreeDefinition definition = new TreeDefinition();
        public Material barkMaterial;
        public Material leafMaterial;
        public LeafAtlas leafAtlas;
    }
}

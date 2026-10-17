using System;
using System.Collections.Generic;
using UnityEngine;

namespace TreeWizard
{
    /// <summary>One leaf or twig image within a leaf texture.</summary>
    [Serializable]
    public struct LeafCell
    {
        [Tooltip("Area of the texture this cell covers, in UV space.")]
        public Rect uvRect;

        [Tooltip("Where the stem is, 0..1 within the cell. This point sits on the branch.")]
        public Vector2 pivot;

        [Tooltip("Width divided by height.")]
        public float aspect;

        [Tooltip("Autumn colours. Picked according to the tree's autumn setting.")]
        public bool autumn;

        [Tooltip("Relative chance of being picked. 0 disables the cell.")]
        [Min(0f)] public float weight;

        /// <summary>The whole texture as one cell, stem at the bottom middle.</summary>
        public static LeafCell FullTexture => new LeafCell
        {
            uvRect = new Rect(0f, 0f, 1f, 1f),
            pivot = new Vector2(0.5f, 0f),
            aspect = 1f,
            weight = 1f,
        };
    }

    /// <summary>A leaf texture plus where each leaf sits in it. Build one with the Leaf Atlas Builder.</summary>
    [CreateAssetMenu(menuName = "TreeWizard/Leaf Atlas", fileName = "LeafAtlas")]
    public class LeafAtlas : ScriptableObject
    {
        public Texture2D texture;
        public List<LeafCell> cells = new List<LeafCell>();
    }
}

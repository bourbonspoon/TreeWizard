using System;
using UnityEngine;

namespace TreeWizard
{
    /// <summary>
    /// Everything needed to generate a tree. Plain serializable data, so it works in the
    /// inspector, in ScriptableObjects and as JSON (JsonUtility) in the standalone app.
    /// </summary>
    [Serializable]
    public class TreeDefinition
    {
        public const int MaxBranchLevels = 3;

        [Tooltip("Same seed and settings always give the same tree.")]
        public int seed = 1;

        public TrunkSettings trunk = new TrunkSettings();
        public RootSettings roots = new RootSettings();

        [Tooltip("Up to three levels. Each level grows off every branch of the level before it.")]
        public BranchLevelSettings[] branchLevels =
        {
            new BranchLevelSettings(),
            new BranchLevelSettings
            {
                count = 6,
                start = 0.2f,
                angle = 40f,
                length = 0.4f,
                lengthAtEnd = 0.6f,
                radius = 0.6f,
                gravity = 0.05f,
                segments = 4,
            },
        };

        public LeafSettings leaves = new LeafSettings();
        public MeshSettings mesh = new MeshSettings();

        /// <summary>Deep copy.</summary>
        public TreeDefinition Clone() => JsonUtility.FromJson<TreeDefinition>(JsonUtility.ToJson(this));
    }

    [Serializable]
    public class TrunkSettings
    {
        [Min(0.1f)] public float height = 6f;

        [Tooltip("Radius at the base, above the flare.")]
        [Min(0.01f)] public float radius = 0.25f;

        [Tooltip("Radius at the top, as a fraction of the base radius.")]
        [Range(0f, 1f)] public float tipRadius = 0.1f;

        [Tooltip("Extra radius at ground level, as a fraction of the base radius.")]
        [Range(0f, 2f)] public float flare = 0.6f;

        [Tooltip("How far up the flare reaches, as a fraction of the height.")]
        [Range(0f, 0.5f)] public float flareHeight = 0.1f;

        [Range(0f, 1f)] public float crookedness = 0.15f;

        [Tooltip("Rings along the trunk.")]
        [Range(2, 64)] public int segments = 10;
    }

    [Serializable]
    public class RootSettings
    {
        [Tooltip("Shallow roots spreading from the base, sinking into the ground along their length so they blend with terrain. 0 for none.")]
        [Range(0, 16)] public int count;

        [Tooltip("How far roots reach, as a multiple of the trunk's base radius.")]
        [Range(0.5f, 20f)] public float length = 6f;

        [Range(0f, 1f)] public float lengthVariance = 0.3f;

        [Tooltip("Thickness where a root leaves the trunk, as a fraction of the trunk's base radius.")]
        [Range(0.05f, 1f)] public float radius = 0.5f;

        [Tooltip("How soon roots sink into the ground. 0: half buried about halfway along. 1: under the surface by halfway. Tips are always buried.")]
        [Range(0f, 1f)] public float depth = 0.3f;

        [Tooltip("Snaking from side to side.")]
        [Range(0f, 1f)] public float crookedness = 0.5f;

        [Tooltip("Rings along each root.")]
        [Range(2, 16)] public int segments = 6;
    }

    [Serializable]
    public class BranchLevelSettings
    {
        [Tooltip("Branches per parent branch.")]
        [Min(0)] public int count = 14;

        [Tooltip("Where branches start along the parent (0 = base, 1 = tip).")]
        [Range(0f, 1f)] public float start = 0.3f;

        [Tooltip("Where branches stop along the parent (0 = base, 1 = tip).")]
        [Range(0f, 1f)] public float end = 0.95f;

        [Tooltip("Angle away from the parent, in degrees.")]
        [Range(0f, 180f)] public float angle = 55f;

        [Range(0f, 90f)] public float angleVariance = 10f;

        [Tooltip("0: branches spiral all around the parent. 1: they spread flat to either side, giving layered sprays like a spruce. Has no effect on a vertical parent.")]
        [Range(0f, 1f)] public float flatten;

        [Tooltip("Length as a fraction of the parent's length.")]
        [Range(0f, 2f)] public float length = 0.45f;

        [Tooltip("Length multiplier for branches at the end of the range. Below 1 gives a conical shape.")]
        [Range(0f, 2f)] public float lengthAtEnd = 0.4f;

        [Range(0f, 1f)] public float lengthVariance = 0.2f;

        [Tooltip("Base radius as a fraction of the parent's radius where it attaches.")]
        [Range(0.05f, 1f)] public float radius = 0.55f;

        [Tooltip("Positive droops towards the ground, negative reaches up.")]
        [Range(-1f, 1f)] public float gravity = 0.15f;

        [Range(0f, 1f)] public float crookedness = 0.2f;

        [Tooltip("Rings along each branch.")]
        [Range(2, 32)] public int segments = 6;

        /// <summary>Sensible starting values for a newly added outer level: fewer, shorter, simpler twigs.</summary>
        public static BranchLevelSettings OuterLevel() => new BranchLevelSettings { count = 4, length = 0.35f, segments = 3 };
    }

    public enum LeafShape
    {
        /// <summary>One double-sided card.</summary>
        Quad,
        /// <summary>Two crossed double-sided cards, fuller from every angle.</summary>
        Cross,
    }

    [Serializable]
    public class LeafSettings
    {
        public bool enabled = true;

        [Tooltip("Leaves per outermost branch.")]
        [Min(0)] public int count = 8;

        [Tooltip("Where leaves start along the branch (0 = base, 1 = tip).")]
        [Range(0f, 1f)] public float start = 0.3f;

        [Tooltip("Card height. Width follows the leaf texture's proportions.")]
        [Min(0.01f)] public float size = 0.4f;

        [Range(0f, 1f)] public float sizeVariance = 0.3f;

        [Tooltip("Angle away from the branch, in degrees.")]
        [Range(0f, 90f)] public float angle = 45f;

        [Tooltip("0: leaves spiral all around the branch. 1: they spread flat to either side, like the shoots on a fir branch or a fern frond.")]
        [Range(0f, 1f)] public float flatten;

        public LeafShape shape = LeafShape.Quad;

        [Tooltip("0: cards face the sky. 1: cards face away from the trunk, which looks fuller from the side.")]
        [Range(0f, 1f)] public float outward = 0.5f;

        [Tooltip("Bends leaf lighting outward from the crown's centre, so the canopy shades as one rounded mass instead of flat cards.")]
        [Range(0f, 1f)] public float canopyNormals = 0.6f;

        [Tooltip("Share of leaves using the leaf atlas's autumn cells.")]
        [Range(0f, 1f)] public float autumn;

        [Tooltip("How often each leaf atlas cell is used, by cell index, on top of the atlas's own weights. 0 leaves a cell out; " +
                 "missing entries count as 1. Edited in the Leaf Mix section.")]
        [HideInInspector] public float[] cellWeights = Array.Empty<float>();
    }

    [Serializable]
    public class MeshSettings
    {
        [Tooltip("Sides around the trunk. Branches use fewer, scaled by their radius.")]
        [Range(3, 32)] public int radialSegments = 8;

        [Range(3, 16)] public int minRadialSegments = 4;
    }
}

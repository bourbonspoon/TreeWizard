using UnityEngine;

namespace TreeWizard
{
    /// <summary>
    /// Sets the wind for every tree drawn with the TreeWizard/Tree shader. The wind blows along this
    /// GameObject's forward (blue) arrow, or follows a WindZone if one is assigned. Without one of
    /// these in the scene, trees stand still.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("TreeWizard/Wind")]
    public class TreeWizardWind : MonoBehaviour
    {
        const string WindProperty = "_TreeWizardWind";
        const string TimingProperty = "_TreeWizardWindTiming";

        [Tooltip("0 is calm.")]
        [Range(0f, 2f)] public float strength = 0.5f;

        [Tooltip("How fast trees sway and gust.")]
        [Range(0.1f, 3f)] public float speed = 1f;

        [Tooltip("Optional. Takes direction from the WindZone, and scales Strength by its Main value.")]
        public WindZone windZone;

        void OnEnable() => Apply();

        void OnValidate() => Apply();

        void Update() => Apply();

        void OnDisable() => Shader.SetGlobalVector(WindProperty, Vector4.zero);

        void Apply()
        {
            var source = windZone != null ? windZone.transform : transform;
            var direction = source.forward;
            var amount = strength * (windZone != null ? windZone.windMain : 1f);
            Shader.SetGlobalVector(WindProperty, new Vector4(direction.x, direction.y, direction.z, amount));
            Shader.SetGlobalVector(TimingProperty, new Vector4(speed, 0f, 0f, 0f));
        }

        void OnDrawGizmos()
        {
            var source = windZone != null ? windZone.transform : transform;
            var start = transform.position;
            var end = start + source.forward * 2f;
            Gizmos.color = new Color(0.4f, 0.8f, 1f);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawLine(end, end - source.forward * 0.4f + source.right * 0.25f);
            Gizmos.DrawLine(end, end - source.forward * 0.4f - source.right * 0.25f);
        }
    }
}

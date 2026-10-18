using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TreeWizard.App
{
    /// <summary>
    /// Viewport camera: left or right drag orbits, middle drag pans, scroll zooms, F frames the tree.
    /// Drags that start over the UI are left to the UI.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public UIDocument ui;
        public Vector3 target = new Vector3(0f, 3f, 0f);
        public float distance = 14f;
        public float yaw = 30f;
        public float pitch = 10f;

        [SerializeField] float orbitSpeed = 0.25f;
        [SerializeField] float zoomSpeed = 0.0015f;
        [SerializeField] Vector2 pitchLimits = new Vector2(-10f, 85f);
        [SerializeField] Vector2 distanceLimits = new Vector2(1f, 200f);

        enum Drag { None, Orbit, Pan }
        Drag _drag;
        Camera _camera;

        void Awake() => _camera = GetComponent<Camera>();

        /// <summary>Points at <paramref name="bounds"/> and backs off far enough to see all of it.</summary>
        public void Frame(Bounds bounds)
        {
            target = bounds.center;
            var halfFov = (_camera != null ? _camera.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad;
            distance = Mathf.Clamp(bounds.extents.magnitude / Mathf.Sin(halfFov) * 1.05f, distanceLimits.x, distanceLimits.y);
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse != null)
                HandleMouse(mouse);
            Apply();
        }

        void HandleMouse(Mouse mouse)
        {
            var position = mouse.position.ReadValue();
            if (_drag == Drag.None && !IsOverUi(position))
            {
                if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                    _drag = Drag.Orbit;
                else if (mouse.middleButton.wasPressedThisFrame)
                    _drag = Drag.Pan;

                var scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f)
                    distance = Mathf.Clamp(distance * (1f - scroll * zoomSpeed), distanceLimits.x, distanceLimits.y);
            }

            var delta = mouse.delta.ReadValue();
            switch (_drag)
            {
                case Drag.Orbit:
                    yaw += delta.x * orbitSpeed;
                    pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, pitchLimits.x, pitchLimits.y);
                    if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed)
                        _drag = Drag.None;
                    break;
                case Drag.Pan:
                    var scale = distance * 0.0015f;
                    target -= (transform.right * delta.x + transform.up * delta.y) * scale;
                    if (!mouse.middleButton.isPressed)
                        _drag = Drag.None;
                    break;
            }
        }

        void Apply()
        {
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(target + rotation * Vector3.back * distance, rotation);
        }

        bool IsOverUi(Vector2 screenPosition)
        {
            var panel = ui != null ? ui.rootVisualElement?.panel : null;
            if (panel == null)
                return false;
            var panelPosition = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return panel.Pick(panelPosition) != null;
        }
    }
}

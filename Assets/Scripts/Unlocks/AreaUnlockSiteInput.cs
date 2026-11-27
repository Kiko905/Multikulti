using Multikulti.Buildings;
using Multikulti.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Multikulti.Unlocks
{
    [RequireComponent(typeof(AreaUnlockService))]
    public sealed class AreaUnlockSiteInput : MonoBehaviour
    {
        [SerializeField, Min(0f)] float siteRadius = 1.5f;
        [SerializeField, Min(0f)] float siteDepthBelowMarker = 0.95f;
        [SerializeField, Range(0.01f, 0.2f)] float markerTapRadius = 0.06f;
        [SerializeField] float maxDistance = 200f;

        AreaUnlockService m_Service;

        void Start() => m_Service = GetComponent<AreaUnlockService>();

        void Update()
        {
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame) return;
            if (m_Service == null || m_Service.Map == null || m_Service.IsOpen) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            var camera = Camera.main;
            if (camera == null) return;

            var screen = pointer.position.ReadValue();
            var ray = camera.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out var hit, maxDistance) && hit.collider.GetComponentInParent<Building>() != null) return;
            if (!TryFindSite(camera, ray, screen, out var need)) return;

            if (!m_Service.TryOpen(need))
                Debug.Log($"Area Unlock {need} nie je dostupný: {m_Service.StateOf(need)}, {m_Service.WaveState.ToJiraName()}");
        }

        bool TryFindSite(Camera camera, Ray ray, Vector2 screen, out Need need)
        {
            need = default;
            var found = false;
            var best = float.MaxValue;
            var markerRadius = Screen.height * markerTapRadius;

            foreach (var site in m_Service.Map.VisibleSites)
            {
                var markerScreen = camera.WorldToScreenPoint(site.MarkerPoint);
                var distance = markerScreen.z > 0f ? Vector2.Distance(screen, markerScreen) : float.MaxValue;
                var onMarker = distance <= markerRadius;

                var ground = new Plane(Vector3.up, site.MarkerPoint - Vector3.up * siteDepthBelowMarker);
                var onSite = false;
                if (ground.Raycast(ray, out var enter))
                {
                    var point = ray.GetPoint(enter);
                    var offset = new Vector2(point.x - site.MarkerPoint.x, point.z - site.MarkerPoint.z);
                    onSite = offset.magnitude <= siteRadius;
                }

                if (!onMarker && !onSite) continue;
                if (found && distance >= best) continue;
                best = distance;
                need = site.Need;
                found = true;
            }
            return found;
        }
    }
}

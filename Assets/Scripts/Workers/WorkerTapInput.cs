using Multikulti.Buildings;
using Multikulti.Waves;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Multikulti.Workers
{
    [RequireComponent(typeof(WorkerManager))]
    public sealed class WorkerTapInput : MonoBehaviour
    {
        [SerializeField] float maxDistance = 200f;

        WorkerManager m_Manager;

        void Start() => m_Manager = GetComponent<WorkerManager>();

        void Update()
        {
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame) return;

            var camera = Camera.main;
            if (camera == null) return;

            var ray = camera.ScreenPointToRay(pointer.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, maxDistance)) return;

            var building = hit.collider.GetComponentInParent<Building>();
            if (building == null) return;

            if (!m_Manager.CanMove())
            {
                Debug.Log($"Presun nie je dostupný: {m_Manager.WaveState.ToJiraName()}");
                return;
            }

            if (m_Manager.TryMoveFrom(building))
                Debug.Log($"Presun z {building.name}, prázdna je teraz budova {m_Manager.Roster.EmptyBuilding}");
        }
    }
}

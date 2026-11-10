using UnityEngine;
using UnityEngine.InputSystem;

namespace Multikulti.Workers
{
    [RequireComponent(typeof(WorkerManager))]
    public sealed class WorkerManagerDebugInput : MonoBehaviour
    {
        WorkerManager m_Manager;

        void Start() => m_Manager = GetComponent<WorkerManager>();

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.nKey.wasPressedThisFrame) return;

            var last = m_Manager.Buildings[m_Manager.Buildings.Count - 1];
            var copy = Instantiate(last, last.transform.position + Vector3.right * 3f, last.transform.rotation, last.transform.parent);
            copy.name = $"{last.name} (debug)";
            m_Manager.AddBuilding(copy);

            Debug.Log($"Budov {m_Manager.Roster.BuildingCount}, Workerov {m_Manager.Roster.WorkerCount}, prázdna {m_Manager.Roster.EmptyBuilding}");
        }
    }
}

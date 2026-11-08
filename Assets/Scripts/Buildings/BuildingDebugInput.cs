using UnityEngine;
using UnityEngine.InputSystem;

namespace Multikulti.Buildings
{
    [RequireComponent(typeof(Building))]
    public sealed class BuildingDebugInput : MonoBehaviour
    {
        Building m_Building;

        void Start() => m_Building = GetComponent<Building>();

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            var queue = m_Building.Queue;

            if (keyboard.spaceKey.wasPressedThisFrame)
                queue.Enqueue(new Unit(m_Building.Need, Time.time), Time.time);

            if (keyboard.wKey.wasPressedThisFrame)
            {
                for (var i = 0; i < 20; i++)
                    queue.Enqueue(new Unit(m_Building.Need, Time.time), Time.time);
                Debug.Log($"{m_Building.Need}: +20 units, backlog {queue.BacklogCount}");
            }

            if (keyboard.sKey.wasPressedThisFrame && queue.Seated.Count > 0)
                queue.CompleteService(queue.Seated[0], Time.time);

            if (keyboard.eKey.wasPressedThisFrame)
            {
                queue.SpeedMultiplier = Mathf.Approximately(queue.SpeedMultiplier, 1f) ? 2f : 1f;
                Debug.Log($"{m_Building.Need}: speed x{queue.SpeedMultiplier}");
            }
        }
    }
}

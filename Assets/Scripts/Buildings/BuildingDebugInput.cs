using UnityEngine;
using UnityEngine.InputSystem;

namespace Multikulti.Buildings
{
    // Temporary: Space adds a unit, S finishes the oldest seated unit.
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

            if (keyboard.sKey.wasPressedThisFrame && queue.Seated.Count > 0)
                queue.CompleteService(queue.Seated[0], Time.time);
        }
    }
}

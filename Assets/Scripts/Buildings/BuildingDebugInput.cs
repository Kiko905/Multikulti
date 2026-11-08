using Multikulti.Config;
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
            var service = WaveConfig.Current.Service;

            if (keyboard.spaceKey.wasPressedThisFrame)
                queue.Enqueue(new Unit(m_Building.Need, Time.time), Time.time);

            if (keyboard.sKey.wasPressedThisFrame && queue.Seated.Count > 0)
                queue.CompleteService(queue.Seated[0], Time.time);

            if (keyboard.uKey.wasPressedThisFrame)
            {
                queue.Utilization = Mathf.Approximately(queue.Utilization, service.UtilizationWorker)
                    ? service.UtilizationEmpty
                    : service.UtilizationWorker;
                Debug.Log($"{m_Building.Need}: utilization {queue.Utilization}");
            }

            if (keyboard.bKey.wasPressedThisFrame)
            {
                queue.SpeedMultiplier = Mathf.Approximately(queue.SpeedMultiplier, 1f) ? 2f : 1f;
                Debug.Log($"{m_Building.Need}: speed x{queue.SpeedMultiplier}");
            }
        }
    }
}

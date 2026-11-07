using Multikulti.Config;
using UnityEngine;

namespace Multikulti.Buildings
{
    public sealed class Building : MonoBehaviour
    {
        [SerializeField] Need need;
        [SerializeField, Min(1)] int level = 1;

        public Need Need => need;
        public BuildingQueue Queue { get; private set; }

        void Awake()
        {
            var capacity = WaveConfig.Current.Buildings.CapacityByLevel[level - 1];
            Queue = new BuildingQueue(need, capacity);
        }

#if UNITY_EDITOR
        void OnGUI()
        {
            if (Queue == null || Camera.main == null) return;
            var pos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up);
            GUI.Label(new Rect(pos.x - 100, Screen.height - pos.y, 200, 20),
                $"{need}: {Queue.Seated.Count}/{Queue.Capacity}, backlog {Queue.BacklogCount}");
        }
#endif
    }
}

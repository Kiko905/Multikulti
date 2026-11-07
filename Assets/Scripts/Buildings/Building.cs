using System.Text;
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
            var config = WaveConfig.Current;
            var capacity = config.Buildings.CapacityByLevel[level - 1];
            Queue = new BuildingQueue(need, capacity, config.Service.ServiceTime)
            {
                Utilization = config.Service.UtilizationWorker
            };
        }

        void Update()
        {
            Queue.Tick(Time.deltaTime, Time.time);
        }

#if UNITY_EDITOR
        readonly StringBuilder m_Label = new StringBuilder();

        void OnGUI()
        {
            if (Queue == null || Camera.main == null) return;

            m_Label.Clear();
            m_Label.Append($"{need}: {Queue.Seated.Count}/{Queue.Capacity}, backlog {Queue.BacklogCount}");
            foreach (var unit in Queue.Seated)
                m_Label.Append($"\n  {unit.ServiceProgress:P0}");

            var pos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up);
            GUI.Label(new Rect(pos.x - 100, Screen.height - pos.y, 200, 100), m_Label.ToString());
        }
#endif
    }
}

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
        bool m_HasWorker;

        public bool HasWorker
        {
            get => m_HasWorker;
            set
            {
                m_HasWorker = value;
                Queue.Utilization = UtilizationRules.For(value, WaveConfig.Current.Service);
            }
        }

        void Awake()
        {
            var config = WaveConfig.Current;
            var capacity = config.Buildings.CapacityByLevel[level - 1];
            Queue = new BuildingQueue(need, capacity, config.Service.ServiceTime)
            {
                Utilization = UtilizationRules.For(m_HasWorker, config.Service)
            };
            Queue.AllServed += () => Debug.Log($"{need}: all {Queue.ServedCount} units served");
        }

        void Update()
        {
            Queue.Tick(Time.deltaTime, Time.time);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Queue.AssertNoUnitLost();
#endif
        }

#if UNITY_EDITOR
        readonly StringBuilder m_Label = new StringBuilder();

        void OnGUI()
        {
            if (Queue == null || Camera.main == null) return;

            m_Label.Clear();
            m_Label.Append(HasWorker ? "[Worker] " : "[ — ] ");
            m_Label.Append($"{Queue.Utilization:0.00}× ");
            m_Label.Append($"{need}: {Queue.Seated.Count}/{Queue.Capacity}, backlog {Queue.BacklogCount}");
            m_Label.Append($", served {Queue.ServedCount}/{Queue.ArrivedCount}");
            if (Queue.ArrivedCount > 0 && Queue.IsAllServed) m_Label.Append(" ✓");
            foreach (var unit in Queue.Seated)
                m_Label.Append($"\n  {unit.ServiceProgress:P0}");

            var pos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up);
            GUI.Label(new Rect(pos.x - 150, Screen.height - pos.y, 300, 120), m_Label.ToString());
        }
#endif
    }
}

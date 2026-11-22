using Multikulti.Economy;
using UnityEngine;

namespace Multikulti.Unlocks
{
    [RequireComponent(typeof(AreaUnlockService))]
    public sealed class AreaUnlockDebugWindow : MonoBehaviour
    {
        const float ReferenceHeight = 720f;

        [SerializeField] ResortBalances balances;

        AreaUnlockService m_Service;

        void Start()
        {
            m_Service = GetComponent<AreaUnlockService>();
            if (balances == null)
                balances = FindAnyObjectByType<ResortBalances>();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnGUI()
        {
            if (m_Service == null || m_Service.Map == null) return;

            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            DrawSiteLabels(scale);
            if (m_Service.Open.HasValue)
                DrawWindow(m_Service.Open.Value, scale);

            GUI.matrix = Matrix4x4.identity;
        }

        void DrawSiteLabels(float scale)
        {
            var camera = Camera.main;
            if (camera == null) return;

            foreach (var site in m_Service.Map.VisibleSites)
            {
                if (!m_Service.Unlocks.TryGet(site.Need, out var unlock)) continue;
                var screen = camera.WorldToScreenPoint(site.MarkerPoint);
                if (screen.z <= 0f) continue;

                var text = m_Service.StateOf(site.Need) == AreaUnlockState.Available
                    ? $"{unlock.Area}: available · {unlock.Price} Coins"
                    : $"{unlock.Area}: od vlny {unlock.AfterWave}";
                var x = screen.x / scale;
                var y = (Screen.height - screen.y) / scale;
                GUI.Label(new Rect(x - 110f, y + 18f, 220f, 24f), text);
            }
        }

        void DrawWindow(AreaUnlock unlock, float scale)
        {
            var width = 320f;
            var height = 170f;
            var area = new Rect((Screen.width / scale - width) * 0.5f, (ReferenceHeight - height) * 0.5f, width, height);
            GUI.Box(area, $"Area Unlock: {unlock.Area}");

            var coins = balances != null ? balances.Coins : 0;
            var missing = Mathf.Max(0, unlock.Price - coins);
            var line = new Rect(area.x + 16f, area.y + 32f, width - 32f, 22f);
            GUI.Label(line, $"Cena: {unlock.Price} Coins");
            line.y += 24f;
            GUI.Label(line, missing > 0 ? $"Chýba: {missing} Coins" : $"Máš: {coins} Coins");
            line.y += 24f;
            GUI.Label(line, "Kúpa príde v SCRUM-51");

            if (GUI.Button(new Rect(area.x + (width - 120f) * 0.5f, area.yMax - 40f, 120f, 28f), "Zavrieť"))
                m_Service.Close();
        }
#endif
    }
}

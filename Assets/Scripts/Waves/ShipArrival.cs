using UnityEngine;

namespace Multikulti.Waves
{
    /// <summary>
    /// Arrival of the guests' ship (SCRUM-23, build document §4.3 WAVE_INCOMING).
    /// While WaveIncoming the ship sails from sailFrom to the pier (dockAt); its progress follows
    /// WaveLoop.IncomingProgress, so the ship reaches the pier exactly when the wave moves on to WaveActive.
    /// The ship stays at the pier during the wave and disappears when the wave is over (AwaitingWave).
    /// A pause stops the ship together with the wave.
    ///
    /// Graybox: without a ship model the component builds a simple box ship; assign a model to replace it.
    /// </summary>
    public sealed class ShipArrival : MonoBehaviour
    {
        [SerializeField] WaveLoop waveLoop;
        [SerializeField, Tooltip("Optional model; a graybox ship is built when empty.")]
        Transform ship;
        [SerializeField] Vector3 sailFrom = new Vector3(-16f, 0.3f, 6f);
        [SerializeField] Vector3 dockAt = new Vector3(-8.5f, 0.3f, 2f);

        Transform m_Ship;

        void Awake()
        {
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
            m_Ship = ship != null ? ship : BuildGrayboxShip();
            m_Ship.gameObject.SetActive(false);
        }

        void Start()
        {
            if (waveLoop == null)
            {
                Debug.LogError("Ship arrival: no WaveLoop in the scene.", this);
                enabled = false;
            }
        }

        void LateUpdate()
        {
            var state = waveLoop.State;
            var visible = state.IsInWave() || state == WaveState.WaveResolving;
            if (m_Ship.gameObject.activeSelf != visible)
                m_Ship.gameObject.SetActive(visible);
            if (!visible) return;

            // WavePaused keeps the last position; IncomingProgress is 1 once the guests arrived.
            var t = Mathf.SmoothStep(0f, 1f, waveLoop.IncomingProgress);
            m_Ship.position = Vector3.Lerp(sailFrom, dockAt, t);
            var heading = dockAt - sailFrom;
            heading.y = 0f;
            if (heading.sqrMagnitude > 0.0001f)
                m_Ship.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
        }

        Transform BuildGrayboxShip()
        {
            var root = new GameObject("Ship (graybox)").transform;
            root.SetParent(transform, false);

            var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.name = "Hull";
            hull.transform.SetParent(root, false);
            hull.transform.localScale = new Vector3(1.2f, 0.5f, 3f);
            hull.transform.localPosition = new Vector3(0f, 0.25f, 0f);

            var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = "Cabin";
            cabin.transform.SetParent(root, false);
            cabin.transform.localScale = new Vector3(0.9f, 0.6f, 1.2f);
            cabin.transform.localPosition = new Vector3(0f, 0.8f, -0.3f);

            // The ship must not take taps meant for the buildings (WorkerTapInput raycasts).
            Destroy(hull.GetComponent<Collider>());
            Destroy(cabin.GetComponent<Collider>());
            return root;
        }
    }
}
